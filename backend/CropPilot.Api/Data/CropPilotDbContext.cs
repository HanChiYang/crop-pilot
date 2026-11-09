using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace CropPilot.Api.Data;

public sealed class CropPilotDbContext(DbContextOptions<CropPilotDbContext> options)
    : IdentityUserContext<IdentityUser<Guid>, Guid>(options)
{
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<IdentityUser<Guid>>(user =>
        {
            user.ToTable("main_account");
            user.HasIndex(account => account.NormalizedEmail)
                .HasDatabaseName("ux_main_account_normalized_email")
                .IsUnique();
            user.HasIndex(account => account.NormalizedUserName)
                .HasDatabaseName("ux_main_account_normalized_user_name")
                .IsUnique();
        });
        builder.Entity<IdentityUserLogin<Guid>>(login =>
        {
            login.ToTable("user_external_logins");
            login.Property<long>("Id");
            login.HasKey("Id");
            login.Property(entry => entry.LoginProvider).IsRequired();
            login.Property(entry => entry.ProviderKey).IsRequired();
            login.HasIndex(entry => new { entry.LoginProvider, entry.ProviderKey })
                .HasDatabaseName("ux_user_external_logins_provider_key")
                .IsUnique();
        });
        builder.Entity<IdentityUserClaim<Guid>>().ToTable("asp_net_user_claims");
        builder.Entity<IdentityUserToken<Guid>>(token =>
        {
            token.ToTable("asp_net_user_tokens");
            token.Property<long>("Id");
            token.HasKey("Id");
            token.Property(entry => entry.LoginProvider).IsRequired();
            token.Property(entry => entry.Name).IsRequired();
            token.HasIndex(entry => new { entry.UserId, entry.LoginProvider, entry.Name })
                .HasDatabaseName("ux_asp_net_user_tokens_user_provider_name")
                .IsUnique();
        });
    }
}
