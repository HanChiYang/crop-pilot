using CropPilot.Api.Constants;
using CropPilot.Api.Data;
using CropPilot.Contract.Models;

namespace CropPilot.Api.Plantings;

/// <summary>處理栽培新增驗證及帳號範圍內的查詢。</summary>
public sealed class PlantingService(PlantingRepository repository)
{
    /// <summary>驗證作物文字與狀態日期，並在指定帳號的田區儲存紀錄。</summary>
    internal async Task<PlantingCreateResult> CreateAsync(
        Guid accountId, Guid fieldId, CreatePlantingRqDto request, CancellationToken cancellationToken)
    {
        var cropName = request.CropName.Trim();
        var errors = new Dictionary<string, string>();
        if (cropName.Length == 0 || InvalidText(request.CropName, 120))
        {
            errors["cropName"] = ErrorMessages.PlantingCropNameInvalid;
        }
        if (InvalidText(request.Variety, 120))
        {
            errors["variety"] = ErrorMessages.PlantingVarietyInvalid;
        }
        if (InvalidText(request.Notes, 1000))
        {
            errors["notes"] = ErrorMessages.PlantingNotesInvalid;
        }
        if (request.Status == CreatePlantingRqDto.StatusEnum.GrowingEnum && request.ActualPlantingDate is null)
        {
            errors["actualPlantingDate"] = ErrorMessages.PlantingActualDateRequired;
        }
        if (errors.Count > 0)
        {
            return new(null, errors);
        }

        var planting = new Planting
        {
            FieldId = fieldId,
            CropName = cropName,
            Variety = TrimOptional(request.Variety),
            PlannedPlantingDate = request.PlannedPlantingDate,
            ActualPlantingDate = request.ActualPlantingDate,
            Description = TrimOptional(request.Notes),
            Status = request.Status switch
            {
                CreatePlantingRqDto.StatusEnum.PlannedEnum => Planting.Planned,
                CreatePlantingRqDto.StatusEnum.GrowingEnum => Planting.Growing,
                CreatePlantingRqDto.StatusEnum.HarvestingEnum => Planting.Harvesting,
                _ => throw new InvalidOperationException(ErrorMessages.PlantingStatusInvalid)
            }
        };
        return new(await repository.TryAddAsync(accountId, planting, cancellationToken) ? planting : null, errors);
    }

    /// <summary>查詢指定田區中的單筆紀錄。</summary>
    internal Task<Planting?> FindAsync(Guid accountId, Guid fieldId, Guid plantingId, CancellationToken cancellationToken) =>
        repository.FindAsync(accountId, fieldId, plantingId, cancellationToken);

    /// <summary>查詢指定田區目前的紀錄。</summary>
    internal Task<FieldPlantings?> ListFieldAsync(Guid accountId, Guid fieldId, CancellationToken cancellationToken) =>
        repository.ListFieldAsync(accountId, fieldId, cancellationToken);

    /// <summary>查詢帳號的田區及目前栽培紀錄。</summary>
    internal Task<List<FieldPlantings>> ListOverviewAsync(Guid accountId, CancellationToken cancellationToken) =>
        repository.ListOverviewAsync(accountId, cancellationToken);

    /// <summary>檢查文字是否超過 Unicode 碼點上限或含 PostgreSQL 不支援的空字元。</summary>
    private static bool InvalidText(string? value, int maxLength) =>
        value is not null && (value.Contains('\0') || value.EnumerateRunes().Count() > maxLength);

    /// <summary>去除選填文字前後的空白，空白輸入存為 null。</summary>
    private static string? TrimOptional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>包含新紀錄或欄位錯誤。兩者皆無時表示田區無法存取。</summary>
internal sealed record PlantingCreateResult(Planting? Planting, Dictionary<string, string> Errors);
