using Microsoft.EntityFrameworkCore;

namespace CropPilot.Api.Data;

public sealed class CropPilotDbContext(DbContextOptions<CropPilotDbContext> options) : DbContext(options)
{
    public DbSet<MainAccount> MainAccounts => Set<MainAccount>();
    public DbSet<LocalCredential> LocalCredentials => Set<LocalCredential>();
    public DbSet<FederatedIdentity> FederatedIdentities => Set<FederatedIdentity>();
    public DbSet<UserProfile> UserProfiles => Set<UserProfile>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    internal DbSet<AuthSession> AuthSessions => Set<AuthSession>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.Entity<AuthSession>(session =>
        {
            session.ToTable("auth_sessions");
            session.HasOne(value => value.MainAccount).WithMany().HasForeignKey(value => value.MainAccountId);
            session.Property(value => value.SecurityStamp).HasMaxLength(256);
        });

        builder.Entity<MainAccount>(account =>
        {
            account.ToTable("main_account", table => table.HasCheckConstraint(
                "ck_main_account_status", "status IN ('PENDING', 'ACTIVE', 'DISABLED')"));
            account.Property(value => value.Status).HasMaxLength(32).HasDefaultValue(MainAccount.Pending);
            account.Property(value => value.SecurityStamp).HasMaxLength(256);
        });

        builder.Entity<LocalCredential>(credential =>
        {
            credential.ToTable("local_credentials", table => table.HasCheckConstraint(
                "ck_local_credentials_username", "length(username) > 0"));
            credential.HasOne<MainAccount>().WithOne(account => account.LocalCredential)
                .HasForeignKey<LocalCredential>(value => value.MainAccountId);
            credential.Property(value => value.Username).HasMaxLength(256);
            credential.Property(value => value.PasswordHash).HasMaxLength(512).IsRequired();
            credential.Property(value => value.ConcurrencyStamp).IsConcurrencyToken();
        });

        builder.Entity<FederatedIdentity>(identity =>
        {
            identity.ToTable("federated_identities", table => table.HasCheckConstraint(
                "ck_federated_identities_provider", "identity_provider = 'GOOGLE'"));
            identity.HasOne(value => value.MainAccount).WithMany()
                .HasForeignKey(value => value.MainAccountId);
            identity.Property(value => value.IdentityProvider).HasMaxLength(32);
            identity.Property(value => value.ProviderUserId).HasMaxLength(255);
            identity.HasIndex(value => new { value.IdentityProvider, value.ProviderUserId }).IsUnique();
        });

        builder.Entity<UserProfile>(profile =>
        {
            profile.ToTable("user_profiles");
            profile.HasOne(value => value.MainAccount).WithOne()
                .HasForeignKey<UserProfile>(value => value.MainAccountId);
            profile.Property(value => value.Email).HasMaxLength(256);
            profile.Property(value => value.DisplayName).HasMaxLength(120);
            profile.Property(value => value.Surname).HasMaxLength(120);
            profile.Property(value => value.GivenName).HasMaxLength(120);
            profile.HasIndex(value => value.Email).IsUnique();
        });

        builder.Entity<UserRole>(role =>
        {
            role.ToTable("user_roles", table => table.HasCheckConstraint(
                "ck_user_roles_role", "role IN ('USER', 'ADMIN')"));
            role.HasOne<MainAccount>().WithMany().HasForeignKey(value => value.MainAccountId);
            role.Property(value => value.Role).HasMaxLength(16);
            role.HasIndex(value => new { value.MainAccountId, value.Role }).IsUnique();
        });
    }
}
