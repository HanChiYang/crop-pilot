using System.Text.Json;
using CropPilot.Api.Authentication;
using CropPilot.Api.Data;
using CropPilot.Contract.Controllers;
using CropPilot.Contract.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;

namespace CropPilot.Api.Fields;

/// <summary>提供具備 <c>USER</c> 角色的帳號新增與查詢田區。</summary>
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
            foreach (var (property, message) in result.Errors)
            {
                ModelState.AddModelError(JsonNamingPolicy.CamelCase.ConvertName(property), message);
            }

            return ValidationProblem(modelStateDictionary: ModelState, statusCode: result.NameConflict
                ? StatusCodes.Status409Conflict
                : StatusCodes.Status400BadRequest);
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

    /// <summary>保留生成契約的田區編輯方法，供後續開發。</summary>
    [NonAction]
    public override Task<IActionResult> EditField(Guid fieldId, EditFieldRqDto request) => throw new NotSupportedException();

    /// <summary>保留生成契約的單筆田區刪除方法，供後續開發。</summary>
    [NonAction]
    public override Task<IActionResult> DeleteField(Guid fieldId) => throw new NotSupportedException();

    /// <summary>保留生成契約的批次田區刪除方法，供後續開發。</summary>
    [NonAction]
    public override Task<IActionResult> DeleteFields(DeleteFieldRqDto request) => throw new NotSupportedException();
}
