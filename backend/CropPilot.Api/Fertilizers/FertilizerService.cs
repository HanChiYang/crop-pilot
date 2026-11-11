using System.Text;
using CropPilot.Api.Constants;
using CropPilot.Api.Data;
using CropPilot.Contract.Models;
using Microsoft.EntityFrameworkCore;
using static CropPilot.Api.Fertilizers.FertilizerWriteStatus;

namespace CropPilot.Api.Fertilizers;

/// <summary>執行肥料主檔、批次及施肥的業務驗證和交易。</summary>
public sealed class FertilizerService(FertilizerRepository repository)
{
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(8));

    /// <summary>取得帳號的肥料主檔及餘量。</summary>
    internal Task<List<MaterialStock>> ListMaterialsAsync(Guid accountId, CancellationToken ct) =>
        repository.MaterialStocks(accountId).ToListAsync(ct);

    /// <summary>取得資材批次。資材無法存取時回傳 null。</summary>
    internal async Task<List<LotStock>?> ListLotsAsync(Guid accountId, Guid materialId, CancellationToken ct)
    {
        if (!await repository.Materials(accountId).AnyAsync(x => x.Id == materialId, ct))
            return null;

        return await repository.LotStocks(accountId, materialId).ToListAsync(ct);
    }

    /// <summary>建立或更新資材，已有批次時鎖定庫存單位。</summary>
    internal async Task<FertilizerResult<MaterialStock>> SaveMaterialAsync(Guid accountId, Guid? id,
        CreateFertilizerMaterialRqDto request, CancellationToken ct)
    {
        var name = request.Name.Trim();
        var unit = request.StockUnit.Trim();
        if (!ValidText(name, 100))
            return new(null, Invalid, ErrorMessages.FertilizerTextInvalid, "name");
        if (!ValidText(unit, 20))
            return new(null, Invalid, ErrorMessages.FertilizerTextInvalid, "stockUnit");

        await using var transaction = await repository.BeginAsync(ct);
        AgriculturalMaterial material;
        if (id is { } materialId)
        {
            var owned = repository.Materials(accountId).Where(x => x.Id == materialId);
            if (!await owned.AnyAsync(ct))
                return new(null, NotFound);
            await repository.LockMaterialAsync(materialId, ct);
            if (await owned.SingleOrDefaultAsync(ct) is not { } existing)
                return new(null, NotFound);
            material = existing;
            if (unit != material.StockUnit && await repository.Lots(accountId).AnyAsync(x => x.MaterialId == materialId, ct))
                return new(null, Conflict, ErrorMessages.FertilizerUnitLocked, "stockUnit");
            material.Name = name;
            material.StockUnit = unit;
        }
        else
        {
            material = new() { MainAccountId = accountId, Name = name, StockUnit = unit };
            repository.Add(material);
        }

        if (!await repository.SaveAsync(ct))
            return new(null, Conflict, ErrorMessages.FertilizerNameConflict, "name");
        var stock = await repository.MaterialStocks(accountId, material.Id).SingleAsync(ct);
        await transaction.CommitAsync(ct);
        return new(stock, id is null ? Created : Updated);
    }

    /// <summary>刪除無批次的資材。</summary>
    internal async Task<FertilizerResult<AgriculturalMaterial>> DeleteMaterialAsync(Guid accountId, Guid materialId,
        CancellationToken ct)
    {
        await using var transaction = await repository.BeginAsync(ct);
        var owned = repository.Materials(accountId).Where(x => x.Id == materialId);
        if (!await owned.AnyAsync(ct))
            return new(null, NotFound);
        await repository.LockMaterialAsync(materialId, ct);
        if (await owned.SingleOrDefaultAsync(ct) is not { } material)
            return new(null, NotFound);
        if (await repository.Lots(accountId).AnyAsync(x => x.MaterialId == materialId, ct))
            return new(null, Conflict, ErrorMessages.FertilizerMaterialInUse);

        repository.Remove(material);
        await repository.SaveAsync(ct);
        await transaction.CommitAsync(ct);
        return new(null, Deleted);
    }

    /// <summary>建立進貨或期初庫存批次。</summary>
    internal Task<FertilizerResult<LotStock>> CreateLotAsync(Guid accountId, Guid materialId,
        CreateFertilizerLotRqDto request, CancellationToken ct) =>
        !Enum.IsDefined(request.Source)
            ? Task.FromResult(new FertilizerResult<LotStock>(null, Invalid, ErrorMessages.FertilizerSourceInvalid, "source"))
            : SaveLotAsync(accountId, materialId, null, request.Source == CreateFertilizerLotRqDto.SourceEnum.PurchaseEnum
            ? MaterialPurchaseLot.Purchase : MaterialPurchaseLot.OpeningStock,
            request.ReceivedQuantity, request.UnitPrice, request.PurchasedOn, request.AvailableFrom, ct);

    /// <summary>修正原有批次的日期、數量與價格。</summary>
    internal Task<FertilizerResult<LotStock>> EditLotAsync(Guid accountId, Guid materialId, Guid lotId,
        EditFertilizerLotRqDto request, CancellationToken ct) =>
        SaveLotAsync(accountId, materialId, lotId, null, request.ReceivedQuantity, request.UnitPrice,
            request.PurchasedOn, request.AvailableFrom, ct);

    /// <summary>鎖定資材及批次後驗證並儲存，避免與施肥扣量競爭。</summary>
    private async Task<FertilizerResult<LotStock>> SaveLotAsync(Guid accountId, Guid materialId, Guid? lotId,
        string? source, decimal quantity, decimal? price, DateOnly? purchasedOn, DateOnly availableFrom, CancellationToken ct)
    {
        await using var transaction = await repository.BeginAsync(ct);
        var materials = repository.Materials(accountId).Where(x => x.Id == materialId);
        if (!await materials.AnyAsync(ct))
            return new(null, NotFound);
        await repository.LockMaterialAsync(materialId, ct);
        if (!await materials.AnyAsync(ct))
            return new(null, NotFound);

        MaterialPurchaseLot lot;
        if (lotId is { } id)
        {
            var owned = repository.Lots(accountId).Where(x => x.MaterialId == materialId && x.Id == id);
            if (!await owned.AnyAsync(ct))
                return new(null, NotFound);
            await repository.LockLotAsync(id, ct);
            if (await owned.SingleOrDefaultAsync(ct) is not { } existing)
                return new(null, NotFound);
            lot = existing;
        }
        else
        {
            lot = new() { MaterialId = materialId, Source = source! };
        }

        if (!ValidQuantity(quantity) || quantity <= 0)
            return new(null, Invalid, ErrorMessages.FertilizerQuantityInvalid, "receivedQuantity");
        if (price is { } value && !ValidQuantity(value))
            return new(null, Invalid, ErrorMessages.FertilizerPriceInvalid, "unitPrice");
        if (lot.Source == MaterialPurchaseLot.Purchase)
        {
            if (purchasedOn is null || purchasedOn == default(DateOnly) || purchasedOn > Today)
                return new(null, Invalid, ErrorMessages.FertilizerDateInvalid, "purchasedOn");
            if (availableFrom != default && availableFrom != purchasedOn)
                return new(null, Invalid, ErrorMessages.FertilizerDateInvalid, "availableFrom");
            availableFrom = purchasedOn.Value;
        }
        else
        {
            if (availableFrom == default)
                availableFrom = lotId is null ? Today : lot.AvailableFrom;
            if (availableFrom > Today)
                return new(null, Invalid, ErrorMessages.FertilizerDateInvalid, "availableFrom");
            if (purchasedOn > availableFrom)
                return new(null, Invalid, ErrorMessages.FertilizerDateInvalid, "purchasedOn");
        }

        var used = 0m;
        if (lotId is { } existingId)
        {
            var usage = repository.Usage(existingId);
            used = await usage.SumAsync(x => x.Quantity, ct);
            if (quantity < used)
                return new(null, Conflict, ErrorMessages.FertilizerQuantityBelowUsed, "receivedQuantity");
            if (await usage.AnyAsync(x => x.FieldOperation.OperationDate < availableFrom, ct))
                return new(null, Conflict, ErrorMessages.FertilizerLotDateInUse,
                    lot.Source == MaterialPurchaseLot.Purchase ? "purchasedOn" : "availableFrom");
        }

        lot.PurchasedOn = purchasedOn;
        lot.AvailableFrom = availableFrom;
        lot.ReceivedQuantity = quantity;
        lot.UnitPrice = price;
        if (lotId is null)
            repository.Add(lot);
        await repository.SaveAsync(ct);
        await transaction.CommitAsync(ct);
        return new(new(lot, quantity - used), lotId is null ? Created : Updated);
    }

    /// <summary>刪除無施肥明細引用的批次。</summary>
    internal async Task<FertilizerResult<LotStock>> DeleteLotAsync(Guid accountId, Guid materialId, Guid lotId,
        CancellationToken ct)
    {
        await using var transaction = await repository.BeginAsync(ct);
        var owned = repository.Lots(accountId).Where(x => x.MaterialId == materialId && x.Id == lotId);
        if (!await owned.AnyAsync(ct))
            return new(null, NotFound);
        await repository.LockLotAsync(lotId, ct);
        if (await owned.SingleOrDefaultAsync(ct) is not { } lot)
            return new(null, NotFound);
        if (await repository.Usage(lotId).AnyAsync(ct))
            return new(null, Conflict, ErrorMessages.FertilizerLotInUse);
        repository.Remove(lot);
        await repository.SaveAsync(ct);
        await transaction.CommitAsync(ct);
        return new(null, Deleted);
    }

    /// <summary>依實際作業日查詢指定栽培的施肥紀錄。</summary>
    internal async Task<List<FieldOperation>?> ListOperationsAsync(Guid accountId, Guid fieldId, Guid plantingId,
        CancellationToken ct)
    {
        if (!await repository.Plantings(accountId, fieldId).AnyAsync(x => x.Id == plantingId, ct))
            return null;
        return await repository.Operations(plantingId).OrderByDescending(x => x.OperationDate)
            .ThenByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).ToListAsync(ct);
    }

    /// <summary>新增一次施肥，交易成功時扣用所有送出的批次。</summary>
    internal Task<FertilizerResult<FieldOperation>> CreateOperationAsync(Guid accountId, Guid fieldId, Guid plantingId,
        CreateFertilizationRqDto request, CancellationToken ct) =>
        SaveOperationAsync(accountId, fieldId, plantingId, null, request.OperationDate,
            request.Details?.Select(x => x is null ? null : new UsageInput(null, x.MaterialPurchaseLotId, x.Quantity)).ToList(), ct);

    /// <summary>更新施肥及完整的明細清單，釋放移除的批次用量。</summary>
    internal Task<FertilizerResult<FieldOperation>> EditOperationAsync(Guid accountId, Guid fieldId, Guid plantingId,
        Guid operationId, EditFertilizationRqDto request, CancellationToken ct) =>
        SaveOperationAsync(accountId, fieldId, plantingId, operationId, request.OperationDate,
            request.Details?.Select(x => x is null ? null : new UsageInput(x.Id == Guid.Empty ? null : x.Id,
                x.MaterialPurchaseLotId, x.Quantity)).ToList(), ct);

    /// <summary>依栽培及批次順序鎖定資料，驗證後原子更新施肥與庫存。</summary>
    private async Task<FertilizerResult<FieldOperation>> SaveOperationAsync(Guid accountId, Guid fieldId,
        Guid plantingId, Guid? operationId, DateOnly date, List<UsageInput?>? input, CancellationToken ct)
    {
        await using var transaction = await repository.BeginAsync(ct);
        var owned = repository.Plantings(accountId, fieldId).Where(x => x.Id == plantingId);
        if (!await owned.AnyAsync(ct))
            return new(null, NotFound);
        await repository.LockPlantingAsync(plantingId, ct);
        if (await owned.SingleOrDefaultAsync(ct) is not { } planting)
            return new(null, NotFound);
        if (operationId is { } existingOperationId)
            await repository.LockOperationAsync(plantingId, existingOperationId, ct);
        var operation = operationId is { } id
            ? await repository.Operations(plantingId).SingleOrDefaultAsync(x => x.Id == id, ct)
            : new FieldOperation { PlantingId = plantingId };
        if (operation is null)
            return new(null, NotFound);
        if (planting.Status == Planting.Planned)
            return new(null, Conflict, ErrorMessages.FertilizerPlantingPlanned);
        if (date == default || date > Today)
            return new(null, Invalid, ErrorMessages.FertilizerDateInvalid, "operationDate");
        if (input is null || input.Count == 0)
            return new(null, Invalid, ErrorMessages.FertilizerDetailsRequired, "details");

        var original = operation.Details.ToDictionary(x => x.Id);
        var retained = new HashSet<Guid>();
        var rows = new List<UsageInput>(input.Count);
        for (var index = 0; index < input.Count; index++)
        {
            if (input[index] is not { } row)
                return new(null, Invalid, ErrorMessages.FertilizerDetailsRequired, $"details[{index}]");
            if (row.Id is { } detailId && (!original.ContainsKey(detailId) || !retained.Add(detailId)))
                return new(null, Invalid, ErrorMessages.FertilizerDetailInvalid, $"details[{index}].id");
            if (!ValidQuantity(row.Quantity) || row.Quantity <= 0)
                return new(null, Invalid, ErrorMessages.FertilizerQuantityInvalid, $"details[{index}].quantity");
            rows.Add(row);
        }

        var lotIds = rows.Select(x => x.LotId).Concat(original.Values.Select(x => x.MaterialPurchaseLotId))
            .Distinct().Order().ToArray();
        var accountLots = repository.Lots(accountId);
        var ownedLotIds = await accountLots.Where(x => lotIds.Contains(x.Id)).Select(x => x.Id).ToListAsync(ct);
        foreach (var lotId in ownedLotIds.Order())
            await repository.LockLotAsync(lotId, ct);

        var lots = await accountLots.AsNoTracking().Where(x => lotIds.Contains(x.Id))
            .Include(x => x.Material).ToDictionaryAsync(x => x.Id, ct);
        for (var index = 0; index < rows.Count; index++)
        {
            if (!lots.TryGetValue(rows[index].LotId, out var lot))
                return new(null, NotFound);
            if (date < lot.AvailableFrom)
                return new(null, Invalid, ErrorMessages.FertilizerLotUnavailable, $"details[{index}].materialPurchaseLotId");
        }

        foreach (var group in rows.Select((row, index) => (row, index)).GroupBy(x => x.row.LotId))
        {
            var usedElsewhere = await repository.Usage(group.Key)
                .Where(x => x.FieldOperationId != operation.Id).SumAsync(x => x.Quantity, ct);
            if (group.Sum(x => x.row.Quantity) > lots[group.Key].ReceivedQuantity - usedElsewhere)
                return new(null, Conflict, ErrorMessages.FertilizerStockInsufficient, $"details[{group.Last().index}].quantity");
        }

        foreach (var removed in original.Values.Where(x => !retained.Contains(x.Id)))
        {
            operation.Details.Remove(removed);
            repository.Remove(removed);
        }
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var lot = lots[row.LotId];
            var detail = row.Id is { } detailId ? original[detailId] : new FertilizationDetail
            {
                FertilizerName = lot.Material.Name,
                Unit = lot.Material.StockUnit
            };
            if (detail.MaterialPurchaseLotId != lot.Id)
            {
                detail.MaterialPurchaseLotId = lot.Id;
                detail.FertilizerName = lot.Material.Name;
                detail.Unit = lot.Material.StockUnit;
                detail.UnitPrice = lot.UnitPrice;
                detail.CurrencyCode = lot.CurrencyCode;
            }
            detail.Quantity = row.Quantity;
            detail.SortOrder = index;
            if (row.Id is null)
            {
                operation.Details.Add(detail);
                if (operationId is not null)
                    repository.Add(detail);
            }
        }

        operation.OperationDate = date;
        operation.UpdatedAt = DateTimeOffset.UtcNow;
        if (operationId is null)
            repository.Add(operation);
        await repository.SaveAsync(ct);
        // 回傳資料需包含批次所屬資材，且避免附加批次的無追蹤實體。
        var saved = await repository.Operations(plantingId).AsNoTracking().SingleAsync(x => x.Id == operation.Id, ct);
        await transaction.CommitAsync(ct);
        return new(saved, operationId is null ? Created : Updated);
    }

    /// <summary>檢查庫存數值是否可由資料庫欄位精確保存。</summary>
    private static bool ValidQuantity(decimal value) =>
        value is >= 0 and <= 999999999.999999m && decimal.Round(value, 6) == value;

    /// <summary>確認資材文字非空，且符合資料庫字數與字元限制。</summary>
    private static bool ValidText(string value, int maxLength) =>
        value.Length > 0 && !value.Contains('\0') && value.EnumerateRunes().Count() <= maxLength;

    /// <summary>統一新增及編輯請求的明細資料，編輯時可帶原識別碼。</summary>
    private sealed record UsageInput(Guid? Id, Guid LotId, decimal Quantity);
}

/// <summary>表示寫入成功或可預期的錯誤，以及契約對應的欄位。</summary>
internal sealed record FertilizerResult<T>(T? Value, FertilizerWriteStatus Status, string? Message = null, string? Field = null)
    where T : class;


/// <summary>庫存或施肥寫入的業務結果。</summary>
internal enum FertilizerWriteStatus
{
    Created,
    Updated,
    Deleted,
    NotFound,
    Invalid,
    Conflict
}
