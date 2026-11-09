namespace CropPilot.Api.Data;

public sealed class MainAccount
{
    public const string Pending = "PENDING";
    public const string Active = "ACTIVE";
    public const string Disabled = "DISABLED";

    public Guid Id { get; init; } = Guid.NewGuid();
    public string Status { get; set; } = Pending;
    public string? SecurityStamp { get; set; } = Guid.NewGuid().ToString("N");
    public LocalCredential? LocalCredential { get; init; }
}

public sealed class LocalCredential
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid MainAccountId { get; init; }
    public required string Username { get; set; }
    public string? PasswordHash { get; set; }
    public int AccessFailedCount { get; set; }
    public bool LockoutEnabled { get; set; } = true;
    public DateTimeOffset? LockoutEnd { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
}

public sealed class FederatedIdentity
{
    public const string Google = "GOOGLE";

    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid MainAccountId { get; init; }
    public MainAccount MainAccount { get; init; } = null!;
    public required string IdentityProvider { get; init; }
    public required string ProviderUserId { get; init; }
}

public sealed class UserProfile
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid MainAccountId { get; init; }
    public MainAccount MainAccount { get; init; } = null!;
    public string? Email { get; init; }
    public string? DisplayName { get; init; }
    public string? Surname { get; init; }
    public string? GivenName { get; init; }
}

public sealed class UserRole
{
    public const string User = "USER";
    public const string Admin = "ADMIN";

    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid MainAccountId { get; init; }
    public required string Role { get; init; }
}
