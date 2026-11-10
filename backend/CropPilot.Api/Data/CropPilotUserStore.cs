using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace CropPilot.Api.Data;

internal sealed class CropPilotUserStore(CropPilotDbContext database, IdentityErrorDescriber errors)
    : IUserPasswordStore<MainAccount>, IUserLockoutStore<MainAccount>, IUserSecurityStampStore<MainAccount>
{
    public Task<MainAccount?> FindByIdAsync(string userId, CancellationToken cancellationToken) =>
        Guid.TryParse(userId, out var id)
            ? database.MainAccounts.Include(account => account.LocalCredential)
                .SingleOrDefaultAsync(account => account.Id == id, cancellationToken)
            : Task.FromResult<MainAccount?>(null);

    public Task<MainAccount?> FindByNameAsync(string username, CancellationToken cancellationToken) =>
        database.MainAccounts.Include(account => account.LocalCredential)
            .SingleOrDefaultAsync(account => account.LocalCredential!.Username == username, cancellationToken);

    public Task<string> GetUserIdAsync(MainAccount user, CancellationToken cancellationToken) =>
        Task.FromResult(user.Id.ToString());

    public Task<string?> GetUserNameAsync(MainAccount user, CancellationToken cancellationToken) =>
        Task.FromResult<string?>(user.LocalCredential?.Username ?? $"google_{user.Id:N}");

    public Task SetUserNameAsync(MainAccount user, string? userName, CancellationToken cancellationToken)
    {
        user.LocalCredential!.Username = userName!;
        return Task.CompletedTask;
    }

    public Task<string?> GetNormalizedUserNameAsync(MainAccount user, CancellationToken cancellationToken) =>
        GetUserNameAsync(user, cancellationToken);

    public Task SetNormalizedUserNameAsync(MainAccount user, string? normalizedName, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public async Task<IdentityResult> CreateAsync(MainAccount user, CancellationToken cancellationToken)
    {
        database.MainAccounts.Add(user);
        await database.SaveChangesAsync(cancellationToken);
        return IdentityResult.Success;
    }

    public async Task<IdentityResult> UpdateAsync(MainAccount user, CancellationToken cancellationToken)
    {
        if (user.LocalCredential is { } credential)
        {
            credential.ConcurrencyStamp = Guid.NewGuid();
        }

        try
        {
            await database.SaveChangesAsync(cancellationToken);
            return IdentityResult.Success;
        }
        catch (DbUpdateConcurrencyException)
        {
            return IdentityResult.Failed(errors.ConcurrencyFailure());
        }
    }

    public async Task<IdentityResult> DeleteAsync(MainAccount user, CancellationToken cancellationToken)
    {
        database.MainAccounts.Remove(user);
        await database.SaveChangesAsync(cancellationToken);
        return IdentityResult.Success;
    }

    public Task<string?> GetPasswordHashAsync(MainAccount user, CancellationToken cancellationToken) =>
        Task.FromResult(user.LocalCredential?.PasswordHash);

    public Task SetPasswordHashAsync(MainAccount user, string? passwordHash, CancellationToken cancellationToken)
    {
        user.LocalCredential!.PasswordHash = passwordHash;
        return Task.CompletedTask;
    }

    public Task<bool> HasPasswordAsync(MainAccount user, CancellationToken cancellationToken) =>
        Task.FromResult(user.LocalCredential?.PasswordHash is not null);

    public Task<string?> GetSecurityStampAsync(MainAccount user, CancellationToken cancellationToken) =>
        Task.FromResult(user.SecurityStamp);

    public Task SetSecurityStampAsync(MainAccount user, string stamp, CancellationToken cancellationToken)
    {
        user.SecurityStamp = stamp;
        return Task.CompletedTask;
    }

    public Task<int> GetAccessFailedCountAsync(MainAccount user, CancellationToken cancellationToken) =>
        Task.FromResult(user.LocalCredential?.AccessFailedCount ?? 0);

    public Task<int> IncrementAccessFailedCountAsync(MainAccount user, CancellationToken cancellationToken) =>
        Task.FromResult(++user.LocalCredential!.AccessFailedCount);

    public Task ResetAccessFailedCountAsync(MainAccount user, CancellationToken cancellationToken)
    {
        user.LocalCredential!.AccessFailedCount = 0;
        return Task.CompletedTask;
    }

    public Task<bool> GetLockoutEnabledAsync(MainAccount user, CancellationToken cancellationToken) =>
        Task.FromResult(user.LocalCredential?.LockoutEnabled ?? false);

    public Task SetLockoutEnabledAsync(MainAccount user, bool enabled, CancellationToken cancellationToken)
    {
        user.LocalCredential!.LockoutEnabled = enabled;
        return Task.CompletedTask;
    }

    public Task<DateTimeOffset?> GetLockoutEndDateAsync(MainAccount user, CancellationToken cancellationToken) =>
        Task.FromResult(user.LocalCredential?.LockoutEnd);

    public Task SetLockoutEndDateAsync(MainAccount user, DateTimeOffset? lockoutEnd, CancellationToken cancellationToken)
    {
        user.LocalCredential!.LockoutEnd = lockoutEnd;
        return Task.CompletedTask;
    }

    public void Dispose() { }
}
