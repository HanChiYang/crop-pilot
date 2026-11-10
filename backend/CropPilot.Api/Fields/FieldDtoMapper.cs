using CropPilot.Api.Data;
using CropPilot.Contract.Models;

namespace CropPilot.Api.Fields;

/// <summary>將已儲存的田區資料轉為生成的 API 回應型別。</summary>
internal static class FieldDtoMapper
{
    /// <summary>轉換單筆田區資料，保留座標及備註的空值。</summary>
    internal static FieldDto ToDto(Field field) => new()
    {
        Id = field.Id,
        Name = field.Name,
        AreaSquareMeters = field.AreaSquareMeters,
        Latitude = field.Latitude,
        Longitude = field.Longitude,
        // 契約允許備註為 null，因此轉換時保留空值。
        Notes = field.Description!
    };
}
