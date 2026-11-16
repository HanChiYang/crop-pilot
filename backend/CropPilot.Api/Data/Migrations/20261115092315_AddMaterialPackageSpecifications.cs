using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CropPilot.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMaterialPackageSpecifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_agricultural_materials_account_type_name",
                table: "agricultural_materials");

            migrationBuilder.AddColumn<decimal>(
                name: "package_quantity",
                table: "agricultural_materials",
                type: "numeric(15,6)",
                precision: 15,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "package_unit",
                table: "agricultural_materials",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ux_agricultural_materials_account_type_name",
                table: "agricultural_materials",
                columns: new[] { "main_account_id", "material_type", "name" },
                unique: true,
                filter: "package_quantity IS NULL");

            migrationBuilder.CreateIndex(
                name: "ux_agricultural_materials_account_type_name_package",
                table: "agricultural_materials",
                columns: new[] { "main_account_id", "material_type", "name", "package_quantity", "package_unit" },
                unique: true,
                filter: "package_quantity IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_agricultural_materials_package",
                table: "agricultural_materials",
                sql: "(package_quantity IS NULL AND package_unit IS NULL)\r\nOR (package_quantity IS NOT NULL AND package_quantity BETWEEN 0.000001 AND 999999999.999999\r\n    AND package_unit IS NOT NULL AND package_unit IN ('liter', 'milliliter', 'kilogram', 'gram'))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_agricultural_materials_account_type_name",
                table: "agricultural_materials");

            migrationBuilder.DropIndex(
                name: "ux_agricultural_materials_account_type_name_package",
                table: "agricultural_materials");

            migrationBuilder.DropCheckConstraint(
                name: "ck_agricultural_materials_package",
                table: "agricultural_materials");

            migrationBuilder.DropColumn(
                name: "package_quantity",
                table: "agricultural_materials");

            migrationBuilder.DropColumn(
                name: "package_unit",
                table: "agricultural_materials");

            migrationBuilder.CreateIndex(
                name: "ux_agricultural_materials_account_type_name",
                table: "agricultural_materials",
                columns: new[] { "main_account_id", "material_type", "name" },
                unique: true);
        }
    }
}
