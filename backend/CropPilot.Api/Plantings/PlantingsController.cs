using CropPilot.Api.Authentication;
using CropPilot.Api.Constants;
using CropPilot.Api.Data;
using CropPilot.Api.Fertilizers;
using CropPilot.Contract.Controllers;
using CropPilot.Contract.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;

namespace CropPilot.Api.Plantings;

/// <summary>讓具備 USER 角色的帳號管理自己田區的栽培紀錄。</summary>
[Authorize(Roles = UserRole.User)]
public sealed class PlantingsController(PlantingService plantings, FertilizerService fertilizers) : PlantingsApiController
{
    private Guid AccountId => HttpContext.Features.GetRequiredFeature<AuthenticatedAccount>().Id;

    /// <summary>新增一筆栽培紀錄，成功時提供單筆查詢位置。</summary>
    public override async Task<IActionResult> CreatePlanting(Guid fieldId, CreatePlantingRqDto request)
    {
        var result = await plantings.CreateAsync(AccountId, fieldId, request, HttpContext.RequestAborted);
        if (result.Planting is { } planting)
        {
            return CreatedAtAction(nameof(GetPlanting), new { fieldId, plantingId = planting.Id },
                PlantingDtoMapper.ToDto(planting));
        }

        return WriteProblem(result);
    }

    /// <summary>更新栽培紀錄並回傳新值，或依驗證結果回傳問題。</summary>
    public override async Task<IActionResult> EditPlanting(Guid fieldId, Guid plantingId, EditPlantingRqDto request)
    {
        var result = await plantings.EditAsync(AccountId, fieldId, plantingId, request, HttpContext.RequestAborted);
        return result.Planting is { } planting ? Ok(PlantingDtoMapper.ToDto(planting)) : WriteProblem(result);
    }

    /// <summary>刪除規劃中的紀錄，成功時回傳 HTTP 204。</summary>
    public override async Task<IActionResult> DeletePlanting(Guid fieldId, Guid plantingId)
    {
        var result = await plantings.DeleteAsync(AccountId, fieldId, plantingId, HttpContext.RequestAborted);
        return result.Status switch
        {
            PlantingWriteStatus.Success => NoContent(),
            PlantingWriteStatus.NotFound => NotFound(),
            _ => Problem(statusCode: StatusCodes.Status409Conflict, detail: ErrorMessages.PlantingDeleteInvalid)
        };
    }

    /// <summary>將寫入失敗轉換成找不到紀錄或指定欄位的錯誤回應。</summary>
    private IActionResult WriteProblem(PlantingWriteResult result)
    {
        if (result.Status == PlantingWriteStatus.NotFound)
        {
            return NotFound();
        }

        foreach (var (property, message) in result.Errors!)
        {
            ModelState.AddModelError(property, message);
        }
        return ValidationProblem(modelStateDictionary: ModelState, statusCode: result.Status == PlantingWriteStatus.Conflict
            ? StatusCodes.Status409Conflict
            : StatusCodes.Status400BadRequest);
    }

    /// <summary>回傳指定田區的單筆栽培紀錄，查無可存取的紀錄時回傳 HTTP 404。</summary>
    public override async Task<IActionResult> GetPlanting(Guid fieldId, Guid plantingId)
    {
        var planting = await plantings.FindAsync(AccountId, fieldId, plantingId, HttpContext.RequestAborted);
        return planting is null ? NotFound() : Ok(PlantingDtoMapper.ToDto(planting));
    }

    /// <summary>回傳指定田區目前的栽培清單，田區無法存取時回傳 HTTP 404。</summary>
    public override async Task<IActionResult> ListFieldPlantings(Guid fieldId)
    {
        var field = await plantings.ListFieldAsync(AccountId, fieldId, HttpContext.RequestAborted);
        return field is null ? NotFound() : Ok(PlantingDtoMapper.ToFieldPlantingsDto(field));
    }

    /// <summary>依選用的田區及作物名稱回傳登入帳號的已結束紀錄。</summary>
    public override async Task<IActionResult> ListPlantingHistory(Guid? fieldId, string? cropName)
    {
        ValidateHistoryFieldId(fieldId);
        if (Request.Query.ContainsKey("cropName") && !PlantingService.IsHistoryCropNameValid(cropName))
        {
            ModelState.AddModelError("cropName", ErrorMessages.PlantingCropNameInvalid);
        }

        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var history = await plantings.ListHistoryAsync(AccountId, fieldId, cropName, HttpContext.RequestAborted);
        return history is null ? NotFound() : Ok(history.ConvertAll(PlantingDtoMapper.ToHistoryDto));
    }

    /// <summary>回傳登入帳號在選用田區的已結束作物選項。</summary>
    public override async Task<IActionResult> ListPlantingHistoryCrops(Guid? fieldId)
    {
        ValidateHistoryFieldId(fieldId);
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var crops = await plantings.ListHistoryCropsAsync(AccountId, fieldId, HttpContext.RequestAborted);
        return crops is null ? NotFound() : Ok(crops);
    }

    /// <summary>辨識模型繫結會轉為 null 的空田區參數，並回報欄位錯誤。</summary>
    private void ValidateHistoryFieldId(Guid? fieldId)
    {
        if (fieldId is null && Request.Query.ContainsKey("fieldId"))
        {
            ModelState.AddModelError("fieldId", ErrorMessages.PlantingHistoryFieldIdInvalid);
        }
    }

    /// <summary>回傳指定栽培的施肥清單。</summary>
    public override async Task<IActionResult> ListFertilizations(Guid fieldId, Guid plantingId)
    {
        var operations = await fertilizers.ListOperationsAsync(AccountId, fieldId, plantingId, HttpContext.RequestAborted);
        return operations is null ? NotFound() : Ok(operations.ConvertAll(FertilizerDtoMapper.ToDto));
    }

    /// <summary>新增施肥作業及其批次扣用明細。</summary>
    public override async Task<IActionResult> CreateFertilization(Guid fieldId, Guid plantingId, CreateFertilizationRqDto request) =>
        this.ToResponse(await fertilizers.CreateOperationAsync(AccountId, fieldId, plantingId, request, HttpContext.RequestAborted),
            FertilizerDtoMapper.ToDto);

    /// <summary>更新既有施肥作業及其完整明細。</summary>
    public override async Task<IActionResult> EditFertilization(Guid fieldId, Guid plantingId, Guid fertilizationId,
        EditFertilizationRqDto request) =>
        this.ToResponse(await fertilizers.EditOperationAsync(AccountId, fieldId, plantingId, fertilizationId, request,
            HttpContext.RequestAborted), FertilizerDtoMapper.ToDto);
}
