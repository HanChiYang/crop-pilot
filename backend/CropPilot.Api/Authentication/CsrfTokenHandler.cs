using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;

namespace CropPilot.Api.Authentication;

internal sealed class CsrfTokenRequirement : IAuthorizationRequirement;

internal sealed class CsrfTokenHandler(IAntiforgery antiforgery)
    : AuthorizationHandler<CsrfTokenRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context, CsrfTokenRequirement requirement)
    {
        if (context.Resource is HttpContext request && await antiforgery.IsRequestValidAsync(request))
        {
            context.Succeed(requirement);
        }
    }
}
