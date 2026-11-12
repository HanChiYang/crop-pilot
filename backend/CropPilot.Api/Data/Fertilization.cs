namespace CropPilot.Api.Data;

/// <summary>保存帳號管理的資材名稱及庫存單位。</summary>
internal sealed class AgriculturalMaterial
{
    internal const string Fertilizer = "FERTILIZER";
    internal const string Pesticide = "PESTICIDE";

    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid MainAccountId { get; init; }
    public required string Name { get; set; }
    public string MaterialType { get; init; } = Fertilizer;
    public required string StockUnit { get; set; }
    public DateTimeOffset CreatedAt { get; init; }
}

/// <summary>保存一次進貨或期初盤點的數量及單價。</summary>
internal sealed class MaterialPurchaseLot
{
    internal const string Purchase = "PURCHASE";
    internal const string OpeningStock = "OPENING_STOCK";
    internal const string TaiwanDollar = "TWD";

    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid MaterialId { get; init; }
    public AgriculturalMaterial Material { get; init; } = null!;
    public required string Source { get; init; }
    public DateOnly? PurchasedOn { get; set; }
    public DateOnly AvailableFrom { get; set; }
    public decimal ReceivedQuantity { get; set; }
    public decimal? UnitPrice { get; set; }
    public string CurrencyCode { get; init; } = TaiwanDollar;
    public DateTimeOffset CreatedAt { get; init; }
}

/// <summary>保存一次田間作業所屬的栽培紀錄及實際作業日。</summary>
internal sealed class FieldOperation
{
    internal const string MaterialApplication = "MATERIAL_APPLICATION";
    internal const string Irrigation = "IRRIGATION";

    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid PlantingId { get; init; }
    public Planting Planting { get; init; } = null!;
    public string OperationType { get; init; } = MaterialApplication;
    public DateOnly OperationDate { get; set; }
    public string? Description { get; set; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; set; }
    public MaterialApplication? Application { get; set; }
    public IrrigationDetail? IrrigationDetail { get; set; }
}

/// <summary>保存一次資材施用共用的噴施水量。</summary>
internal sealed class MaterialApplication
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid FieldOperationId { get; init; }
    public FieldOperation FieldOperation { get; init; } = null!;
    public decimal? SprayWaterVolumeLiters { get; set; }
    public List<MaterialApplicationDetail> Details { get; } = [];
}

/// <summary>保存一次資材施用的批次用量與資材快照。</summary>
internal sealed class MaterialApplicationDetail
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid MaterialApplicationId { get; init; }
    public MaterialApplication Application { get; init; } = null!;
    public Guid MaterialPurchaseLotId { get; set; }
    public MaterialPurchaseLot Lot { get; set; } = null!;
    public required string MaterialName { get; set; }
    public required string ApplicationMethod { get; set; }
    public decimal Quantity { get; set; }
    public required string Unit { get; set; }
    public decimal? UnitPrice { get; set; }
    public string CurrencyCode { get; set; } = MaterialPurchaseLot.TaiwanDollar;
    public int SortOrder { get; set; }
}
