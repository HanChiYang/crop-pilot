using System.Text.Json;
using CropPilot.Api.Authentication;
using CropPilot.Api.Constants;
using CropPilot.Api.Data;
using CropPilot.Contract.Controllers;
using CropPilot.Contract.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;

namespace CropPilot.Api.Fields;

/// <summary>提供具備 <c>USER</c> 角色的帳號管理自己的田區。</summary>
[Authorize(Roles = UserRole.User)]
public sealed class FieldsController(FieldService fields) : FieldsApiController
{
    /// <summary>取得 Bearer 權杖驗證流程提供的主帳號 ID。</summary>
    private Guid AccountId => HttpContext.Features.GetRequiredFeature<AuthenticatedAccount>().Id;

    /// <summary>新增田區。成功時提供單筆查詢位置，失敗時回傳欄位錯誤。</summary>
    public override async Task<IActionResult> CreateField(CreateFieldRqDto request)
    {
        var result = await fields.CreateAsync(AccountId, request, HttpContext.RequestAborted);
        if (result.Field is not { } field)
        {
            return SaveProblem(result);
        }

        return CreatedAtAction(nameof(GetField), new { fieldId = field.Id },
            new FieldRsDto { Fields = [FieldDtoMapper.ToDto(field)] });
    }

    /// <summary>回傳目前帳號的田區清單。帳號尚無田區時回傳空清單。</summary>
    public override async Task<IActionResult> GetFields()
    {
        var ownedFields = await fields.ListAsync(AccountId, HttpContext.RequestAborted);
        return Ok(new FieldRsDto { Fields = ownedFields.ConvertAll(FieldDtoMapper.ToDto) });
    }

    /// <summary>回傳目前帳號擁有的單筆田區。查無可存取的田區時回傳 HTTP 404。</summary>
    public override async Task<IActionResult> GetField(Guid fieldId)
    {
        var field = await fields.FindAsync(AccountId, fieldId, HttpContext.RequestAborted);
        return field is null ? NotFound() : Ok(FieldDtoMapper.ToDto(field));
    }

    /// <summary>更新帳號擁有的田區，並回傳更新後的資料或對應問題。</summary>
    public override async Task<IActionResult> EditField(Guid fieldId, EditFieldRqDto request)
    {
        var result = await fields.EditAsync(AccountId, fieldId, request, HttpContext.RequestAborted);
        return result.Field is { } field
            ? Ok(new FieldRsDto { Fields = [FieldDtoMapper.ToDto(field)] })
            : SaveProblem(result);
    }

    /// <summary>刪除帳號擁有的一筆田區，成功時回傳 HTTP 204。</summary>
    public override async Task<IActionResult> DeleteField(Guid fieldId) =>
        DeleteResponse(await fields.DeleteAsync(AccountId, fieldId, HttpContext.RequestAborted));

    /// <summary>刪除帳號擁有的全部指定田區；任一筆無法刪除時保留整批。</summary>
    public override async Task<IActionResult> DeleteFields(DeleteFieldRqDto request) =>
        DeleteResponse(await fields.DeleteAsync(AccountId, request, HttpContext.RequestAborted));

    /// <summary>將儲存失敗結果轉換成找不到田區或欄位問題回應。</summary>
    private IActionResult SaveProblem(FieldSaveResult result)
    {
        if (result.Status == FieldWriteStatus.NotFound)
        {
            return NotFound();
        }

        foreach (var (property, message) in result.Errors)
        {
            ModelState.AddModelError(JsonNamingPolicy.CamelCase.ConvertName(property), message);
        }

        return ValidationProblem(modelStateDictionary: ModelState, statusCode: result.Status is FieldWriteStatus.NameConflict or FieldWriteStatus.AreaConflict
            ? StatusCodes.Status409Conflict
            : StatusCodes.Status400BadRequest);
    }

    /// <summary>依刪除結果回傳無內容或契約定義的問題。</summary>
    private IActionResult DeleteResponse(FieldWriteStatus status)
    {
        if (status == FieldWriteStatus.InvalidInput)
        {
            ModelState.AddModelError("fieldIds", ErrorMessages.FieldDeleteIdsInvalid);
            return ValidationProblem(ModelState);
        }

        return status switch
        {
            FieldWriteStatus.Success => NoContent(),
            FieldWriteStatus.NotFound => NotFound(),
            FieldWriteStatus.RelatedPlantings => Problem(statusCode: StatusCodes.Status409Conflict,
                detail: ErrorMessages.FieldHasPlantings),
            _ => throw new InvalidOperationException(ErrorMessages.UnexpectedFieldDeleteResult)
        };
    }
}
