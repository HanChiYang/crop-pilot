using Microsoft.EntityFrameworkCore;

namespace CropPilot.Api.Data;

/// <summary>定義施肥作業及肥料庫存的資料庫約束。</summary>
internal static class FertilizationModel
{
    internal const string MaterialNameIndex = "ux_agricultural_materials_account_type_name";

    /// <summary>將施肥及庫存資料模型加入共用資料庫內容。</summary>
    /// <param name="builder">應用程式的 EF Core 模型建構器。</param>
    internal static void Configure(ModelBuilder builder)
    {
        builder.Entity<AgriculturalMaterial>(material =>
        {
            material.ToTable("agricultural_materials", table =>
            {
                table.HasCheckConstraint("ck_agricultural_materials_type", "material_type IN ('FERTILIZER', 'PESTICIDE')");
                table.HasCheckConstraint("ck_agricultural_materials_name", "btrim(name) <> ''");
                table.HasCheckConstraint("ck_agricultural_materials_stock_unit", "btrim(stock_unit) <> '' AND (material_type <> 'PESTICIDE' OR stock_unit IN ('公升', '毫升', '公斤', '公克'))");
            });
            material.HasOne<MainAccount>().WithMany().HasForeignKey(value => value.MainAccountId)
                .OnDelete(DeleteBehavior.Restrict);
            material.Property(value => value.Name).HasMaxLength(100).UseCollation("C");
            material.Property(value => value.MaterialType).HasMaxLength(24);
            material.Property(value => value.StockUnit).HasMaxLength(20);
            material.Property(value => value.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            material.HasIndex(value => new { value.MainAccountId, value.MaterialType, value.Name })
                .IsUnique().HasDatabaseName(MaterialNameIndex);
        });

        builder.Entity<MaterialPurchaseLot>(lot =>
        {
            lot.ToTable("material_purchase_lots", table =>
            {
                table.HasCheckConstraint("ck_material_purchase_lots_dates", """
                    (source = 'PURCHASE' AND purchased_on IS NOT NULL AND purchased_on = available_from)
                    OR (source = 'OPENING_STOCK' AND (purchased_on IS NULL OR purchased_on <= available_from))
                    """);
                table.HasCheckConstraint("ck_material_purchase_lots_quantity",
                    "received_quantity BETWEEN 0.000001 AND 999999999.999999");
                table.HasCheckConstraint("ck_material_purchase_lots_price",
                    "unit_price BETWEEN 0 AND 999999999.999999");
                table.HasCheckConstraint("ck_material_purchase_lots_currency", "currency_code = 'TWD'");
            });
            lot.HasOne(value => value.Material).WithMany().HasForeignKey(value => value.MaterialId)
                .OnDelete(DeleteBehavior.Restrict);
            lot.Property(value => value.Source).HasMaxLength(24);
            lot.Property(value => value.ReceivedQuantity).HasPrecision(15, 6);
            lot.Property(value => value.UnitPrice).HasPrecision(15, 6);
            lot.Property(value => value.CurrencyCode).HasColumnType("char(3)");
            lot.Property(value => value.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            lot.HasIndex(value => new { value.MaterialId, value.AvailableFrom, value.Id });
        });

        builder.Entity<FieldOperation>(operation =>
        {
            operation.ToTable("field_operations", table =>
                table.HasCheckConstraint("ck_field_operations_type", "operation_type IN ('MATERIAL_APPLICATION', 'IRRIGATION')"));
            operation.HasOne(value => value.Planting).WithMany().HasForeignKey(value => value.PlantingId)
                .OnDelete(DeleteBehavior.Restrict);
            operation.Property(value => value.OperationType).HasMaxLength(24);
            operation.Property(value => value.Description).HasMaxLength(500);
            operation.Property(value => value.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            operation.Property(value => value.UpdatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            operation.HasIndex(value => new
            {
                value.PlantingId, value.OperationType, value.OperationDate, value.CreatedAt, value.Id
            }).IsDescending(false, false, true, true, true);
        });

        builder.Entity<MaterialApplication>(application =>
        {
            application.ToTable("material_applications", table =>
                table.HasCheckConstraint("ck_material_applications_water",
                    "spray_water_volume_liters BETWEEN 0.000001 AND 999999999.999999"));
            application.HasOne(value => value.FieldOperation).WithOne(value => value.Application)
                .HasForeignKey<MaterialApplication>(value => value.FieldOperationId).OnDelete(DeleteBehavior.Cascade);
            application.Property(value => value.SprayWaterVolumeLiters).HasPrecision(15, 6);
        });

        builder.Entity<MaterialApplicationDetail>(detail =>
        {
            detail.ToTable("material_application_details", table =>
            {
                table.HasCheckConstraint("ck_material_application_details_quantity",
                    "quantity BETWEEN 0.000001 AND 999999999.999999");
                table.HasCheckConstraint("ck_material_application_details_price",
                    "unit_price BETWEEN 0 AND 999999999.999999");
                table.HasCheckConstraint("ck_material_application_details_currency", "currency_code = 'TWD'");
                table.HasCheckConstraint("ck_material_application_details_method", "application_method IN ('spray', 'broadcast')");
                table.HasCheckConstraint("ck_material_application_details_sort_order", "sort_order >= 0");
            });
            detail.HasOne(value => value.Application).WithMany(value => value.Details)
                .HasForeignKey(value => value.MaterialApplicationId).OnDelete(DeleteBehavior.Cascade);
            detail.HasOne(value => value.Lot).WithMany().HasForeignKey(value => value.MaterialPurchaseLotId)
                .OnDelete(DeleteBehavior.Restrict);
            detail.Property(value => value.MaterialName).HasMaxLength(100);
            detail.Property(value => value.ApplicationMethod).HasMaxLength(24);
            detail.Property(value => value.Quantity).HasPrecision(15, 6);
            detail.Property(value => value.Unit).HasMaxLength(20);
            detail.Property(value => value.UnitPrice).HasPrecision(15, 6);
            detail.Property(value => value.CurrencyCode).HasColumnType("char(3)");
            detail.HasIndex(value => new { value.MaterialApplicationId, value.SortOrder });
        });
    }
}
