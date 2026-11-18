using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CropPilot.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class NormalizeEnumCodes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_material_applications_method",
                table: "material_applications");

            migrationBuilder.DropCheckConstraint(
                name: "ck_material_applications_water",
                table: "material_applications");

            migrationBuilder.DropCheckConstraint(
                name: "ck_irrigation_details_method",
                table: "irrigation_details");

            migrationBuilder.DropCheckConstraint(
                name: "ck_agricultural_materials_package",
                table: "agricultural_materials");

            migrationBuilder.DropCheckConstraint(
                name: "ck_agricultural_materials_stock_unit",
                table: "agricultural_materials");

            // 列舉值改存大寫代碼；農藥庫存單位由中文改為與包裝規格相同的代碼。
            migrationBuilder.Sql("""
                UPDATE material_applications SET application_method = upper(application_method);
                UPDATE irrigation_details SET method = upper(method);
                UPDATE agricultural_materials SET package_unit = upper(package_unit) WHERE package_unit IS NOT NULL;
                UPDATE agricultural_materials SET stock_unit = CASE stock_unit
                    WHEN '公升' THEN 'LITER' WHEN '毫升' THEN 'MILLILITER'
                    WHEN '公斤' THEN 'KILOGRAM' WHEN '公克' THEN 'GRAM' END
                WHERE material_type = 'PESTICIDE';
                """);

            migrationBuilder.AddCheckConstraint(
                name: "ck_material_applications_method",
                table: "material_applications",
                sql: "application_method IN ('SPRAY', 'BROADCAST')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_material_applications_water",
                table: "material_applications",
                sql: "spray_water_volume_liters IS NULL OR (application_method = 'SPRAY' AND spray_water_volume_liters BETWEEN 0.000001 AND 999999999.999999)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_irrigation_details_method",
                table: "irrigation_details",
                sql: "method IN ('DRIP', 'SPRINKLER', 'FURROW', 'MANUAL', 'OTHER')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_agricultural_materials_package",
                table: "agricultural_materials",
                sql: "(package_quantity IS NULL AND package_unit IS NULL)\r\nOR (package_quantity IS NOT NULL AND package_quantity BETWEEN 0.000001 AND 999999999.999999\r\n    AND package_unit IS NOT NULL AND package_unit IN ('LITER', 'MILLILITER', 'KILOGRAM', 'GRAM'))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_agricultural_materials_stock_unit",
                table: "agricultural_materials",
                sql: "btrim(stock_unit) <> '' AND (material_type <> 'PESTICIDE' OR stock_unit IN ('LITER', 'MILLILITER', 'KILOGRAM', 'GRAM'))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_material_applications_method",
                table: "material_applications");

            migrationBuilder.DropCheckConstraint(
                name: "ck_material_applications_water",
                table: "material_applications");

            migrationBuilder.DropCheckConstraint(
                name: "ck_irrigation_details_method",
                table: "irrigation_details");

            migrationBuilder.DropCheckConstraint(
                name: "ck_agricultural_materials_package",
                table: "agricultural_materials");

            migrationBuilder.DropCheckConstraint(
                name: "ck_agricultural_materials_stock_unit",
                table: "agricultural_materials");

            migrationBuilder.Sql("""
                UPDATE material_applications SET application_method = lower(application_method);
                UPDATE irrigation_details SET method = lower(method);
                UPDATE agricultural_materials SET package_unit = lower(package_unit) WHERE package_unit IS NOT NULL;
                UPDATE agricultural_materials SET stock_unit = CASE stock_unit
                    WHEN 'LITER' THEN '公升' WHEN 'MILLILITER' THEN '毫升'
                    WHEN 'KILOGRAM' THEN '公斤' WHEN 'GRAM' THEN '公克' END
                WHERE material_type = 'PESTICIDE';
                """);

            migrationBuilder.AddCheckConstraint(
                name: "ck_material_applications_method",
                table: "material_applications",
                sql: "application_method IN ('spray', 'broadcast')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_material_applications_water",
                table: "material_applications",
                sql: "spray_water_volume_liters IS NULL OR (application_method = 'spray' AND spray_water_volume_liters BETWEEN 0.000001 AND 999999999.999999)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_irrigation_details_method",
                table: "irrigation_details",
                sql: "method IN ('drip', 'sprinkler', 'furrow', 'manual', 'other')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_agricultural_materials_package",
                table: "agricultural_materials",
                sql: "(package_quantity IS NULL AND package_unit IS NULL)\r\nOR (package_quantity IS NOT NULL AND package_quantity BETWEEN 0.000001 AND 999999999.999999\r\n    AND package_unit IS NOT NULL AND package_unit IN ('liter', 'milliliter', 'kilogram', 'gram'))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_agricultural_materials_stock_unit",
                table: "agricultural_materials",
                sql: "btrim(stock_unit) <> '' AND (material_type <> 'PESTICIDE' OR stock_unit IN ('公升', '毫升', '公斤', '公克'))");
        }
    }
}
