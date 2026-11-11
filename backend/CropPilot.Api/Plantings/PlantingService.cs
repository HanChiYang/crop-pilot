using CropPilot.Api.Constants;
using CropPilot.Api.Data;
using CropPilot.Contract.Models;

namespace CropPilot.Api.Plantings;

/// <summary>驗證栽培欄位、狀態轉換與占用面積，並協調帳號範圍內的讀寫。</summary>
public sealed class PlantingService(PlantingRepository repository)
{
    /// <summary>驗證作物文字與狀態日期，並在指定帳號的田區儲存紀錄。</summary>
    internal Task<PlantingWriteResult> CreateAsync(
        Guid accountId, Guid fieldId, CreatePlantingRqDto request, CancellationToken cancellationToken)
    {
        var status = request.Status switch
        {
            CreatePlantingRqDto.StatusEnum.PlannedEnum => Planting.Planned,
            CreatePlantingRqDto.StatusEnum.GrowingEnum => Planting.Growing,
            CreatePlantingRqDto.StatusEnum.HarvestingEnum => Planting.Harvesting,
            _ => throw new InvalidOperationException(ErrorMessages.PlantingStatusInvalid)
        };
        var errors = Validate(request.CropName, request.Variety, request.Notes, request.AreaSquareMeters,
            status, request.ActualPlantingDate);
        if (errors.Count > 0)
        {
            return Task.FromResult(new PlantingWriteResult(PlantingWriteStatus.InvalidInput, Errors: errors));
        }

        var planting = new Planting
        {
            FieldId = fieldId,
            CropName = request.CropName.Trim(),
            AreaSquareMeters = request.AreaSquareMeters,
            Variety = TrimOptional(request.Variety),
            PlannedPlantingDate = request.PlannedPlantingDate,
            ActualPlantingDate = request.ActualPlantingDate,
            Description = TrimOptional(request.Notes),
            Status = status
        };
        return repository.WriteAsync(accountId, fieldId, null, (_, available) =>
            planting.AreaSquareMeters > available
                ? AreaConflict(available)
                : new(PlantingWriteStatus.Success, planting), cancellationToken);
    }

    /// <summary>在田區鎖定後核對狀態轉換及可分配面積，再一起更新整份表單。</summary>
    internal Task<PlantingWriteResult> EditAsync(Guid accountId, Guid fieldId, Guid plantingId,
        EditPlantingRqDto request, CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(request.Status))
        {
            return Task.FromResult(new PlantingWriteResult(PlantingWriteStatus.InvalidInput,
                Errors: new() { ["status"] = ErrorMessages.PlantingStatusInvalid }));
        }
        var status = request.Status switch
        {
            PlantingStatus.PlannedEnum => Planting.Planned,
            PlantingStatus.GrowingEnum => Planting.Growing,
            PlantingStatus.HarvestingEnum => Planting.Harvesting,
            PlantingStatus.CompletedEnum => Planting.Completed,
            _ => throw new InvalidOperationException(ErrorMessages.PlantingStatusInvalid)
        };
        var errors = Validate(request.CropName, request.Variety, request.Notes, request.AreaSquareMeters,
            status, request.ActualPlantingDate);
        if (errors.Count > 0)
        {
            return Task.FromResult(new PlantingWriteResult(PlantingWriteStatus.InvalidInput, Errors: errors));
        }

