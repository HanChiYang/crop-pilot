using CropPilot.Api.Authentication;
using CropPilot.Api.Data;
using CropPilot.Contract.Controllers;
using CropPilot.Contract.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;

namespace CropPilot.Api.Plantings;

/// <summary>讓具備 USER 角色的帳號新增及查詢自己田區的栽培紀錄。</summary>
[Authorize(Roles = UserRole.User)]
public sealed class PlantingsController(PlantingService plantings) : PlantingsApiController
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

        if (result.Errors.Count == 0)
        {
            return NotFound();
        }

        foreach (var (property, message) in result.Errors)
        {
            ModelState.AddModelError(property, message);
        }
        return ValidationProblem(ModelState);
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
        return field is null ? NotFound() : Ok(field.Plantings.ConvertAll(PlantingDtoMapper.ToDto));
    }
}
