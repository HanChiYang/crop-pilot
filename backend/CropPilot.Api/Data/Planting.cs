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
    public required string CropName { get; init; }
    public string? Variety { get; init; }
    public DateOnly? PlannedPlantingDate { get; init; }
    public DateOnly? ActualPlantingDate { get; init; }
    public string Status { get; init; } = Planned;
    public string? Description { get; init; }
}
