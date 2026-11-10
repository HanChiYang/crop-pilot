using CropPilot.Contract.Controllers;
using CropPilot.Contract.Models;
using CropPilot.Api.Data;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace CropPilot.Api.Authentication;

public sealed class AuthController(
    IAntiforgery antiforgery,
    IOptions<AntiforgeryOptions> antiforgeryOptions,
    SignInManager<MainAccount> signInManager,
    UserManager<MainAccount> users,
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
            Secure = antiforgeryOptions.Value.Cookie.SecurePolicy == CookieSecurePolicy.Always || Request.IsHttps,
            SameSite = SameSiteMode.Lax,
            Path = "/"
        });
        return Task.FromResult<IActionResult>(NoContent());
    }

    public override async Task<IActionResult> Login(LoginRequest loginRequest)
    {
        var username = loginRequest.Username.Trim();
        if (username.Length == 0)
        {
            ModelState.AddModelError("username", "請輸入帳號。");
            return ValidationProblem(ModelState);
        }

        var user = await users.FindByNameAsync(username);
        var result = user is { Status: MainAccount.Active }
            ? await signInManager.PasswordSignInAsync(
                user, loginRequest.Password, isPersistent: false, lockoutOnFailure: true)
            : Microsoft.AspNetCore.Identity.SignInResult.Failed;

        return result.Succeeded
            ? NoContent()
            : Problem(statusCode: StatusCodes.Status401Unauthorized, title: "帳號或密碼不正確");
    }

    public override Task<IActionResult> GetSession()
    {
        Response.Headers.CacheControl = "no-store";
        return Task.FromResult<IActionResult>(NoContent());
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
