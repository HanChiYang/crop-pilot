namespace CropPilot.Api.Data;

/// <summary>保存一筆灌溉作業的方式及選填用水量。</summary>
internal sealed class IrrigationDetail
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid FieldOperationId { get; init; }
    public FieldOperation FieldOperation { get; init; } = null!;
    public required string Method { get; set; }
    public decimal? WaterVolumeLiters { get; set; }
}
