using System.Text;
using CropPilot.Api.Constants;
using CropPilot.Api.Data;
using CropPilot.Api.Plantings;
using CropPilot.Contract.Models;

namespace CropPilot.Api.Harvests;

/// <summary>處理採收查詢及寫入的業務驗證。</summary>
public sealed class HarvestService(HarvestRepository repository)
{
    /// <summary>核對查詢參數後回傳採收清單、整批累計或預期錯誤。</summary>
    internal async Task<(HarvestList? List, Dictionary<string, string>? Errors)> ListAsync(Guid accountId,
        Guid plantingId, DateOnly? startDate, DateOnly? endDate, string? sortBy, string? sortDirection,
        CancellationToken ct)
    {
        var errors = new Dictionary<string, string>();
        if (startDate > endDate)
            errors["endDate"] = ErrorMessages.HarvestDateRangeInvalid;
        if (sortBy is not (null or "harvestDate" or "weightKg"))
            errors["sortBy"] = ErrorMessages.HarvestSortByInvalid;
        if (sortDirection is not (null or "asc" or "desc"))
            errors["sortDirection"] = ErrorMessages.HarvestSortDirectionInvalid;
        if (errors.Count > 0)
            return (null, errors);

        var list = await repository.ListAsync(accountId, plantingId, startDate, endDate,
            sortBy == "weightKg", sortDirection == "asc", ct);
        return (list, null);
    }

    /// <summary>替登入帳號的指定栽培新增採收。</summary>
    internal Task<HarvestWriteResult> CreateAsync(Guid accountId, Guid plantingId, SaveHarvestRqDto request,
        CancellationToken ct) => SaveAsync(accountId, plantingId, null, request, ct);

    /// <summary>修正登入帳號擁有的採收，保留原本的識別碼與栽培。</summary>
    internal async Task<HarvestWriteResult> EditAsync(Guid accountId, Guid harvestId, SaveHarvestRqDto request,
        CancellationToken ct)
    {
        var plantingId = await repository.FindPlantingIdAsync(accountId, harvestId, ct);
        return plantingId is Guid id ? await SaveAsync(accountId, id, harvestId, request, ct)
            : new(HarvestWriteStatus.NotFound);
    }

    /// <summary>鎖定所屬栽培後刪除採收。採收已刪除或無法存取時回傳 false。</summary>
    internal async Task<bool> DeleteAsync(Guid accountId, Guid harvestId, CancellationToken ct)
    {
        var plantingId = await repository.FindPlantingIdAsync(accountId, harvestId, ct);
        if (plantingId is not Guid id)
            return false;

        await using var transaction = await repository.BeginAsync(ct);
        if (await repository.LockPlantingStatusAsync(accountId, id, ct) is null ||
            !await repository.DeleteAsync(accountId, id, harvestId, ct))
            return false;

        await transaction.CommitAsync(ct);
        return true;
    }

    /// <summary>在栽培鎖內核對狀態及輸入後一次儲存採收。</summary>
    private async Task<HarvestWriteResult> SaveAsync(Guid accountId, Guid plantingId, Guid? harvestId,
        SaveHarvestRqDto request, CancellationToken ct)
    {
        await using var transaction = await repository.BeginAsync(ct);
        var planting = await repository.LockPlantingStatusAsync(accountId, plantingId, ct);
        if (planting is null)
            return new(HarvestWriteStatus.NotFound);

        var harvest = harvestId is Guid id
            ? await repository.FindAsync(accountId, plantingId, id, ct)
            : new Harvest { PlantingId = plantingId };
        if (harvest is null)
            return new(HarvestWriteStatus.NotFound);
        if (planting.Status == Planting.Planned)
            return new(HarvestWriteStatus.Conflict);

        var errors = Validate(request);
        if (!errors.ContainsKey("harvestDate") && PlantingCompletionRules.DateAfterCompletionError(planting, request.HarvestDate) is { } dateError)
            errors["harvestDate"] = dateError;
        if (errors.Count > 0)
            return new(HarvestWriteStatus.Invalid, Errors: errors);

        harvest.HarvestDate = request.HarvestDate;
        harvest.WeightKg = request.WeightKg;
        harvest.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        harvest.UpdatedAt = DateTimeOffset.UtcNow;
        if (harvestId is null)
            repository.Add(harvest);

        await repository.SaveAsync(ct);
        await transaction.CommitAsync(ct);
        return new(HarvestWriteStatus.Success, harvest);
    }

    /// <summary>檢查實際採收日期、公斤重量及備註字數。</summary>
    private static Dictionary<string, string> Validate(SaveHarvestRqDto request)
    {
        var errors = new Dictionary<string, string>();
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(8));
        if (request.HarvestDate == default || request.HarvestDate > today)
            errors["harvestDate"] = ErrorMessages.HarvestDateInvalid;
        if (request.WeightKg is <= 0 or > 999999999.999999m || decimal.Round(request.WeightKg, 6) != request.WeightKg)
            errors["weightKg"] = ErrorMessages.HarvestWeightInvalid;
        if (request.Description is { } description &&
            (description.Contains('\0') || description.EnumerateRunes().Count() > 500))
            errors["description"] = ErrorMessages.HarvestDescriptionInvalid;
        return errors;
    }
}

/// <summary>採收新增與編輯的結果與欄位錯誤。</summary>
internal sealed record HarvestWriteResult(HarvestWriteStatus Status, Harvest? Harvest = null,
    Dictionary<string, string>? Errors = null);

/// <summary>採收寫入的成功與預期失敗狀態。</summary>
internal enum HarvestWriteStatus { Success, NotFound, Invalid, Conflict }
