namespace CropPilot.Api.Data;

/// <summary>保存一筆灌溉作業的方式及選填用水量。</summary>
internal sealed class IrrigationDetail
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid FieldOperationId { get; init; }
    public FieldOperation FieldOperation { get; init; } = null!;

    /// <summary>固定為灌溉，與作業識別碼組成複合外鍵，避免灌溉資料接到其他類型的作業。</summary>
    public string OperationType { get; init; } = FieldOperation.Irrigation;
    public required string Method { get; set; }
    public decimal? WaterVolumeLiters { get; set; }
}
