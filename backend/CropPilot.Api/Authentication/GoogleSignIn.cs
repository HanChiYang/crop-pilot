using System.Security.Claims;
using CropPilot.Api.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Npgsql;

namespace CropPilot.Api.Authentication;

internal sealed class GoogleSignIn(
    UserManager<MainAccount> users,
    AuthSessions sessions,
    CropPilotDbContext database,
    ILogger<GoogleSignIn> logger)
{
    internal const string Scheme = "Google";
    internal const string FailedPath = "/login?error=google_sign_in_failed";

    internal static void Configure(AuthenticationBuilder authentication, IConfiguration configuration, bool isDevelopment)
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
            if (isDevelopment)
            {
                // Google returns through a top-level GET, which permits Lax cookies.
                options.CorrelationCookie.SameSite = SameSiteMode.Lax;
                options.CorrelationCookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                options.NonceCookie.SameSite = SameSiteMode.Lax;
                options.NonceCookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            }

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
                var previousSessionId = Guid.TryParse(context.Properties?.GetString(AuthSessions.SessionIdClaim),
                    out var id) ? id : Guid.Empty;
                await signIn.CompleteAsync(context.HttpContext, context.Principal, previousSessionId);
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

    private async Task CompleteAsync(HttpContext context, ClaimsPrincipal? principal, Guid previousSessionId)
    {
        var destination = FailedPath;
        try
        {
            destination = await SignInAsync(principal, previousSessionId, context);
        }
        catch (Exception exception) when (!context.RequestAborted.IsCancellationRequested)
        {
            logger.LogError(exception, "Unable to complete Google sign-in.");
        }

        context.Response.Redirect(destination);
    }

    private async Task<string> SignInAsync(ClaimsPrincipal? principal, Guid previousSessionId, HttpContext context)
    {
        var cancellationToken = context.RequestAborted;
        var subject = principal?.FindFirstValue("sub");
        if (string.IsNullOrWhiteSpace(subject) || subject.Length > 255)
        {
            return FailedPath;
        }

        var account = await FindAccountAsync(subject, cancellationToken);
        if (account is null)
        {
            var providedEmail = principal?.FindFirstValue("email")?.Trim();
            var verified = bool.TryParse(principal?.FindFirstValue("email_verified"), out var isVerified) && isVerified;
            if (string.IsNullOrEmpty(providedEmail) || providedEmail.Length > 256 || !verified)
            {
                return FailedPath;
            }

            var email = await database.Database.SqlQuery<string>($"SELECT lower({providedEmail}) AS \"Value\"")
                .SingleAsync(cancellationToken);
            if (!await database.UserProfiles.AnyAsync(profile => profile.Email == email, cancellationToken))
            {
                try
                {
                    await CreateAccountAsync(subject, email, cancellationToken);
                }
                catch (DbUpdateException exception) when (exception.InnerException is PostgresException
                    { SqlState: PostgresErrorCodes.UniqueViolation })
                {
                    // A simultaneous callback can win the unique email or external login constraint.
                    database.ChangeTracker.Clear();
                }
            }

            account = await FindAccountAsync(subject, cancellationToken);
            if (account is null)
            {
                return await GetExistingAccountPathAsync(email, cancellationToken);
            }
        }

        if (account.Status != MainAccount.Active || await users.IsLockedOutAsync(account))
        {
            return FailedPath;
        }

        return await sessions.StartAsync(account, previousSessionId, context) is null
            ? "/login?error=session_changed"
            : "/dashboard";
    }

    private Task<MainAccount?> FindAccountAsync(string subject, CancellationToken cancellationToken) =>
        database.MainAccounts.Include(account => account.LocalCredential)
            .SingleOrDefaultAsync(account => database.FederatedIdentities.Any(identity =>
                identity.MainAccountId == account.Id && identity.IdentityProvider == FederatedIdentity.Google
                && identity.ProviderUserId == subject), cancellationToken);

    private async Task CreateAccountAsync(string subject, string email, CancellationToken cancellationToken)
    {
        var account = new MainAccount { Status = MainAccount.Active };
        database.MainAccounts.Add(account);
        database.UserProfiles.Add(new UserProfile { MainAccountId = account.Id, Email = email });
        database.UserRoles.Add(new UserRole { MainAccountId = account.Id, Role = UserRole.User });
        database.FederatedIdentities.Add(new FederatedIdentity
        {
            MainAccountId = account.Id,
            IdentityProvider = FederatedIdentity.Google,
            ProviderUserId = subject
        });
        await database.SaveChangesAsync(cancellationToken);
    }

    private async Task<string> GetExistingAccountPathAsync(string email, CancellationToken cancellationToken)
    {
        var existing = await database.UserProfiles.Where(profile => profile.Email == email)
            .Select(profile => new
            {
                profile.MainAccount.Status,
                Local = profile.MainAccount.LocalCredential != null,
                Google = database.FederatedIdentities.Any(identity => identity.MainAccountId == profile.MainAccountId)
            }).SingleOrDefaultAsync(cancellationToken);
        if (existing is null)
        {
            return FailedPath;
        }

        return existing switch
        {
            { Status: MainAccount.Pending, Local: true } => "/login?error=account_pending",
            { Local: true, Google: true } => "/login?error=use_existing_sign_in",
            { Local: true } => "/login?error=use_existing_password",
            { Google: true } => "/login?error=use_existing_google",
            _ => FailedPath
        };
    }
}
