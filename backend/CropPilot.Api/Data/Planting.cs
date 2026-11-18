namespace CropPilot.Api.Data;

/// <summary>儲存一筆田區栽培紀錄及其栽種與結束日期。</summary>
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
    public DateOnly? CompletionDate { get; set; }
    public string Status { get; set; } = Planned;
    public string? Description { get; set; }
}

/// <summary>交易內鎖定栽培後讀取的狀態與結束日，供作業及採收寫入核對日期。</summary>
internal sealed class PlantingLockState
{
    public string Status { get; init; } = string.Empty;
    public DateOnly? CompletionDate { get; init; }
}
