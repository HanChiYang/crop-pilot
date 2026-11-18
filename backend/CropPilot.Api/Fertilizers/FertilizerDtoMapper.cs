using System.Text.Json.Serialization.Metadata;
using CropPilot.Api.Data;
using CropPilot.Contract.Models;
using Microsoft.AspNetCore.Mvc;

namespace CropPilot.Api.Fertilizers;

/// <summary>將庫存與施肥資料轉換為 OpenAPI 回應。</summary>
internal static class FertilizerDtoMapper
{
    /// <summary>在規格回應中省略舊資料尚未填寫的包裝欄位。</summary>
    /// <param name="type">目前設定的 JSON 契約型別。</param>
    internal static void ConfigurePackageSerialization(JsonTypeInfo type)
    {
        if (type.Type == typeof(FertilizerMaterialRsDto) || type.Type == typeof(PesticideMaterialRsDto))
            type.Properties.Single(property => property.Name == "package").ShouldSerialize = (_, value) => value is not null;
    }

    /// <summary>回傳肥料規格及即時餘量。</summary>
    /// <param name="stock">肥料主檔及按規格彙總的餘量。</param>
    internal static FertilizerMaterialRsDto ToDto(MaterialStock stock) => new()
    {
        Id = stock.Material.Id,
        Name = stock.Material.Name,
        StockUnit = stock.Material.StockUnit,
        Package = ToPackageDto(stock.Material),
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

    /// <summary>依表單順序回傳作業明細，名稱、單位及單價採用批次與資材主檔目前的值。</summary>
    internal static MaterialApplicationRsDto ToDto(FieldOperation operation) => new()
    {
        Id = operation.Id,
        OperationDate = operation.OperationDate,
        Description = operation.Description,
        ApplicationMethod = ContractEnumJsonConverter<MaterialApplicationMethod>.FromCode(operation.Application!.ApplicationMethod),
        SprayWaterVolumeLiters = operation.Application.SprayWaterVolumeLiters,
        Details = operation.Application.Details.OrderBy(x => x.SortOrder).Select(x => new MaterialApplicationDetailRsDto
        {
            Id = x.Id,
            MaterialId = x.Lot.MaterialId,
            MaterialPurchaseLotId = x.MaterialPurchaseLotId,
            MaterialName = x.Lot.Material.Name,
            MaterialType = x.Lot.Material.MaterialType == AgriculturalMaterial.Pesticide
                ? MaterialApplicationDetailRsDto.MaterialTypeEnum.PesticideEnum
                : MaterialApplicationDetailRsDto.MaterialTypeEnum.FertilizerEnum,
            Quantity = x.Quantity,
            Unit = MaterialUnitLabels.Display(x.Lot.Material.MaterialType, x.Lot.Material.StockUnit),
            UnitPrice = x.Lot.UnitPrice,
            CurrencyCode = MaterialApplicationDetailRsDto.CurrencyCodeEnum.TWDEnum
        }).ToList()
    };

    /// <summary>回傳農藥規格、契約庫存單位及合計餘量。</summary>
    /// <param name="stock">農藥主檔及按規格彙總的餘量。</param>
    internal static PesticideMaterialRsDto ToPesticideDto(MaterialStock stock) => new()
    {
        Id = stock.Material.Id,
        Name = stock.Material.Name,
        StockUnit = ContractEnumJsonConverter<PesticideStockUnit>.FromCode(stock.Material.StockUnit),
        Package = ToPackageDto(stock.Material),
        RemainingQuantity = stock.Remaining
    };

    /// <summary>將主檔上的每件包裝資料轉成契約欄位。</summary>
    /// <param name="material">可能沒有包裝資料的資材主檔。</param>
    /// <returns>包裝規格，舊資料缺少規格時回傳空值。</returns>
    private static MaterialPackageDto? ToPackageDto(AgriculturalMaterial material) =>
        material.PackageQuantity is { } quantity ? new()
        {
            Quantity = quantity,
            Unit = ContractEnumJsonConverter<MaterialPackageUnit>.FromCode(material.PackageUnit!)
        } : null;

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
