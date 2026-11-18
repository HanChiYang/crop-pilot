using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CropPilot.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class DeriveApplicationCostsFromLots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_material_application_details_currency",
                table: "material_application_details");

            migrationBuilder.DropCheckConstraint(
                name: "ck_material_application_details_price",
                table: "material_application_details");

            migrationBuilder.DropColumn(
                name: "currency_code",
                table: "material_application_details");

            migrationBuilder.DropColumn(
                name: "material_name",
                table: "material_application_details");

            migrationBuilder.DropColumn(
                name: "unit",
                table: "material_application_details");

            migrationBuilder.DropColumn(
                name: "unit_price",
                table: "material_application_details");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "currency_code",
                table: "material_application_details",
                type: "char(3)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "material_name",
                table: "material_application_details",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "unit",
                table: "material_application_details",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "unit_price",
                table: "material_application_details",
                type: "numeric(15,6)",
                precision: 15,
                scale: 6,
                nullable: true);

            // 降版時從批次及資材主檔回填舊快照欄位，再移除暫用的預設值。
            migrationBuilder.Sql("""
                UPDATE material_application_details AS detail
                SET material_name = material.name,
                    unit = material.stock_unit,
                    unit_price = lot.unit_price,
                    currency_code = lot.currency_code
                FROM material_purchase_lots AS lot
                JOIN agricultural_materials AS material ON material.id = lot.material_id
                WHERE lot.id = detail.material_purchase_lot_id;
                ALTER TABLE material_application_details
                    ALTER COLUMN currency_code DROP DEFAULT,
                    ALTER COLUMN material_name DROP DEFAULT,
                    ALTER COLUMN unit DROP DEFAULT;
                """);

            migrationBuilder.AddCheckConstraint(
                name: "ck_material_application_details_currency",
                table: "material_application_details",
                sql: "currency_code = 'TWD'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_material_application_details_price",
                table: "material_application_details",
                sql: "unit_price BETWEEN 0 AND 999999999.999999");
        }
    }
}
