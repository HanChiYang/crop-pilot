using CropPilot.Contract.Controllers;
using CropPilot.Contract.Models;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace CropPilot.Api.Authentication;

public sealed class AuthController(
    IAntiforgery antiforgery,
    SignInManager<IdentityUser<Guid>> signInManager,
    IAuthenticationSchemeProvider schemes,
    ILogger<AuthController> logger) : AuthApiController
{
    [AllowAnonymous]
    public override Task<IActionResult> GetCsrfToken()
    {
        var tokens = antiforgery.GetAndStoreTokens(HttpContext);
        Response.Cookies.Append("XSRF-TOKEN", tokens.RequestToken!, new CookieOptions
        {
            HttpOnly = false,
            Secure = true,
            SameSite = SameSiteMode.Lax,
            Path = "/"
        });
        return Task.FromResult<IActionResult>(NoContent());
    }

    public override async Task<IActionResult> Login(LoginRequest loginRequest)
    {
        var result = await signInManager.PasswordSignInAsync(
            loginRequest.Username, loginRequest.Password, isPersistent: false, lockoutOnFailure: true);

        return result.Succeeded
            ? NoContent()
            : Problem(statusCode: StatusCodes.Status401Unauthorized, title: "帳號或密碼不正確");
    }

    public override async Task<IActionResult> Logout()
    {
        await signInManager.SignOutAsync();
        return NoContent();
    }

    [AllowAnonymous]
    public override async Task<IActionResult> StartGoogleSignIn()
    {
        Response.Headers.CacheControl = "no-store";
        if (await schemes.GetSchemeAsync(GoogleSignIn.Scheme) is null)
        {
            return LocalRedirect(GoogleSignIn.FailedPath);
        }

        try
        {
            await HttpContext.ChallengeAsync(
                GoogleSignIn.Scheme, new AuthenticationProperties { RedirectUri = "/dashboard" });
            return new EmptyResult();
        }
        catch (InvalidOperationException exception) when (!Response.HasStarted)
        {
            logger.LogError(exception, "Unable to start Google sign-in.");
            return LocalRedirect(GoogleSignIn.FailedPath);
        }
    }
}
