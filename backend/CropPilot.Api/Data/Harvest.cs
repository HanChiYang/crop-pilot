namespace CropPilot.Api.Data;

/// <summary>儲存一筆栽培的實際採收日期與重量。</summary>
internal sealed class Harvest
{
    internal const string PlantingForeignKey = "fk_harvests_plantings_planting_id";

    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid PlantingId { get; init; }
    public Planting Planting { get; init; } = null!;
    public DateOnly HarvestDate { get; set; }
    public decimal WeightKg { get; set; }
    public string? Description { get; set; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; set; }
}
