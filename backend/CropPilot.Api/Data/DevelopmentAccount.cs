using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace CropPilot.Api.Data;

internal static class DevelopmentAccount
{
    internal static async Task SeedAsync(IServiceProvider services, IConfiguration configuration)
    {
        var username = configuration["DevelopmentAccount:Username"]?.Trim();
        var password = configuration["DevelopmentAccount:Password"];
        if (string.IsNullOrEmpty(username) || username.Length > 256 || string.IsNullOrEmpty(password))
        {
            throw new InvalidOperationException("Configure DevelopmentAccount:Username and DevelopmentAccount:Password first.");
        }

        await using var scope = services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<CropPilotDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<MainAccount>>();
        var duplicate = await database.LocalCredentials
            .FromSql($"SELECT * FROM local_credentials WHERE lower(username) = lower({username})")
            .AnyAsync();
        if (duplicate)
        {
            Console.WriteLine("An account with this username already exists.");
            return;
        }

        await using var transaction = await database.Database.BeginTransactionAsync();
        var account = new MainAccount
        {
            Status = MainAccount.Active,
            LocalCredential = new LocalCredential { Username = username }
        };
        var created = await users.CreateAsync(account, password);
        if (!created.Succeeded)
        {
            throw new InvalidOperationException(string.Join(" ", created.Errors.Select(error => error.Description)));
        }

        database.UserProfiles.Add(new UserProfile { MainAccountId = account.Id });
        database.UserRoles.Add(new UserRole { MainAccountId = account.Id, Role = UserRole.User });
        await database.SaveChangesAsync();
        await transaction.CommitAsync();
        Console.WriteLine("Development account created.");
    }
}
