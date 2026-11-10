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
    internal DbSet<Field> Fields => Set<Field>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.Entity<Field>(field =>
        {
            field.ToTable("fields", table =>
            {
                // Match the whitespace removed by .NET's string.Trim(), regardless of database locale.
                table.HasCheckConstraint("ck_fields_name", """
                    btrim(name, U&'\0009\000A\000B\000C\000D\0020\0085\00A0\1680\2000\2001\2002\2003\2004\2005\2006\2007\2008\2009\200A\2028\2029\202F\205F\3000') <> ''
                    """);
                table.HasCheckConstraint("ck_fields_area_square_meters",
                    "area_square_meters > 0 AND area_square_meters <= 9999999999.9999");
                table.HasCheckConstraint("ck_fields_coordinates", """
                    (latitude IS NULL AND longitude IS NULL) OR
                    (latitude IS NOT NULL AND longitude IS NOT NULL AND
                     latitude BETWEEN -90 AND 90 AND longitude BETWEEN -180 AND 180)
                    """);
            });
            field.HasOne<MainAccount>().WithMany().HasForeignKey(value => value.MainAccountId)
                .OnDelete(DeleteBehavior.Restrict);
            field.Property(value => value.Name).HasMaxLength(120).UseCollation("C");
            field.Property(value => value.AreaSquareMeters).HasPrecision(14, 4);
            field.Property(value => value.Latitude).HasPrecision(9, 6);
            field.Property(value => value.Longitude).HasPrecision(9, 6);
            field.Property(value => value.Description).HasMaxLength(1000);
            field.HasIndex(value => new { value.MainAccountId, value.Name }).IsUnique();
        });

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
