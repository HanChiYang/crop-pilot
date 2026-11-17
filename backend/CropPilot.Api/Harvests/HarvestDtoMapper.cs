using CropPilot.Api.Data;
using CropPilot.Contract.Models;

namespace CropPilot.Api.Harvests;

/// <summary>將採收查詢及寫入結果轉為對外契約。</summary>
internal static class HarvestDtoMapper
{
    /// <summary>轉換一筆採收紀錄。</summary>
    internal static HarvestRsDto ToDto(Harvest harvest) => new()
    {
        Id = harvest.Id,
        PlantingId = harvest.PlantingId,
        HarvestDate = harvest.HarvestDate,
        WeightKg = harvest.WeightKg,
        Description = harvest.Description
    };

    /// <summary>轉換採收明細與整批累計。</summary>
    internal static HarvestListRsDto ToDto(HarvestList list) => new()
    {
        TotalWeightKg = list.TotalWeightKg,
        Harvests = list.Records.ConvertAll(ToDto)
    };
}
