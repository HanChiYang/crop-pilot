using CropPilot.Api.Data;
using CropPilot.Contract.Models;
using Microsoft.AspNetCore.Mvc;

namespace CropPilot.Api.Fertilizers;

/// <summary>將庫存與施肥資料轉換為 OpenAPI 回應。</summary>
internal static class FertilizerDtoMapper
{
    /// <summary>回傳資材及即時餘量。</summary>
    internal static FertilizerMaterialRsDto ToDto(MaterialStock stock) => new()
    {
        Id = stock.Material.Id,
        Name = stock.Material.Name,
        StockUnit = stock.Material.StockUnit,
        RemainingQuantity = stock.Remaining
    };

    /// <summary>回傳進貨或期初批次及即時餘量。</summary>
    internal static FertilizerLotRsDto ToDto(LotStock stock) => new()
    {
        Id = stock.Lot.Id,
        MaterialId = stock.Lot.MaterialId,
        Source = stock.Lot.Source == MaterialPurchaseLot.Purchase
            ? FertilizerLotRsDto.SourceEnum.PurchaseEnum
            : FertilizerLotRsDto.SourceEnum.OpeningStockEnum,
        ReceivedQuantity = stock.Lot.ReceivedQuantity,
        PurchasedOn = stock.Lot.PurchasedOn,
        AvailableFrom = stock.Lot.AvailableFrom,
        UnitPrice = stock.Lot.UnitPrice,
        RemainingQuantity = stock.Remaining,
        CurrencyCode = FertilizerLotRsDto.CurrencyCodeEnum.TWDEnum
    };

    /// <summary>依表單順序回傳作業明細及當時的價格。</summary>
    internal static MaterialApplicationRsDto ToDto(FieldOperation operation) => new()
    {
        Id = operation.Id,
        OperationDate = operation.OperationDate,
        SprayWaterVolumeLiters = operation.Application!.SprayWaterVolumeLiters,
        Details = operation.Application.Details.OrderBy(x => x.SortOrder).Select(x => new MaterialApplicationDetailRsDto
        {
            Id = x.Id,
            MaterialId = x.Lot.MaterialId,
            MaterialPurchaseLotId = x.MaterialPurchaseLotId,
            MaterialName = x.MaterialName,
            MaterialType = x.Lot.Material.MaterialType == AgriculturalMaterial.Pesticide
                ? MaterialApplicationDetailRsDto.MaterialTypeEnum.PesticideEnum
                : MaterialApplicationDetailRsDto.MaterialTypeEnum.FertilizerEnum,
            ApplicationMethod = x.ApplicationMethod == "spray"
                ? MaterialApplicationMethod.SprayEnum : MaterialApplicationMethod.BroadcastEnum,
            Quantity = x.Quantity,
            Unit = x.Unit,
            UnitPrice = x.UnitPrice,
            CurrencyCode = MaterialApplicationDetailRsDto.CurrencyCodeEnum.TWDEnum
        }).ToList()
    };

    /// <summary>回傳農藥主檔、契約庫存單位及合計餘量。</summary>
    internal static PesticideMaterialRsDto ToPesticideDto(MaterialStock stock) => new()
    {
        Id = stock.Material.Id,
        Name = stock.Material.Name,
        StockUnit = stock.Material.StockUnit switch
        {
            "公升" => PesticideStockUnit.LiterEnum,
            "毫升" => PesticideStockUnit.MilliliterEnum,
            "公斤" => PesticideStockUnit.KilogramEnum,
            "公克" => PesticideStockUnit.GramEnum,
            _ => throw new InvalidOperationException()
        },
        RemainingQuantity = stock.Remaining
    };

    /// <summary>回傳農藥批次、剩餘量及可計算的整批成本。</summary>
    internal static PesticideLotRsDto ToPesticideDto(LotStock stock) => new()
    {
        Id = stock.Lot.Id,
        MaterialId = stock.Lot.MaterialId,
        Source = stock.Lot.Source == MaterialPurchaseLot.Purchase
            ? PesticideLotRsDto.SourceEnum.PurchaseEnum : PesticideLotRsDto.SourceEnum.OpeningStockEnum,
        ReceivedQuantity = stock.Lot.ReceivedQuantity,
        PurchasedOn = stock.Lot.PurchasedOn,
        AvailableFrom = stock.Lot.AvailableFrom,
        UnitPrice = stock.Lot.UnitPrice,
        TotalCost = stock.Lot.UnitPrice * stock.Lot.ReceivedQuantity,
        RemainingQuantity = stock.Remaining,
        CurrencyCode = PesticideLotRsDto.CurrencyCodeEnum.TWDEnum
    };

    /// <summary>將服務結果轉換為契約的成功回應或共用問題格式。</summary>
    internal static IActionResult ToResponse<T, TDto>(this ControllerBase controller, FertilizerResult<T> result,
        Func<T, TDto> map) where T : class
    {
        if (result.Value is { } value)
            return result.Status == FertilizerWriteStatus.Created
                ? controller.StatusCode(StatusCodes.Status201Created, map(value))
                : controller.Ok(map(value));
        if (result.Status == FertilizerWriteStatus.NotFound)
            return controller.NotFound();
        if (result.Status == FertilizerWriteStatus.Deleted)
            return controller.NoContent();
        var status = result.Status == FertilizerWriteStatus.Conflict
            ? StatusCodes.Status409Conflict : StatusCodes.Status400BadRequest;
        if (result.Field is null)
            return controller.Problem(statusCode: status, detail: result.Message);

        controller.ModelState.AddModelError(result.Field, result.Message!);
        return controller.ValidationProblem(detail: result.Message, statusCode: status,
            modelStateDictionary: controller.ModelState);
    }
}
