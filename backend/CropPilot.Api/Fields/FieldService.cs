using CropPilot.Api.Constants;
using CropPilot.Api.Data;
using CropPilot.Contract.Models;

namespace CropPilot.Api.Fields;

/// <summary>處理新增田區的業務規則，並協調帳號範圍內的田區查詢。</summary>
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
    internal async Task<FieldCreationResult> CreateAsync(
        Guid accountId, CreateFieldRqDto request, CancellationToken cancellationToken)
    {
        var name = request.Name?.Trim() ?? string.Empty;
        var errors = Validate(request, name);
        if (errors.Count > 0)
        {
            return new FieldCreationResult(null, errors);
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
            return new FieldCreationResult(null, errors, NameConflict: true);
        }

        return new FieldCreationResult(field, errors);
    }

    /// <summary>收集名稱、面積、座標及備註的驗證錯誤。</summary>
    private static Dictionary<string, string> Validate(CreateFieldRqDto request, string name)
    {
        var errors = new Dictionary<string, string>();
        if (name.Length == 0 || request.Name.EnumerateRunes().Count() > 120 || name.Contains('\0'))
        {
            errors.Add(nameof(CreateFieldRqDto.Name), ErrorMessages.FieldNameInvalid);
        }

        if (request.AreaSquareMeters is <= 0 or > 9999999999.9999m ||
            request.AreaSquareMeters != decimal.Round(request.AreaSquareMeters, 4))
        {
            errors.Add(nameof(CreateFieldRqDto.AreaSquareMeters), ErrorMessages.FieldAreaInvalid);
        }

        if (request.Latitude.HasValue != request.Longitude.HasValue)
        {
            var missingField = request.Latitude.HasValue ? nameof(CreateFieldRqDto.Longitude) : nameof(CreateFieldRqDto.Latitude);
            errors.Add(missingField, ErrorMessages.FieldCoordinatesMustBePaired);
        }

        if (request.Latitude is decimal latitude && (latitude is < -90 or > 90 || latitude != decimal.Round(latitude, 6)))
        {
            errors.Add(nameof(CreateFieldRqDto.Latitude), ErrorMessages.FieldLatitudeInvalid);
        }

        if (request.Longitude is decimal longitude && (longitude is < -180 or > 180 || longitude != decimal.Round(longitude, 6)))
        {
            errors.Add(nameof(CreateFieldRqDto.Longitude), ErrorMessages.FieldLongitudeInvalid);
        }

        if (request.Notes is { } notes && (notes.EnumerateRunes().Count() > 1000 || notes.Contains('\0')))
        {
            errors.Add(nameof(CreateFieldRqDto.Notes), ErrorMessages.FieldNotesInvalid);
        }

        return errors;
    }
}

/// <summary>記錄新增田區的結果。</summary>
/// <param name="Field">新增成功後儲存的田區。</param>
/// <param name="Errors">以請求屬性名稱為鍵的驗證訊息。</param>
/// <param name="NameConflict">擁有者是否已有同名田區。</param>
internal sealed record FieldCreationResult(Field? Field, IReadOnlyDictionary<string, string> Errors, bool NameConflict = false);
