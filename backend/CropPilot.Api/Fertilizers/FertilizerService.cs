using System.Text;
using CropPilot.Api.Constants;
using CropPilot.Api.Data;
using CropPilot.Api.Plantings;
using CropPilot.Contract.Models;
using Microsoft.EntityFrameworkCore;
using static CropPilot.Api.Fertilizers.FertilizerWriteStatus;

namespace CropPilot.Api.Fertilizers;

/// <summary>執行肥料主檔、批次及施肥的業務驗證和交易。</summary>
public sealed class FertilizerService(FertilizerRepository repository)
{
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(8));

    /// <summary>取得帳號的肥料主檔及餘量。</summary>
    internal Task<List<MaterialStock>> ListMaterialsAsync(Guid accountId, CancellationToken ct, string type = AgriculturalMaterial.Fertilizer) =>
        repository.MaterialStocks(accountId, type: type).ToListAsync(ct);

    /// <summary>取得資材批次。資材無法存取時回傳 null。</summary>
    internal async Task<List<LotStock>?> ListLotsAsync(Guid accountId, Guid materialId, CancellationToken ct, string type = AgriculturalMaterial.Fertilizer)
    {
        if (!await repository.Materials(accountId, type).AnyAsync(x => x.Id == materialId, ct))
            return null;

        return await repository.LotStocks(accountId, materialId, type).ToListAsync(ct);
    }

    /// <summary>建立或更新帳號的資材規格，已有批次時鎖定包裝與庫存單位。</summary>
    /// <param name="accountId">資材所屬帳號。</param>
    /// <param name="id">更新時指定規格識別碼，建立時留空。</param>
    /// <param name="name">資材名稱。</param>
    /// <param name="unit">用於登記庫存及扣量的單位。</param>
    /// <param name="package">每件包裝的數值與單位。更新時省略會沿用原值。</param>
    /// <param name="ct">取消權杖。</param>
    /// <param name="type">農藥或肥料類型。</param>
    internal async Task<FertilizerResult<MaterialStock>> SaveMaterialAsync(Guid accountId, Guid? id,
        string name, string unit, MaterialPackageDto? package, CancellationToken ct,
        string type = AgriculturalMaterial.Fertilizer)
    {
        name = name.Trim();
        unit = unit.Trim();
        if (!ValidText(name, 100))
            return new(null, Invalid, ErrorMessages.FertilizerTextInvalid, "name");
        if (!ValidText(unit, 20))
            return new(null, Invalid, ErrorMessages.FertilizerTextInvalid, "stockUnit");

        var packageUnit = package?.Unit switch
        {
            MaterialPackageUnit.LiterEnum => "liter",
            MaterialPackageUnit.MilliliterEnum => "milliliter",
            MaterialPackageUnit.KilogramEnum => "kilogram",
            MaterialPackageUnit.GramEnum => "gram",
            _ => null
        };
        if (package is not null)
        {
            if (package.Quantity <= 0 || !ValidQuantity(package.Quantity))
                return new(null, Invalid, ErrorMessages.FertilizerQuantityInvalid, "package.quantity");
            if (packageUnit is null)
                return new(null, Invalid, ErrorMessages.MaterialPackageUnitRequired, "package.unit");
        }

        await using var transaction = await repository.BeginAsync(ct);
        AgriculturalMaterial material;
        if (id is { } materialId)
        {
            var owned = repository.Materials(accountId, type).Where(x => x.Id == materialId);
            if (!await owned.AnyAsync(ct))
                return new(null, NotFound);
            await repository.LockMaterialAsync(materialId, ct);
            if (await owned.SingleOrDefaultAsync(ct) is not { } existing)
                return new(null, NotFound);
            material = existing;
            var stockUnitChanged = unit != material.StockUnit;
            var packageChanged = package is not null &&
                (package.Quantity != material.PackageQuantity || packageUnit != material.PackageUnit);
            if ((stockUnitChanged || packageChanged) &&
                await repository.Lots(accountId, type).AnyAsync(x => x.MaterialId == materialId, ct))
            {
                return stockUnitChanged
                    ? new(null, Conflict, ErrorMessages.FertilizerUnitLocked, "stockUnit")
                    : new(null, Conflict, ErrorMessages.MaterialPackageLocked, "package");
            }
            material.Name = name;
            material.StockUnit = unit;
        }
        else
        {
            material = new() { MainAccountId = accountId, Name = name, StockUnit = unit, MaterialType = type };
            repository.Add(material);
        }

        if (package is not null)
        {
            material.PackageQuantity = package.Quantity;
            material.PackageUnit = packageUnit;
        }
        if (!await repository.SaveAsync(ct))
        {
            var message = material.PackageQuantity is not null ? ErrorMessages.MaterialPackageConflict
                : type == AgriculturalMaterial.Pesticide ? ErrorMessages.PesticideNameConflict
                : ErrorMessages.FertilizerNameConflict;
            return new(null, Conflict, message, "name");
        }
        var stock = await repository.MaterialStocks(accountId, material.Id, type).SingleAsync(ct);
        await transaction.CommitAsync(ct);
        return new(stock, id is null ? Created : Updated);
    }

    /// <summary>刪除登入帳號的無批次資材規格。</summary>
    /// <param name="accountId">呼叫者的帳號。</param>
    /// <param name="materialId">要刪除的規格識別碼。</param>
    /// <param name="ct">要求取消時使用的權杖。</param>
    /// <param name="type">端點對應的資材類型。</param>
    /// <returns>刪除結果。帳號或類型不符時視為找不到，仍有批次時回傳衝突。</returns>
    internal async Task<FertilizerResult<AgriculturalMaterial>> DeleteMaterialAsync(Guid accountId, Guid materialId,
        CancellationToken ct, string type = AgriculturalMaterial.Fertilizer)
    {
        await using var transaction = await repository.BeginAsync(ct);
        var owned = repository.Materials(accountId, type).Where(x => x.Id == materialId);
        if (!await owned.AnyAsync(ct))
            return new(null, NotFound);
        await repository.LockMaterialAsync(materialId, ct);
        if (await owned.SingleOrDefaultAsync(ct) is not { } material)
            return new(null, NotFound);
        if (await repository.Lots(accountId, type).AnyAsync(x => x.MaterialId == materialId, ct))
            return new(null, Conflict, ErrorMessages.MaterialHasLots);

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
    /// <param name="accountId">呼叫者的帳號。</param>
    /// <param name="lotId">要更正的批次識別碼。</param>
    /// <param name="request">更正後的登記量、日期及單價。</param>
    /// <param name="ct">要求取消時使用的權杖。</param>
    /// <param name="type">端點對應的資材類型。</param>
    internal Task<FertilizerResult<LotStock>> EditLotAsync(Guid accountId, Guid lotId,
        EditFertilizerLotRqDto request, CancellationToken ct, string type = AgriculturalMaterial.Fertilizer) =>
        SaveLotAsync(accountId, null, lotId, null, request.ReceivedQuantity, request.UnitPrice,
            request.PurchasedOn, request.AvailableFrom, ct, type);

    /// <summary>依農藥庫存規則建立進貨或期初批次。</summary>
    internal Task<FertilizerResult<LotStock>> CreatePesticideLotAsync(Guid accountId, Guid materialId,
        CreatePesticideLotRqDto request, CancellationToken ct) =>
        !Enum.IsDefined(request.Source)
            ? Task.FromResult(new FertilizerResult<LotStock>(null, Invalid, ErrorMessages.FertilizerSourceInvalid, "source"))
            : SaveLotAsync(accountId, materialId, null, request.Source == CreatePesticideLotRqDto.SourceEnum.PurchaseEnum
                ? MaterialPurchaseLot.Purchase : MaterialPurchaseLot.OpeningStock,
                request.ReceivedQuantity, request.UnitPrice, request.PurchasedOn, request.AvailableFrom, ct,
                AgriculturalMaterial.Pesticide);

    /// <summary>依資材類型查找並鎖定資材及批次，驗證後儲存，避免與施用扣量競爭。</summary>
    private async Task<FertilizerResult<LotStock>> SaveLotAsync(Guid accountId, Guid? requestedMaterialId, Guid? lotId,
        string? source, decimal quantity, decimal? price, DateOnly? purchasedOn, DateOnly availableFrom, CancellationToken ct, string type = AgriculturalMaterial.Fertilizer)
    {
        await using var transaction = await repository.BeginAsync(ct);
        var resolvedMaterialId = requestedMaterialId ?? await repository.Lots(accountId, type)
            .Where(x => x.Id == lotId).Select(x => (Guid?)x.MaterialId).SingleOrDefaultAsync(ct);
        if (resolvedMaterialId is not Guid materialId)
            return new(null, NotFound);

        var materials = repository.Materials(accountId, type).Where(x => x.Id == materialId);
        if (!await materials.AnyAsync(ct))
            return new(null, NotFound);
        await repository.LockMaterialAsync(materialId, ct);
        if (!await materials.AnyAsync(ct))
            return new(null, NotFound);

        MaterialPurchaseLot lot;
        if (lotId is { } id)
        {
            var owned = repository.Lots(accountId, type).Where(x => x.MaterialId == materialId && x.Id == id);
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
        if (price is { } value && !ValidQuantity(value)
            || type == AgriculturalMaterial.Pesticide && lot.Source == MaterialPurchaseLot.Purchase && price is null)
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
            if (await usage.AnyAsync(x => x.Application.FieldOperation.OperationDate < availableFrom, ct))
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

    /// <summary>刪除未被資材施用明細引用的庫存批次。</summary>
    /// <param name="accountId">批次所屬的登入帳號。</param>
    /// <param name="lotId">要刪除的批次識別碼。</param>
    /// <param name="ct">要求取消時使用的權杖。</param>
    /// <param name="type">端點所對應的資材類型。</param>
    /// <returns>刪除結果。帳號或類型不符時視為找不到，已有施用明細時回傳衝突。</returns>
    internal async Task<FertilizerResult<LotStock>> DeleteLotAsync(Guid accountId, Guid lotId,
        CancellationToken ct, string type = AgriculturalMaterial.Fertilizer)
    {
        await using var transaction = await repository.BeginAsync(ct);
        var owned = repository.Lots(accountId, type).Where(x => x.Id == lotId);
        if (!await owned.AnyAsync(ct))
            return new(null, NotFound);
        await repository.LockLotAsync(lotId, ct);
        if (await owned.SingleOrDefaultAsync(ct) is not { } lot)
            return new(null, NotFound);
        if (await repository.Usage(lotId).AnyAsync(ct))
            return new(null, Conflict, ErrorMessages.MaterialLotInUse);
        repository.Remove(lot);
        await repository.SaveAsync(ct);
        await transaction.CommitAsync(ct);
        return new(null, Deleted);
    }

    /// <summary>依實際作業日查詢指定栽培的施肥紀錄。</summary>
    internal async Task<List<FieldOperation>?> ListOperationsAsync(Guid accountId, Guid plantingId,
        CancellationToken ct)
    {
        if (!await repository.Plantings(accountId).AnyAsync(x => x.Id == plantingId, ct))
            return null;
        return await repository.Operations(plantingId).OrderByDescending(x => x.OperationDate)
            .ThenByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).ToListAsync(ct);
    }

    /// <summary>新增一次施肥，交易成功時扣用所有送出的批次。</summary>
    internal Task<FertilizerResult<FieldOperation>> CreateOperationAsync(Guid accountId, Guid plantingId,
        CreateMaterialApplicationRqDto request, CancellationToken ct) =>
        SaveOperationAsync(accountId, plantingId, null, request.OperationDate, request.ApplicationMethod, request.SprayWaterVolumeLiters,
            request.Description,
            request.Details?.Select(x => x is null ? null : new UsageInput(null, x.MaterialPurchaseLotId, x.Quantity)).ToList(), ct);

    /// <summary>更新施肥及完整的明細清單，釋放移除的批次用量。</summary>
    internal Task<FertilizerResult<FieldOperation>> EditOperationAsync(Guid accountId,
        Guid operationId, EditMaterialApplicationRqDto request, CancellationToken ct) =>
        SaveOperationAsync(accountId, null, operationId, request.OperationDate, request.ApplicationMethod, request.SprayWaterVolumeLiters,
            request.Description,
            request.Details?.Select(x => x is null ? null : new UsageInput(x.Id == Guid.Empty ? null : x.Id,
                x.MaterialPurchaseLotId, x.Quantity)).ToList(), ct);

    /// <summary>依栽培及批次順序鎖定資料，驗證後原子更新施肥與庫存。</summary>
    private async Task<FertilizerResult<FieldOperation>> SaveOperationAsync(Guid accountId,
        Guid? requestedPlantingId, Guid? operationId, DateOnly date, MaterialApplicationMethod method, decimal? water,
        string? description, List<UsageInput?>? input, CancellationToken ct)
    {
        await using var transaction = await repository.BeginAsync(ct);
        var resolvedPlantingId = operationId is Guid requestedOperationId
            ? await repository.FindOperationPlantingIdAsync(accountId, requestedOperationId, ct)
            : requestedPlantingId;
        if (resolvedPlantingId is not Guid plantingId)
            return new(null, NotFound);

        var owned = repository.Plantings(accountId).Where(x => x.Id == plantingId);
        if (!await owned.AnyAsync(ct))
            return new(null, NotFound);
        await repository.LockPlantingAsync(plantingId, ct);
        var planting = await repository.FindPlantingStateAsync(accountId, plantingId, ct);
        if (planting is null)
            return new(null, NotFound);
        if (operationId is { } existingOperationId)
            await repository.LockOperationAsync(plantingId, existingOperationId, ct);
        var operation = operationId is { } id
            ? await repository.Operations(plantingId).SingleOrDefaultAsync(x => x.Id == id, ct)
            : new FieldOperation { PlantingId = plantingId, Application = new MaterialApplication() };
        if (operation is null)
            return new(null, NotFound);
        if (date == default || date > Today)
            return new(null, Invalid, ErrorMessages.FertilizerDateInvalid, "operationDate");
        if (PlantingCompletionRules.DateAfterCompletionError(planting, date) is { } dateError)
            return new(null, Invalid, dateError, "operationDate");
        if (input is null || input.Count == 0)
            return new(null, Invalid, ErrorMessages.FertilizerDetailsRequired, "details");

        if (!Enum.IsDefined(method))
            return new(null, Invalid, ErrorMessages.MaterialApplicationMethodInvalid, "applicationMethod");
        if (description is not null && (description.Contains('\0') || description.EnumerateRunes().Count() > 500))
            return new(null, Invalid, ErrorMessages.MaterialApplicationDescriptionInvalid, "description");

        var application = operation.Application!;
        var original = application.Details.ToDictionary(x => x.Id);
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

        if (water is { } liters && (!ValidQuantity(liters) || liters <= 0
            || method != MaterialApplicationMethod.SprayEnum))
            return new(null, Invalid, ErrorMessages.MaterialApplicationWaterInvalid, "sprayWaterVolumeLiters");

        var lotIds = rows.Select(x => x.LotId).Concat(original.Values.Select(x => x.MaterialPurchaseLotId))
            .Distinct().Order().ToArray();
        var accountLots = repository.Lots(accountId, null);
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
                .Where(x => x.MaterialApplicationId != application.Id).SumAsync(x => x.Quantity, ct);
            var remaining = lots[group.Key].ReceivedQuantity - usedElsewhere;
            foreach (var (row, index) in group)
            {
                remaining -= row.Quantity;
                if (remaining < 0)
                    return new(null, Conflict, ErrorMessages.FertilizerStockInsufficient, $"details[{index}].quantity");
            }
        }

        foreach (var removed in original.Values.Where(x => !retained.Contains(x.Id)))
        {
            application.Details.Remove(removed);
            repository.Remove(removed);
        }
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var lot = lots[row.LotId];
            var detail = row.Id is { } detailId ? original[detailId] : new MaterialApplicationDetail();
            detail.MaterialPurchaseLotId = lot.Id;
            detail.Quantity = row.Quantity;
            detail.SortOrder = index;
            if (row.Id is null)
            {
                application.Details.Add(detail);
                if (operationId is not null)
                    repository.Add(detail);
            }
        }

        application.ApplicationMethod = method == MaterialApplicationMethod.SprayEnum ? "spray" : "broadcast";
        application.SprayWaterVolumeLiters = water;
        operation.Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
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

    /// <summary>刪除登入帳號的施用作業，並在同一交易釋放各批次用量。</summary>
    internal async Task<FertilizerResult<FieldOperation>> DeleteOperationAsync(Guid accountId, Guid operationId,
        CancellationToken ct)
    {
        await using var transaction = await repository.BeginAsync(ct);
        if (await repository.FindOperationPlantingIdAsync(accountId, operationId, ct) is not { } plantingId)
            return new(null, NotFound);
        await repository.LockPlantingAsync(plantingId, ct);
        await repository.LockOperationAsync(plantingId, operationId, ct);
        if (await repository.Operations(plantingId).SingleOrDefaultAsync(x => x.Id == operationId, ct) is not { } operation)
            return new(null, NotFound);

        foreach (var lotId in operation.Application!.Details.Select(x => x.MaterialPurchaseLotId).Distinct().Order())
            await repository.LockLotAsync(lotId, ct);

        repository.Remove(operation);
        await repository.SaveAsync(ct);
        await transaction.CommitAsync(ct);
        return new(null, Deleted);
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
