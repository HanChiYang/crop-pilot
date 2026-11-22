using CropPilot.Api.Data;
using CropPilot.Api.Fertilizers;
using CropPilot.Contract.Models;

namespace CropPilot.Api.Plantings;

/// <summary>將資料庫的栽培紀錄與田區轉成 API 回應。</summary>
internal static class PlantingDtoMapper
{
    /// <summary>轉換歷史清單摘要並保留原紀錄的品種與占用面積。</summary>
    internal static PlantingHistoryItemDto ToHistoryDto(PlantingHistoryEntry entry) => new()
    {
        Id = entry.Id,
        FieldId = entry.FieldId,
        FieldName = entry.FieldName,
        CropName = entry.CropName,
        Variety = entry.Variety!,
        ActualPlantingDate = entry.ActualPlantingDate,
        CompletionDate = entry.CompletionDate,
        AreaSquareMeters = entry.AreaSquareMeters
    };

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
        CompletionDate = planting.CompletionDate,
        Notes = planting.Description!,
        Status = ToStatus(planting.Status)
    };

    /// <summary>轉換已結束栽培的結束日可填範圍與下限來源。</summary>
    internal static PlantingCompletionDateRangeRsDto ToCompletionDateRangeDto(PlantingCompletionDateRange range) => new()
    {
        CompletionDate = range.CompletionDate,
        EarliestDate = range.EarliestDate,
        EarliestDateSource = range.EarliestDateSource switch
        {
            CompletionDateLowerBoundOrigin.Harvest => CompletionDateLowerBoundSource.HarvestEnum,
            CompletionDateLowerBoundOrigin.MaterialApplication => CompletionDateLowerBoundSource.MaterialApplicationEnum,
            CompletionDateLowerBoundOrigin.Irrigation => CompletionDateLowerBoundSource.IrrigationEnum,
            CompletionDateLowerBoundOrigin.ActualPlanting => CompletionDateLowerBoundSource.ActualPlantingEnum,
            _ => throw new InvalidOperationException(Constants.ErrorMessages.CompletionDateLowerBoundSourceInvalid)
        },
        LatestDate = range.LatestDate
    };

    /// <summary>轉換栽培刪除預覽，包含原批次恢復用量及預覽版本。</summary>
    internal static PlantingDeletePreviewRsDto ToDeletePreviewDto(PlantingDeletion deletion) => new()
    {
        PlantingId = deletion.Planting.Id,
        FieldName = deletion.Planting.Field.Name,
        CropName = deletion.Planting.CropName,
        Status = ToStatus(deletion.Planting.Status),
        ReleasedAreaSquareMeters = deletion.Planting.AreaSquareMeters,
        MaterialApplicationCount = deletion.Operations.Count(value => value.Type == FieldOperation.MaterialApplication),
        IrrigationCount = deletion.Operations.Count(value => value.Type == FieldOperation.Irrigation),
        InventoryReturns = deletion.InventoryReturns.ConvertAll(value => new PlantingInventoryReturnDto
        {
            LotId = value.LotId,
            MaterialName = value.MaterialName,
            Source = value.Source == MaterialPurchaseLot.Purchase
                ? PlantingInventoryReturnDto.SourceEnum.PurchaseEnum : PlantingInventoryReturnDto.SourceEnum.OpeningStockEnum,
            AvailableFrom = value.AvailableFrom,
            Quantity = value.Quantity,
            Unit = MaterialUnitLabels.Display(value.MaterialType, value.Unit)
        }),
        PreviewVersion = deletion.Version()
    };

    /// <summary>將資料庫狀態轉為公開 API 的栽培狀態。</summary>
    private static PlantingStatus ToStatus(string status) => status switch
    {
        Planting.Planned => PlantingStatus.PlannedEnum,
        Planting.Growing => PlantingStatus.GrowingEnum,
        Planting.Harvesting => PlantingStatus.HarvestingEnum,
        Planting.Completed => PlantingStatus.CompletedEnum,
        _ => throw new InvalidOperationException(Constants.ErrorMessages.PlantingStatusInvalid)
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
