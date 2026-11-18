using CropPilot.Api.Authentication;
using CropPilot.Api.Constants;
using CropPilot.Api.Data;
using CropPilot.Contract.Controllers;
using CropPilot.Contract.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;

namespace CropPilot.Api.Fertilizers;

/// <summary>提供登入帳號管理農藥與肥料資材及庫存批次的端點。</summary>
[Authorize(Roles = UserRole.User)]
public sealed class MaterialsController(FertilizerService service) : MaterialsApiController
{
    private Guid AccountId => HttpContext.Features.GetRequiredFeature<AuthenticatedAccount>().Id;
    private CancellationToken Aborted => HttpContext.RequestAborted;

    /// <summary>列出目前帳號的肥料及合計餘量。</summary>
    public override async Task<IActionResult> ListFertilizerMaterials() =>
        Ok((await service.ListMaterialsAsync(AccountId, Aborted)).ConvertAll(FertilizerDtoMapper.ToDto));

    /// <summary>建立肥料資材並回傳包裝規格與餘量。</summary>
    /// <param name="request">資材名稱、庫存單位及包裝規格。</param>
    public override async Task<IActionResult> CreateFertilizerMaterial(CreateFertilizerMaterialRqDto request) =>
        this.ToResponse(await service.SaveMaterialAsync(AccountId, null, request.Name, request.StockUnit, request.Package, Aborted),
            FertilizerDtoMapper.ToDto);

    /// <summary>更新指定肥料規格的資料。</summary>
    /// <param name="materialId">肥料規格識別碼。</param>
    /// <param name="request">更正的名稱、庫存單位及包裝規格。</param>
    public override async Task<IActionResult> EditFertilizerMaterial(Guid materialId, CreateFertilizerMaterialRqDto request) =>
        this.ToResponse(await service.SaveMaterialAsync(AccountId, materialId, request.Name, request.StockUnit, request.Package, Aborted),
            FertilizerDtoMapper.ToDto);

    /// <summary>刪除無批次的肥料資材。</summary>
    public override async Task<IActionResult> DeleteFertilizerMaterial(Guid materialId) =>
        this.ToResponse(await service.DeleteMaterialAsync(AccountId, materialId, Aborted), value => value.Id);

    /// <summary>列出指定肥料的所有批次及各批次餘量。</summary>
    public override async Task<IActionResult> ListFertilizerLots(Guid materialId)
    {
        var lots = await service.ListLotsAsync(AccountId, materialId, Aborted);
        return lots is null ? NotFound() : Ok(lots.ConvertAll(FertilizerDtoMapper.ToDto));
    }

    /// <summary>新增進貨或期初庫存批次。</summary>
    public override async Task<IActionResult> CreateFertilizerLot(Guid materialId, CreateFertilizerLotRqDto request) =>
        this.ToResponse(await service.CreateLotAsync(AccountId, materialId, request, Aborted), FertilizerDtoMapper.ToDto);

    /// <summary>修正指定批次的日期、數量及價格。</summary>
    public override async Task<IActionResult> EditFertilizerLot(Guid lotId, EditFertilizerLotRqDto request) =>
        this.ToResponse(await service.EditLotAsync(AccountId, lotId, request, Aborted), FertilizerDtoMapper.ToDto);

    /// <summary>刪除未被資材施用明細引用的肥料批次。</summary>
    /// <param name="lotId">要刪除的肥料批次識別碼。</param>
    public override async Task<IActionResult> DeleteFertilizerLot(Guid lotId) =>
        this.ToResponse(await service.DeleteLotAsync(AccountId, lotId, Aborted), FertilizerDtoMapper.ToDto);

    /// <summary>列出登入帳號的農藥主檔及餘量。</summary>
    public override async Task<IActionResult> ListPesticideMaterials() =>
        Ok((await service.ListMaterialsAsync(AccountId, Aborted, AgriculturalMaterial.Pesticide))
            .ConvertAll(FertilizerDtoMapper.ToPesticideDto));

    /// <summary>建立農藥規格，將庫存單位對應至資料庫使用的大寫代碼。</summary>
    /// <param name="request">農藥名稱、庫存單位及包裝規格。</param>
    public override async Task<IActionResult> CreatePesticideMaterial(CreatePesticideMaterialRqDto request)
    {
        var unit = ContractEnumJsonConverter<PesticideStockUnit>.ToCode(request.StockUnit);
        if (unit is null)
        {
            ModelState.AddModelError("stockUnit", ErrorMessages.PesticideUnitInvalid);
            return ValidationProblem();
        }
        return this.ToResponse(await service.SaveMaterialAsync(AccountId, null, request.Name, unit, request.Package, Aborted,
            AgriculturalMaterial.Pesticide), FertilizerDtoMapper.ToPesticideDto);
    }

    /// <summary>刪除沒有庫存批次的農藥規格。</summary>
    /// <param name="materialId">要刪除的農藥規格識別碼。</param>
    public override async Task<IActionResult> DeletePesticideMaterial(Guid materialId) =>
        this.ToResponse(await service.DeleteMaterialAsync(AccountId, materialId, Aborted, AgriculturalMaterial.Pesticide),
            value => value.Id);

    /// <summary>列出指定農藥的進貨及期初庫存批次。</summary>
    public override async Task<IActionResult> ListPesticideLots(Guid materialId)
    {
        var lots = await service.ListLotsAsync(AccountId, materialId, Aborted, AgriculturalMaterial.Pesticide);
        return lots is null ? NotFound() : Ok(lots.ConvertAll(FertilizerDtoMapper.ToPesticideDto));
    }

    /// <summary>為指定農藥建立進貨或期初庫存批次。</summary>
    public override async Task<IActionResult> CreatePesticideLot(Guid materialId, CreatePesticideLotRqDto request) =>
        this.ToResponse(await service.CreatePesticideLotAsync(AccountId, materialId, request, Aborted),
            FertilizerDtoMapper.ToPesticideDto);

    /// <summary>修正指定農藥批次的日期、登記量與單價。</summary>
    /// <param name="lotId">要更正的農藥批次識別碼。</param>
    /// <param name="request">更正後的登記量、日期與單價。</param>
    public override async Task<IActionResult> EditPesticideLot(Guid lotId, EditFertilizerLotRqDto request) =>
        this.ToResponse(await service.EditLotAsync(AccountId, lotId, request, Aborted, AgriculturalMaterial.Pesticide),
            FertilizerDtoMapper.ToPesticideDto);

    /// <summary>刪除未被資材施用明細引用的農藥批次。</summary>
    /// <param name="lotId">要刪除的農藥批次識別碼。</param>
    public override async Task<IActionResult> DeletePesticideLot(Guid lotId) =>
        this.ToResponse(await service.DeleteLotAsync(AccountId, lotId, Aborted, AgriculturalMaterial.Pesticide),
            FertilizerDtoMapper.ToPesticideDto);
}
