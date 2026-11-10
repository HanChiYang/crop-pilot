namespace CropPilot.Api.Data;

internal sealed class AuthSession
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid MainAccountId { get; init; }
    public MainAccount MainAccount { get; init; } = null!;
    public string? SecurityStamp { get; init; }
    public DateTimeOffset ExpiresAt { get; init; }
}