        return repository.WriteAsync(accountId, fieldId, plantingId, (current, available) =>
        {
            var planting = current!;
            if (!CanTransition(planting.Status, status))
            {
                return new(PlantingWriteStatus.Conflict, Errors: new() { ["status"] = ErrorMessages.PlantingTransitionInvalid });
            }
            if (status != Planting.Completed && request.AreaSquareMeters > available)
            {
                return AreaConflict(available);
            }

            planting.CropName = request.CropName.Trim();
            planting.Variety = TrimOptional(request.Variety);
            planting.Status = status;
            planting.PlannedPlantingDate = request.PlannedPlantingDate;
            planting.ActualPlantingDate = request.ActualPlantingDate;
            planting.AreaSquareMeters = request.AreaSquareMeters;
            planting.Description = TrimOptional(request.Notes);
            return new(PlantingWriteStatus.Success, planting);
        }, cancellationToken);
    }

    /// <summary>只刪除儲存當下仍為規劃中的紀錄。</summary>
    internal Task<PlantingWriteResult> DeleteAsync(Guid accountId, Guid fieldId, Guid plantingId,
        CancellationToken cancellationToken) =>
        repository.WriteAsync(accountId, fieldId, plantingId, (current, _) =>
            new(current!.Status == Planting.Planned ? PlantingWriteStatus.Success : PlantingWriteStatus.Conflict),
            cancellationToken);

    /// <summary>檢查未結束紀錄能否維持原狀態或沿栽培流程前進。</summary>
    private static bool CanTransition(string current, string next) => (current, next) switch
    {
        (Planting.Planned, Planting.Planned or Planting.Growing) => true,
        (Planting.Growing, Planting.Growing or Planting.Harvesting or Planting.Completed) => true,
        (Planting.Harvesting, Planting.Harvesting or Planting.Completed) => true,
        _ => false
    };

    /// <summary>以面積欄位錯誤回傳交易中計算出的可分配面積。</summary>
    private static PlantingWriteResult AreaConflict(decimal available) =>
        new(PlantingWriteStatus.Conflict, Errors: new()
        {
            ["areaSquareMeters"] = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                ErrorMessages.PlantingAreaExceedsAvailable, available)
        });

    /// <summary>驗證新增和編輯共用的文字、面積及狀態日期規則。</summary>
    private static Dictionary<string, string> Validate(string? cropName, string? variety, string? notes,
        decimal area, string status, DateOnly? actualDate)
    {
        var errors = new Dictionary<string, string>();
        if (string.IsNullOrWhiteSpace(cropName) || InvalidText(cropName, 120))
        {
            errors["cropName"] = ErrorMessages.PlantingCropNameInvalid;
        }
        if (InvalidText(variety, 120))
        {
            errors["variety"] = ErrorMessages.PlantingVarietyInvalid;
        }
        if (InvalidText(notes, 1000))
        {
            errors["notes"] = ErrorMessages.PlantingNotesInvalid;
        }
        if (area is <= 0 or > 9999999999.9999m || area != decimal.Round(area, 4))
        {
            errors["areaSquareMeters"] = ErrorMessages.PlantingAreaInvalid;
        }
        if (status is Planting.Growing or Planting.Completed && actualDate is null)
        {
            errors["actualPlantingDate"] = ErrorMessages.PlantingActualDateRequired;
        }
        return errors;
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

    /// <summary>驗證明確提供的作物篩選名稱，允許至多 120 個 Unicode 碼點。</summary>
    internal static bool IsHistoryCropNameValid(string? cropName) =>
        !string.IsNullOrWhiteSpace(cropName) && !InvalidText(cropName, 120);

    /// <summary>查詢已結束的紀錄。指定田區無法存取時回傳 null。</summary>
    internal async Task<List<PlantingHistoryEntry>?> ListHistoryAsync(
        Guid accountId, Guid? fieldId, string? cropName, CancellationToken cancellationToken)
    {
        if (fieldId is Guid id && !await repository.OwnsFieldAsync(accountId, id, cancellationToken))
        {
            return null;
        }

        return await repository.ListHistoryAsync(accountId, fieldId, cropName, cancellationToken);
    }

    /// <summary>查詢已結束紀錄的作物名稱。指定田區無法存取時回傳 null。</summary>
    internal async Task<List<string>?> ListHistoryCropsAsync(
        Guid accountId, Guid? fieldId, CancellationToken cancellationToken)
    {
        if (fieldId is Guid id && !await repository.OwnsFieldAsync(accountId, id, cancellationToken))
        {
            return null;
        }

        return await repository.ListHistoryCropsAsync(accountId, fieldId, cancellationToken);
    }

    /// <summary>檢查文字是否超過 Unicode 碼點上限或含 PostgreSQL 不支援的空字元。</summary>
    private static bool InvalidText(string? value, int maxLength) =>
        value is not null && (value.Contains('\0') || value.EnumerateRunes().Count() > maxLength);

    /// <summary>去除選填文字前後的空白，空白輸入存為 null。</summary>
    private static string? TrimOptional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>表示栽培寫入的可預期結果。</summary>
internal enum PlantingWriteStatus { Success, InvalidInput, NotFound, Conflict }

/// <summary>包含寫入狀態、儲存後的紀錄或欄位錯誤。</summary>
internal sealed record PlantingWriteResult(PlantingWriteStatus Status, Planting? Planting = null,
    Dictionary<string, string>? Errors = null);
