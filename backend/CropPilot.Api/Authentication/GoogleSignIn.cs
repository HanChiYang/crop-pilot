using System.Security.Claims;
using CropPilot.Api.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Npgsql;

namespace CropPilot.Api.Authentication;

internal sealed class GoogleSignIn(
    UserManager<IdentityUser<Guid>> users,
    SignInManager<IdentityUser<Guid>> signInManager,
    CropPilotDbContext database,
    ILogger<GoogleSignIn> logger)
{
    internal const string Scheme = "Google";
    internal const string FailedPath = "/login?error=google_sign_in_failed";

    internal static void Configure(AuthenticationBuilder authentication, IConfiguration configuration)
    {
        var clientId = configuration["Authentication:Google:ClientId"];
        var clientSecret = configuration["Authentication:Google:ClientSecret"];
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
        {
            return;
        }

        authentication.Services.AddScoped<GoogleSignIn>();
        authentication.AddOpenIdConnect(Scheme, options =>
        {
            options.Authority = "https://accounts.google.com";
            options.ClientId = clientId;
            options.ClientSecret = clientSecret;
            options.CallbackPath = "/signin-google";
            options.SignInScheme = IdentityConstants.ExternalScheme;
            options.ResponseType = OpenIdConnectResponseType.Code;
            options.ResponseMode = OpenIdConnectResponseMode.Query;
            options.UsePkce = true;
            options.MapInboundClaims = false;
            options.SaveTokens = false;
            options.Scope.Clear();
            options.Scope.Add("openid");
            options.Scope.Add("email");

            options.Events.OnTicketReceived = async context =>
            {
                context.HandleResponse();
                var signIn = context.HttpContext.RequestServices.GetRequiredService<GoogleSignIn>();
                await signIn.CompleteAsync(context.HttpContext, context.Principal);
            };
            options.Events.OnAccessDenied = context =>
            {
                context.HandleResponse();
                context.Response.Redirect("/login?error=google_cancelled");
                return Task.CompletedTask;
            };
            options.Events.OnRemoteFailure = context =>
            {
                context.HandleResponse();
                context.Response.Redirect(FailedPath);
                return Task.CompletedTask;
            };
        });
    }

    private async Task CompleteAsync(HttpContext context, ClaimsPrincipal? principal)
    {
        var destination = FailedPath;
        try
        {
            if (await TrySignInAsync(principal, context.RequestAborted))
            {
                destination = "/dashboard";
            }
        }
        catch (Exception exception) when (!context.RequestAborted.IsCancellationRequested)
        {
            logger.LogError(exception, "Unable to complete Google sign-in.");
        }

        context.Response.Redirect(destination);
    }

    private async Task<bool> TrySignInAsync(ClaimsPrincipal? principal, CancellationToken cancellationToken)
    {
        var subject = principal?.FindFirstValue("sub");
        if (string.IsNullOrWhiteSpace(subject) || subject.Length > 255)
        {
            return false;
        }

        if (await users.FindByLoginAsync(Scheme, subject) is null)
        {
            var email = principal?.FindFirstValue("email");
            var verified = bool.TryParse(principal?.FindFirstValue("email_verified"), out var isVerified) && isVerified;
            if (string.IsNullOrWhiteSpace(email) || email.Length > 256 || !verified)
            {
                return false;
            }

            if (await users.FindByEmailAsync(email) is null && await users.FindByNameAsync(email) is null)
            {
                try
                {
                    await CreateAccountAsync(subject, email, cancellationToken);
                }
                catch (DbUpdateException exception) when (exception.InnerException is PostgresException
                    { SqlState: PostgresErrorCodes.UniqueViolation })
                {
                    // A simultaneous callback can win the unique email or external login constraint.
                }

                database.ChangeTracker.Clear();
            }
        }

        var result = await signInManager.ExternalLoginSignInAsync(Scheme, subject, isPersistent: false);
        return result.Succeeded;
    }

    private async Task CreateAccountAsync(string subject, string email, CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var id = Guid.NewGuid();
        var user = new IdentityUser<Guid>
        {
            Id = id,
            UserName = $"google_{id:N}",
            Email = email,
            EmailConfirmed = true
        };
        var created = await users.CreateAsync(user);
        if (!created.Succeeded)
        {
            return;
        }

        var linked = await users.AddLoginAsync(user, new UserLoginInfo(Scheme, subject, "Google"));
        if (linked.Succeeded)
        {
            await transaction.CommitAsync(cancellationToken);
        }
    }
}
