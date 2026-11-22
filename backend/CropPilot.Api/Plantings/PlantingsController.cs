using System.Globalization;
using CropPilot.Api.Authentication;
using CropPilot.Api.Constants;
using CropPilot.Api.Data;
using CropPilot.Api.Fertilizers;
using CropPilot.Api.Harvests;
using CropPilot.Api.Irrigations;
using CropPilot.Contract.Controllers;
using CropPilot.Contract.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;

namespace CropPilot.Api.Plantings;

/// <summary>讓具備 USER 角色的帳號管理自己田區的栽培紀錄。</summary>
[Authorize(Roles = UserRole.User)]
public sealed class PlantingsController(PlantingService plantings, FertilizerService fertilizers,
    IrrigationService irrigations, HarvestService harvests) : PlantingsApiController
{
    private Guid AccountId => HttpContext.Features.GetRequiredFeature<AuthenticatedAccount>().Id;

    /// <summary>新增一筆栽培紀錄，成功時提供單筆查詢位置。</summary>
    public override async Task<IActionResult> CreatePlanting(Guid fieldId, CreatePlantingRqDto request)
    {
        var result = await plantings.CreateAsync(AccountId, fieldId, request, HttpContext.RequestAborted);
        if (result.Planting is { } planting)
        {
            return CreatedAtAction(nameof(GetPlanting), new { plantingId = planting.Id },
                PlantingDtoMapper.ToDto(planting));
        }

        return WriteProblem(result);
    }

    /// <summary>更新栽培紀錄並回傳新值，或依驗證結果回傳問題。</summary>
    public override async Task<IActionResult> EditPlanting(Guid plantingId, EditPlantingRqDto request)
    {
        var result = await plantings.EditAsync(AccountId, plantingId, request, HttpContext.RequestAborted);
        return result.Planting is { } planting ? Ok(PlantingDtoMapper.ToDto(planting)) : WriteProblem(result);
    }

    /// <summary>查詢已結束栽培目前的結束日及可填上下限，查無紀錄回傳 404，未結束回傳 409。</summary>
    public override async Task<IActionResult> GetPlantingCompletionDateRange(Guid plantingId)
    {
        var result = await plantings.GetCompletionDateRangeAsync(AccountId, plantingId, HttpContext.RequestAborted);
        return result.Range is { } range
            ? Ok(PlantingDtoMapper.ToCompletionDateRangeDto(range))
            : WriteProblem(result.Status, result.Errors);
    }

    /// <summary>修正已結束栽培的結束日並回傳更新後的栽培紀錄。</summary>
    public override async Task<IActionResult> EditPlantingCompletionDate(Guid plantingId,
        EditPlantingCompletionDateRqDto request)
    {
        var result = await plantings.EditCompletionDateAsync(AccountId, plantingId, request, HttpContext.RequestAborted);
        return result.Planting is { } planting
            ? Ok(PlantingDtoMapper.ToDto(planting))
            : WriteProblem(result.Status, result.Errors);
    }

    /// <summary>顯示栽培刪除後將移除的田間作業與回復的庫存用量。</summary>
    public override async Task<IActionResult> GetPlantingDeletePreview(Guid plantingId)
    {
        var (status, deletion) = await plantings.GetDeletePreviewAsync(AccountId, plantingId, HttpContext.RequestAborted);
        if (deletion is not null)
            return Ok(PlantingDtoMapper.ToDeletePreviewDto(deletion));
        return PlantingDeleteProblem(status);
    }

    /// <summary>核對使用者確認過的預覽版本，刪除栽培及其作業。</summary>
    public override async Task<IActionResult> DeletePlanting(string previewVersion, Guid plantingId)
    {
        var status = await plantings.DeleteAsync(AccountId, plantingId, previewVersion, HttpContext.RequestAborted);
        return status == PlantingWriteStatus.Success ? NoContent() : PlantingDeleteProblem(status);
    }

    /// <summary>回傳栽培刪除預覽與確認共用的問題回應。</summary>
    private IActionResult PlantingDeleteProblem(PlantingWriteStatus status) => status switch
    {
        PlantingWriteStatus.NotFound => NotFound(),
        PlantingWriteStatus.PreviewChanged => Problem(statusCode: StatusCodes.Status409Conflict,
            detail: ErrorMessages.PlantingDeletePreviewChanged),
        PlantingWriteStatus.HasHarvests => Problem(statusCode: StatusCodes.Status409Conflict,
            detail: ErrorMessages.PlantingDeleteHasHarvests),
        _ => Problem(statusCode: StatusCodes.Status409Conflict, detail: ErrorMessages.PlantingDeleteInvalid)
    };

    /// <summary>將寫入失敗轉換成找不到紀錄或指定欄位的錯誤回應。</summary>
    private IActionResult WriteProblem(PlantingWriteResult result) => WriteProblem(result.Status, result.Errors);

    /// <summary>依結果狀態與欄位錯誤清單回傳 404、409 或 400 問題回應。</summary>
    private IActionResult WriteProblem(PlantingWriteStatus status, Dictionary<string, string>? errors)
    {
        if (status == PlantingWriteStatus.NotFound)
        {
            return NotFound();
        }

        foreach (var (property, message) in errors!)
        {
            ModelState.AddModelError(property, message);
        }
        return ValidationProblem(modelStateDictionary: ModelState, statusCode: status == PlantingWriteStatus.Conflict
            ? StatusCodes.Status409Conflict
            : StatusCodes.Status400BadRequest);
    }

    /// <summary>依栽培 ID 回傳登入帳號的單筆栽培紀錄，查無可存取的紀錄時回傳 HTTP 404。</summary>
    public override async Task<IActionResult> GetPlanting(Guid plantingId)
    {
        var planting = await plantings.FindAsync(AccountId, plantingId, HttpContext.RequestAborted);
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

    /// <summary>回傳指定栽培的資材施用清單。</summary>
    public override async Task<IActionResult> ListMaterialApplications(Guid plantingId)
    {
        var operations = await fertilizers.ListOperationsAsync(AccountId, plantingId, HttpContext.RequestAborted);
        return operations is null ? NotFound() : Ok(operations.ConvertAll(FertilizerDtoMapper.ToDto));
    }

    /// <summary>新增資材施用作業及其批次扣用明細。</summary>
    public override async Task<IActionResult> CreateMaterialApplication(Guid plantingId, CreateMaterialApplicationRqDto request) =>
        this.ToResponse(await fertilizers.CreateOperationAsync(AccountId, plantingId, request, HttpContext.RequestAborted),
            FertilizerDtoMapper.ToDto);

    /// <summary>更新資材施用作業及其完整明細。</summary>
    public override async Task<IActionResult> EditMaterialApplication(Guid materialApplicationId,
        EditMaterialApplicationRqDto request) =>
        this.ToResponse(await fertilizers.EditOperationAsync(AccountId, materialApplicationId, request,
            HttpContext.RequestAborted), FertilizerDtoMapper.ToDto);

    /// <summary>刪除指定施用作業並退回庫存用量。</summary>
    public override async Task<IActionResult> DeleteMaterialApplication(Guid materialApplicationId) =>
        this.ToResponse(await fertilizers.DeleteOperationAsync(AccountId, materialApplicationId,
            HttpContext.RequestAborted), value => value.Id);

    /// <summary>依日期區間與排序查詢採收及整批累計重量。</summary>
    public override async Task<IActionResult> GetHarvets(Guid plantingId, DateOnly? startDate, DateOnly? endDate,
        string? sortBy, string? sortDirection)
    {
        var dateErrors = new Dictionary<string, string>();
        if (!MatchesHarvestFilterDate("startDate", startDate))
            dateErrors["startDate"] = ErrorMessages.HarvestFilterDateInvalid;
        if (!MatchesHarvestFilterDate("endDate", endDate))
            dateErrors["endDate"] = ErrorMessages.HarvestFilterDateInvalid;
        if (dateErrors.Count > 0)
            return HarvestValidationProblem(dateErrors);

        var (list, errors) = await harvests.ListAsync(AccountId, plantingId, startDate, endDate,
            Request.Query.TryGetValue("sortBy", out var sortField) ? sortField.ToString() : sortBy,
            Request.Query.TryGetValue("sortDirection", out var direction) ? direction.ToString() : sortDirection,
            HttpContext.RequestAborted);
        if (errors is not null)
            return HarvestValidationProblem(errors);
        return list is null ? NotFound() : Ok(HarvestDtoMapper.ToDto(list));
    }

    /// <summary>日期查詢可省略。提供時須使用單一 ISO 日期。</summary>
    private bool MatchesHarvestFilterDate(string name, DateOnly? date) =>
        !Request.Query.TryGetValue(name, out var value) ||
        date is { } parsed && string.Equals(value.ToString(),
            parsed.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), StringComparison.Ordinal);

    /// <summary>新增採收並在成功時回傳該筆紀錄。</summary>
    public override async Task<IActionResult> CreateHarvest(Guid plantingId, SaveHarvestRqDto request) =>
        HarvestResponse(await harvests.CreateAsync(AccountId, plantingId, request, HttpContext.RequestAborted),
            StatusCodes.Status201Created);

    /// <summary>編輯採收並在成功時回傳更新後的紀錄。</summary>
    public override async Task<IActionResult> EditHarvest(Guid harvestId, SaveHarvestRqDto request) =>
        HarvestResponse(await harvests.EditAsync(AccountId, harvestId, request, HttpContext.RequestAborted),
            StatusCodes.Status200OK);

    /// <summary>刪除帳號所屬的一筆採收，成功時回傳 204。</summary>
    public override async Task<IActionResult> DeleteHarvest(Guid harvestId) =>
        await harvests.DeleteAsync(AccountId, harvestId, HttpContext.RequestAborted) ? NoContent() : NotFound();

    /// <summary>將採收寫入結果轉為契約規定的 HTTP 回應。</summary>
    private IActionResult HarvestResponse(HarvestWriteResult result, int successStatus) => result.Status switch
    {
        HarvestWriteStatus.Success => StatusCode(successStatus, HarvestDtoMapper.ToDto(result.Harvest!)),
        HarvestWriteStatus.NotFound => NotFound(),
        HarvestWriteStatus.Conflict => Problem(statusCode: StatusCodes.Status409Conflict,
            detail: ErrorMessages.HarvestPlantingPlanned),
        _ => HarvestValidationProblem(result.Errors!)
    };

    /// <summary>使用原始 JSON 欄位名回傳採收驗證錯誤。</summary>
    private IActionResult HarvestValidationProblem(Dictionary<string, string> errors)
    {
        foreach (var (field, message) in errors)
            ModelState.AddModelError(field, message);
        return ValidationProblem(ModelState);
    }

    /// <summary>回傳指定栽培的灌溉清單，無法存取栽培時回傳 404。</summary>
    public override async Task<IActionResult> ListIrrigations(Guid plantingId)
    {
        var operations = await irrigations.ListAsync(AccountId, plantingId, HttpContext.RequestAborted);
        return operations is null ? NotFound() : Ok(operations.ConvertAll(IrrigationDtoMapper.ToDto));
    }

    /// <summary>新增灌溉作業，成功時回傳 201 及完整紀錄。</summary>
    public override async Task<IActionResult> CreateIrrigation(Guid plantingId, SaveIrrigationRqDto request) =>
        IrrigationResponse(await irrigations.CreateAsync(AccountId, plantingId, request, HttpContext.RequestAborted),
            StatusCodes.Status201Created);

    /// <summary>更新單筆灌溉作業並回傳完整紀錄。</summary>
    public override async Task<IActionResult> EditIrrigation(Guid irrigationId, SaveIrrigationRqDto request) =>
        IrrigationResponse(await irrigations.EditAsync(AccountId, irrigationId, request, HttpContext.RequestAborted),
            StatusCodes.Status200OK);

    /// <summary>刪除單筆灌溉作業，成功時回傳 204。</summary>
    public override async Task<IActionResult> DeleteIrrigation(Guid irrigationId) =>
        await irrigations.DeleteAsync(AccountId, irrigationId, HttpContext.RequestAborted) ? NoContent() : NotFound();

    /// <summary>將灌溉寫入結果轉成成功或契約定義的問題回應。</summary>
    private IActionResult IrrigationResponse(IrrigationWriteResult result, int successStatus)
    {
        switch (result.Status)
        {
            case IrrigationWriteStatus.Success:
                return StatusCode(successStatus, IrrigationDtoMapper.ToDto(result.Operation!));
            case IrrigationWriteStatus.NotFound:
                return NotFound();
            default:
                ModelState.AddModelError(result.Field!, result.Message!);
                return ValidationProblem(ModelState);
        }
    }
}
