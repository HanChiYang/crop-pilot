using System.Security.Claims;
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
    AuthSessions sessions,
    IUserClaimsPrincipalFactory<MainAccount> principals,
    AccessTokens tokens,
    IAuthenticationSchemeProvider schemes,
    ILogger<AuthController> logger) : AuthApiController
{
    [AllowAnonymous]
    public override async Task<IActionResult> GetCsrfToken()
    {
        var renewal = await HttpContext.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        if (renewal.Principal is { } principal)
        {
            HttpContext.User = principal;
        }

        var tokens = antiforgery.GetAndStoreTokens(HttpContext);
        Response.Cookies.Append("XSRF-TOKEN", tokens.RequestToken!, new CookieOptions
        {
            HttpOnly = false,
            Secure = antiforgeryOptions.Value.Cookie.SecurePolicy == CookieSecurePolicy.Always || Request.IsHttps,
            SameSite = SameSiteMode.Lax,
            Path = "/"
        });
        return NoContent();
    }

    public override async Task<IActionResult> Login(LoginRqDto loginRqDto)
    {
        Response.Headers.CacheControl = "no-store";
        var username = loginRqDto.Username.Trim();
        if (username.Length == 0)
        {
            ModelState.AddModelError("username", "請輸入帳號。");
            return ValidationProblem(ModelState);
        }

        var user = await users.FindByNameAsync(username);
        var result = user is { Status: MainAccount.Active }
            ? await signInManager.CheckPasswordSignInAsync(user, loginRqDto.Password, lockoutOnFailure: true)
            : Microsoft.AspNetCore.Identity.SignInResult.Failed;

        if (!result.Succeeded || user is null)
        {
            return Problem(statusCode: StatusCodes.Status401Unauthorized, title: "帳號或密碼不正確");
        }

        var session = await sessions.StartAsync(user, AuthSessions.GetId(User), HttpContext);
        return session is null ? SessionChanged() : await TokenResponseAsync(user, session);
    }

    public override Task<IActionResult> GetSession()
    {
        Response.Headers.CacheControl = "no-store";
        return Task.FromResult<IActionResult>(NoContent());
    }

    public override async Task<IActionResult> Logout(LogoutRqDto logoutRqDto)
    {
        Response.Headers.CacheControl = "no-store";
        if (logoutRqDto.SessionId == Guid.Empty)
        {
            ModelState.AddModelError("sessionId", "請提供工作階段識別碼。");
            return ValidationProblem(ModelState);
        }

        var id = AuthSessions.GetId(User);
        if (id != logoutRqDto.SessionId || await sessions.RevokeAsync(id, HttpContext.RequestAborted) == 0)
        {
            return SessionChanged();
        }

        await signInManager.SignOutAsync();
        return NoContent();
    }

    public override async Task<IActionResult> RefreshAccessToken()
    {
        var session = HttpContext.Features.Get<AuthSession>();
        return session is null ? Unauthorized() : await TokenResponseAsync(session.MainAccount, session);
    }

    private ObjectResult SessionChanged() =>
        Problem(statusCode: StatusCodes.Status409Conflict, title: "登入工作階段已變更");

    private async Task<IActionResult> TokenResponseAsync(MainAccount account, AuthSession session)
    {
        Response.Headers.CacheControl = "no-store";
        var principal = await principals.CreateAsync(account);
        var identity = (ClaimsIdentity)principal.Identity!;
        var roles = identity.FindAll(identity.RoleClaimType).Select(claim => claim.Value);
        var token = tokens.Issue(account.Id, roles, session.ExpiresAt);
        return token is null ? Unauthorized() : Ok(new AccessTokenRsDto { AccessToken = token, SessionId = session.Id });
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
            var renewal = await HttpContext.AuthenticateAsync(IdentityConstants.ApplicationScheme);
            var properties = new AuthenticationProperties { RedirectUri = "/dashboard" };
            properties.Items[AuthSessions.SessionIdClaim] = AuthSessions.GetId(renewal.Principal).ToString("D");
            await HttpContext.ChallengeAsync(GoogleSignIn.Scheme, properties);
            return new EmptyResult();
        }
        catch (InvalidOperationException exception) when (!Response.HasStarted)
        {
            logger.LogError(exception, "Unable to start Google sign-in.");
            return LocalRedirect(GoogleSignIn.FailedPath);
        }
    }
}
