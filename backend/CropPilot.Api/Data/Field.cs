namespace CropPilot.Api.Data;

internal sealed class Field
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid MainAccountId { get; init; }
    public required string Name { get; init; }
    public decimal AreaSquareMeters { get; init; }
    public decimal? Latitude { get; init; }
    public decimal? Longitude { get; init; }
    public string? Description { get; init; }
}
