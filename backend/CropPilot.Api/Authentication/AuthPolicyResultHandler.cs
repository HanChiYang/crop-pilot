using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Mvc;

namespace CropPilot.Api.Authentication;

internal sealed class AuthPolicyResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler fallback = new();

    public Task HandleAsync(
        RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult result)
    {
        var csrfFailed = result.AuthorizationFailure?.FailedRequirements is { } failures
            && failures.Any()
            && failures.All(requirement => requirement is CsrfTokenRequirement);
        var anonymousCsrfFailed = result.Challenged
            && policy.Requirements.All(requirement => requirement is CsrfTokenRequirement);
        if (!csrfFailed && !anonymousCsrfFailed)
        {
            return fallback.HandleAsync(next, context, policy, result);
        }

        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        return context.Response.WriteAsJsonAsync(new ValidationProblemDetails
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "防偽權杖驗證失敗"
        }, options: null, contentType: "application/problem+json", cancellationToken: context.RequestAborted);
    }
}
