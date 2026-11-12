using CropPilot.Api.Data;
using CropPilot.Contract.Models;

namespace CropPilot.Api.Irrigations;

/// <summary>組合共用作業與灌溉明細為契約回應。</summary>
internal static class IrrigationDtoMapper
{
    /// <summary>將已載入明細的灌溉作業轉換為回應 DTO。</summary>
    internal static IrrigationRsDto ToDto(FieldOperation operation) => new()
    {
        Id = operation.Id,
        OperationDate = operation.OperationDate,
        Description = operation.Description!,
        Method = ContractEnumJsonConverter<IrrigationMethod>.FromName(operation.IrrigationDetail!.Method),
        WaterVolumeLiters = operation.IrrigationDetail.WaterVolumeLiters
    };
}
