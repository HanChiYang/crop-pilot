namespace CropPilot.Api.Data;

/// <summary>儲存一筆田區栽培紀錄及其栽種日期。</summary>
internal sealed class Planting
{
    internal const string Planned = "PLANNED";
    internal const string Growing = "GROWING";
    internal const string Harvesting = "HARVESTING";
    internal const string Completed = "COMPLETED";
    internal const string FieldForeignKey = "fk_plantings_fields_field_id";

    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid FieldId { get; init; }
    public Field Field { get; init; } = null!;
    public required string CropName { get; set; }
    public decimal AreaSquareMeters { get; set; }
    public string? Variety { get; set; }
    public DateOnly? PlannedPlantingDate { get; set; }
    public DateOnly? ActualPlantingDate { get; set; }
    public string Status { get; set; } = Planned;
    public string? Description { get; set; }
}
