using System.Security.Claims;
using CropPilot.Api.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace CropPilot.Api.Authentication;

public sealed class AuthSessions(CropPilotDbContext database, SignInManager<MainAccount> signIn)
{
    internal const string SessionIdClaim = "crop_pilot_session_id";

    internal async Task<AuthSession?> StartAsync(MainAccount account, Guid previousSessionId, HttpContext context)
    {
        var now = DateTimeOffset.UtcNow;
        var session = new AuthSession
        {
            MainAccountId = account.Id,
            SecurityStamp = account.SecurityStamp,
            ExpiresAt = now.AddHours(8)
        };
        await database.AuthSessions
            .Where(value => value.MainAccountId == account.Id && value.ExpiresAt <= now)
            .ExecuteDeleteAsync(context.RequestAborted);

        await using var transaction = await database.Database.BeginTransactionAsync(context.RequestAborted);
        if (previousSessionId != Guid.Empty && await RevokeAsync(previousSessionId, context.RequestAborted) == 0)
        {
            return null;
        }

        database.AuthSessions.Add(session);
        await database.SaveChangesAsync(context.RequestAborted);
        await transaction.CommitAsync(context.RequestAborted);
        await signIn.SignInWithClaimsAsync(account,
            new AuthenticationProperties { ExpiresUtc = session.ExpiresAt },
            [new Claim(SessionIdClaim, session.Id.ToString("D"))]);
        return session;
    }

    internal Task<AuthSession?> FindActiveAsync(Guid id, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        return database.AuthSessions.AsNoTracking().Include(value => value.MainAccount)
            .SingleOrDefaultAsync(value => value.Id == id && value.ExpiresAt > now
                && value.MainAccount.Status == MainAccount.Active
                && value.MainAccount.SecurityStamp == value.SecurityStamp, cancellationToken);
    }

    public Task<int> RevokeAsync(Guid id, CancellationToken cancellationToken) =>
        database.AuthSessions.Where(value => value.Id == id).ExecuteDeleteAsync(cancellationToken);

    public static Guid GetId(ClaimsPrincipal? principal) =>
        Guid.TryParse(principal?.FindFirstValue(SessionIdClaim), out var id) ? id : Guid.Empty;
}
