using CropPilot.Api.Data;
using CropPilot.Contract.Models;

namespace CropPilot.Api.Plantings;

/// <summary>將資料庫的栽培紀錄與田區轉成 API 回應。</summary>
internal static class PlantingDtoMapper
{
    /// <summary>轉換栽培紀錄，保留選填欄位空值並轉換資料庫狀態。</summary>
    internal static PlantingRsDto ToDto(Planting planting) => new()
    {
        Id = planting.Id,
        FieldId = planting.FieldId,
        CropName = planting.CropName,
        AreaSquareMeters = planting.AreaSquareMeters,
        Variety = planting.Variety!,
        PlannedPlantingDate = planting.PlannedPlantingDate,
        ActualPlantingDate = planting.ActualPlantingDate,
        Notes = planting.Description!,
        Status = planting.Status switch
        {
            Planting.Planned => PlantingStatus.PlannedEnum,
            Planting.Growing => PlantingStatus.GrowingEnum,
            Planting.Harvesting => PlantingStatus.HarvestingEnum,
            Planting.Completed => PlantingStatus.CompletedEnum,
            _ => throw new InvalidOperationException(Constants.ErrorMessages.PlantingStatusInvalid)
        }
    };

    /// <summary>從同一批目前紀錄計算已分配與剩餘面積。</summary>
    internal static FieldPlantingsRsDto ToFieldPlantingsDto(FieldPlantings entry)
    {
        var allocated = entry.Plantings.Sum(planting => planting.AreaSquareMeters);
        return new()
        {
            AreaSquareMeters = entry.Field.AreaSquareMeters,
            AllocatedAreaSquareMeters = allocated,
            RemainingAreaSquareMeters = entry.Field.AreaSquareMeters - allocated,
            Plantings = entry.Plantings.ConvertAll(ToDto)
        };
    }

    /// <summary>轉換田區及該田區目前的栽培清單。</summary>
    internal static FieldOverviewRsDto ToOverviewDto(FieldPlantings entry) => new()
    {
        Id = entry.Field.Id,
        Name = entry.Field.Name,
        AreaSquareMeters = entry.Field.AreaSquareMeters,
        Latitude = entry.Field.Latitude,
        Longitude = entry.Field.Longitude,
        Notes = entry.Field.Description!,
        Plantings = entry.Plantings.ConvertAll(ToDto)
    };
}
