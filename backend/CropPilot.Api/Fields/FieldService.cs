using CropPilot.Api.Constants;
using CropPilot.Api.Data;
using CropPilot.Contract.Models;

namespace CropPilot.Api.Fields;

/// <summary>驗證田區輸入，並協調帳號範圍內的田區讀寫。</summary>
public sealed class FieldService(FieldRepository repository)
{
    /// <summary>取得指定帳號擁有的田區清單。</summary>
    internal Task<List<Field>> ListAsync(Guid accountId, CancellationToken cancellationToken) =>
        repository.ListAsync(accountId, cancellationToken);

    /// <summary>取得指定帳號擁有的田區。查無該田區時回傳 <see langword="null"/>。</summary>
    internal Task<Field?> FindAsync(Guid accountId, Guid fieldId, CancellationToken cancellationToken) =>
        repository.FindAsync(accountId, fieldId, cancellationToken);

    /// <summary>移除名稱頭尾空白，驗證請求，並為擁有者儲存田區。</summary>
    /// <returns>儲存成功時回傳田區。輸入無效或名稱重複時回傳對應的欄位錯誤。</returns>
    internal async Task<FieldSaveResult> CreateAsync(
        Guid accountId, CreateFieldRqDto request, CancellationToken cancellationToken)
    {
        var name = request.Name?.Trim() ?? string.Empty;
        var errors = Validate(request.Name, name, request.AreaSquareMeters, request.Latitude, request.Longitude, request.Notes);
        if (errors.Count > 0)
        {
            return new FieldSaveResult(null, errors);
        }

        var field = new Field
        {
            MainAccountId = accountId,
            Name = name,
            AreaSquareMeters = request.AreaSquareMeters,
            Latitude = request.Latitude,
            Longitude = request.Longitude,
            Description = request.Notes
        };

        if (!await repository.TryAddAsync(field, cancellationToken))
        {
            errors.Add(nameof(CreateFieldRqDto.Name), ErrorMessages.FieldNameAlreadyUsed);
            return new FieldSaveResult(null, errors, FieldWriteStatus.NameConflict);
        }

        return new FieldSaveResult(field, errors, FieldWriteStatus.Success);
    }

    /// <summary>驗證完整編輯表單，並更新指定帳號擁有的田區。</summary>
    /// <returns>成功時回傳更新後的田區，失敗時提供欄位錯誤或找不到田區的結果。</returns>
    internal async Task<FieldSaveResult> EditAsync(Guid accountId, Guid fieldId, EditFieldRqDto request,
        CancellationToken cancellationToken)
    {
        var name = request.Name?.Trim() ?? string.Empty;
        var errors = Validate(request.Name, name, request.AreaSquareMeters, request.Latitude, request.Longitude, request.Notes);
        if (errors.Count > 0)
        {
            return new FieldSaveResult(null, errors);
        }

        var field = new Field
        {
            Id = fieldId,
            MainAccountId = accountId,
            Name = name,
            AreaSquareMeters = request.AreaSquareMeters,
            Latitude = request.Latitude,
            Longitude = request.Longitude,
            Description = request.Notes
        };

        var status = await repository.UpdateAsync(field, cancellationToken);
        if (status == FieldWriteStatus.NameConflict)
        {
            errors.Add(nameof(EditFieldRqDto.Name), ErrorMessages.FieldNameAlreadyUsed);
        }
        if (status == FieldWriteStatus.AreaConflict)
        {
            errors.Add(nameof(EditFieldRqDto.AreaSquareMeters), ErrorMessages.FieldAreaBelowAllocated);
        }

        return new FieldSaveResult(status == FieldWriteStatus.Success ? field : null, errors, status);
    }

    /// <summary>刪除一筆田區，或回傳找不到田區及關聯資料衝突的結果。</summary>
    internal Task<FieldWriteStatus> DeleteAsync(Guid accountId, Guid fieldId, CancellationToken cancellationToken) =>
        repository.DeleteAsync(accountId, [fieldId], cancellationToken);

    /// <summary>驗證批次田區 ID 至少有一筆且不重複，再刪除全部指定田區。</summary>
    internal Task<FieldWriteStatus> DeleteAsync(Guid accountId, DeleteFieldRqDto request, CancellationToken cancellationToken) =>
        request.FieldIds is { Count: > 0 } ids && ids.Distinct().Count() == ids.Count
            ? repository.DeleteAsync(accountId, [.. ids], cancellationToken)
            : Task.FromResult(FieldWriteStatus.InvalidInput);

    /// <summary>收集名稱、面積、座標及備註的驗證錯誤。</summary>
    private static Dictionary<string, string> Validate(string? rawName, string name, decimal area,
        decimal? latitude, decimal? longitude, string? notes)
    {
        var errors = new Dictionary<string, string>();
        if (rawName is null || name.Length == 0 || rawName.EnumerateRunes().Count() > 120 || name.Contains('\0'))
        {
            errors.Add(nameof(CreateFieldRqDto.Name), ErrorMessages.FieldNameInvalid);
        }

        if (area is <= 0 or > 9999999999.9999m || area != decimal.Round(area, 4))
        {
            errors.Add(nameof(CreateFieldRqDto.AreaSquareMeters), ErrorMessages.FieldAreaInvalid);
        }

        if (latitude.HasValue != longitude.HasValue)
        {
            var missingField = latitude.HasValue ? nameof(CreateFieldRqDto.Longitude) : nameof(CreateFieldRqDto.Latitude);
            errors.Add(missingField, ErrorMessages.FieldCoordinatesMustBePaired);
        }

        if (latitude is decimal lat && (lat is < -90 or > 90 || lat != decimal.Round(lat, 6)))
        {
            errors.Add(nameof(CreateFieldRqDto.Latitude), ErrorMessages.FieldLatitudeInvalid);
        }

        if (longitude is decimal lng && (lng is < -180 or > 180 || lng != decimal.Round(lng, 6)))
        {
            errors.Add(nameof(CreateFieldRqDto.Longitude), ErrorMessages.FieldLongitudeInvalid);
        }

        if (notes is not null && (notes.EnumerateRunes().Count() > 1000 || notes.Contains('\0')))
        {
            errors.Add(nameof(CreateFieldRqDto.Notes), ErrorMessages.FieldNotesInvalid);
        }

        return errors;
    }
}

/// <summary>記錄儲存田區的結果。</summary>
/// <param name="Field">成功儲存的田區。</param>
/// <param name="Errors">以請求屬性名稱為鍵的驗證訊息。</param>
/// <param name="Status">儲存結果，包含名稱衝突與找不到田區。</param>
internal sealed record FieldSaveResult(Field? Field, IReadOnlyDictionary<string, string> Errors,
    FieldWriteStatus Status = FieldWriteStatus.InvalidInput);
