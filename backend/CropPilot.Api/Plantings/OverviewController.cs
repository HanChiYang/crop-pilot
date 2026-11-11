using CropPilot.Api.Authentication;
using CropPilot.Api.Data;
using CropPilot.Contract.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;

namespace CropPilot.Api.Plantings;

/// <summary>讓具備 USER 角色的帳號查看自己的田區總覽。</summary>
[Authorize(Roles = UserRole.User)]
public sealed class OverviewController(PlantingService plantings) : OverviewApiController
{
    /// <summary>回傳目前帳號的所有田區及各田區目前的栽培紀錄。</summary>
    public override async Task<IActionResult> GetOverview()
    {
        var accountId = HttpContext.Features.GetRequiredFeature<AuthenticatedAccount>().Id;
        var overview = await plantings.ListOverviewAsync(accountId, HttpContext.RequestAborted);
        return Ok(overview.ConvertAll(PlantingDtoMapper.ToOverviewDto));
    }
}
