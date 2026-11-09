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
            user.ToTable("asp_net_users");
            user.HasIndex(account => account.NormalizedEmail)
                .HasDatabaseName("ux_asp_net_users_normalized_email")
                .IsUnique();
            user.HasIndex(account => account.NormalizedUserName)
                .HasDatabaseName("ux_asp_net_users_normalized_user_name")
                .IsUnique();
        });
        builder.Entity<IdentityUserLogin<Guid>>().ToTable("asp_net_user_logins");
        builder.Entity<IdentityUserClaim<Guid>>().ToTable("asp_net_user_claims");
        builder.Entity<IdentityUserToken<Guid>>().ToTable("asp_net_user_tokens");
    }
}
