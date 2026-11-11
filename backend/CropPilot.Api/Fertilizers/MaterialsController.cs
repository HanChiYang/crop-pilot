using CropPilot.Api.Authentication;
using CropPilot.Api.Data;
using CropPilot.Contract.Controllers;
using CropPilot.Contract.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;

namespace CropPilot.Api.Fertilizers;

/// <summary>提供登入帳號管理肥料資材及庫存批次的端點。</summary>
[Authorize(Roles = UserRole.User)]
public sealed class MaterialsController(FertilizerService service) : MaterialsApiController
{
    private Guid AccountId => HttpContext.Features.GetRequiredFeature<AuthenticatedAccount>().Id;
    private CancellationToken Aborted => HttpContext.RequestAborted;

    /// <summary>列出目前帳號的肥料及合計餘量。</summary>
    public override async Task<IActionResult> ListFertilizerMaterials() =>
        Ok((await service.ListMaterialsAsync(AccountId, Aborted)).ConvertAll(FertilizerDtoMapper.ToDto));

    /// <summary>建立肥料資材並回傳主檔資料。</summary>
    public override async Task<IActionResult> CreateFertilizerMaterial(CreateFertilizerMaterialRqDto request) =>
        this.ToResponse(await service.SaveMaterialAsync(AccountId, null, request, Aborted), FertilizerDtoMapper.ToDto);

    /// <summary>更新指定肥料的主檔資料。</summary>
    public override async Task<IActionResult> EditFertilizerMaterial(Guid materialId, CreateFertilizerMaterialRqDto request) =>
        this.ToResponse(await service.SaveMaterialAsync(AccountId, materialId, request, Aborted), FertilizerDtoMapper.ToDto);

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
    public override async Task<IActionResult> EditFertilizerLot(Guid materialId, Guid lotId, EditFertilizerLotRqDto request) =>
        this.ToResponse(await service.EditLotAsync(AccountId, materialId, lotId, request, Aborted), FertilizerDtoMapper.ToDto);

    /// <summary>刪除沒有施肥明細引用的庫存批次。</summary>
    public override async Task<IActionResult> DeleteFertilizerLot(Guid materialId, Guid lotId) =>
        this.ToResponse(await service.DeleteLotAsync(AccountId, materialId, lotId, Aborted), FertilizerDtoMapper.ToDto);
}
