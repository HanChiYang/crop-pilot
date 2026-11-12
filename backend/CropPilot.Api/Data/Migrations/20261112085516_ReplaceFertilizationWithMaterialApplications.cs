using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CropPilot.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class ReplaceFertilizationWithMaterialApplications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM field_operations WHERE operation_type = 'FERTILIZATION';");

            migrationBuilder.DropTable(
                name: "fertilization_details");

            migrationBuilder.DropCheckConstraint(
                name: "ck_field_operations_type",
                table: "field_operations");

            migrationBuilder.DropCheckConstraint(
                name: "ck_agricultural_materials_stock_unit",
                table: "agricultural_materials");

            migrationBuilder.DropCheckConstraint(
                name: "ck_agricultural_materials_type",
                table: "agricultural_materials");

            migrationBuilder.CreateTable(
                name: "material_applications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    field_operation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    spray_water_volume_liters = table.Column<decimal>(type: "numeric(15,6)", precision: 15, scale: 6, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_material_applications", x => x.id);
                    table.CheckConstraint("ck_material_applications_water", "spray_water_volume_liters BETWEEN 0.000001 AND 999999999.999999");
                    table.ForeignKey(
                        name: "fk_material_applications_field_operations_field_operation_id",
                        column: x => x.field_operation_id,
                        principalTable: "field_operations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "material_application_details",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    material_application_id = table.Column<Guid>(type: "uuid", nullable: false),
                    material_purchase_lot_id = table.Column<Guid>(type: "uuid", nullable: false),
                    material_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    application_method = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(15,6)", precision: 15, scale: 6, nullable: false),
                    unit = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(15,6)", precision: 15, scale: 6, nullable: true),
                    currency_code = table.Column<string>(type: "char(3)", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_material_application_details", x => x.id);
                    table.CheckConstraint("ck_material_application_details_currency", "currency_code = 'TWD'");
                    table.CheckConstraint("ck_material_application_details_method", "application_method IN ('spray', 'broadcast')");
                    table.CheckConstraint("ck_material_application_details_price", "unit_price BETWEEN 0 AND 999999999.999999");
                    table.CheckConstraint("ck_material_application_details_quantity", "quantity BETWEEN 0.000001 AND 999999999.999999");
                    table.CheckConstraint("ck_material_application_details_sort_order", "sort_order >= 0");
                    table.ForeignKey(
                        name: "fk_material_application_details_material_application_material_",
                        column: x => x.material_application_id,
                        principalTable: "material_applications",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_material_application_details_material_purchase_lots_materia",
                        column: x => x.material_purchase_lot_id,
                        principalTable: "material_purchase_lots",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_field_operations_type",
                table: "field_operations",
                sql: "operation_type IN ('MATERIAL_APPLICATION', 'IRRIGATION')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_agricultural_materials_stock_unit",
                table: "agricultural_materials",
                sql: "btrim(stock_unit) <> '' AND (material_type <> 'PESTICIDE' OR stock_unit IN ('公升', '毫升', '公斤', '公克'))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_agricultural_materials_type",
                table: "agricultural_materials",
                sql: "material_type IN ('FERTILIZER', 'PESTICIDE')");

            migrationBuilder.CreateIndex(
                name: "ix_material_application_details_material_application_id_sort_o",
                table: "material_application_details",
                columns: new[] { "material_application_id", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "ix_material_application_details_material_purchase_lot_id",
                table: "material_application_details",
                column: "material_purchase_lot_id");

            migrationBuilder.CreateIndex(
                name: "ix_material_applications_field_operation_id",
                table: "material_applications",
                column: "field_operation_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM field_operations WHERE operation_type = 'MATERIAL_APPLICATION';");
            migrationBuilder.Sql("""
                DELETE FROM material_purchase_lots
                WHERE material_id IN (SELECT id FROM agricultural_materials WHERE material_type = 'PESTICIDE');
                """);
            migrationBuilder.Sql("DELETE FROM agricultural_materials WHERE material_type = 'PESTICIDE';");

            migrationBuilder.DropTable(
                name: "material_application_details");

            migrationBuilder.DropTable(
                name: "material_applications");

            migrationBuilder.DropCheckConstraint(
                name: "ck_field_operations_type",
                table: "field_operations");

            migrationBuilder.DropCheckConstraint(
                name: "ck_agricultural_materials_stock_unit",
                table: "agricultural_materials");

            migrationBuilder.DropCheckConstraint(
                name: "ck_agricultural_materials_type",
                table: "agricultural_materials");

            migrationBuilder.CreateTable(
                name: "fertilization_details",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    field_operation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    material_purchase_lot_id = table.Column<Guid>(type: "uuid", nullable: false),
                    currency_code = table.Column<string>(type: "char(3)", nullable: false),
                    fertilizer_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(15,6)", precision: 15, scale: 6, nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    unit = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(15,6)", precision: 15, scale: 6, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fertilization_details", x => x.id);
                    table.CheckConstraint("ck_fertilization_details_currency", "currency_code = 'TWD'");
                    table.CheckConstraint("ck_fertilization_details_price", "unit_price BETWEEN 0 AND 999999999.999999");
                    table.CheckConstraint("ck_fertilization_details_quantity", "quantity BETWEEN 0.000001 AND 999999999.999999");
                    table.CheckConstraint("ck_fertilization_details_sort_order", "sort_order >= 0");
                    table.ForeignKey(
                        name: "fk_fertilization_details_field_operations_field_operation_id",
                        column: x => x.field_operation_id,
                        principalTable: "field_operations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_fertilization_details_material_purchase_lots_material_purch",
                        column: x => x.material_purchase_lot_id,
                        principalTable: "material_purchase_lots",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_field_operations_type",
                table: "field_operations",
                sql: "operation_type IN ('FERTILIZATION', 'IRRIGATION')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_agricultural_materials_stock_unit",
                table: "agricultural_materials",
                sql: "btrim(stock_unit) <> ''");

            migrationBuilder.AddCheckConstraint(
                name: "ck_agricultural_materials_type",
                table: "agricultural_materials",
                sql: "material_type = 'FERTILIZER'");

            migrationBuilder.CreateIndex(
                name: "ix_fertilization_details_field_operation_id_sort_order",
                table: "fertilization_details",
                columns: new[] { "field_operation_id", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "ix_fertilization_details_material_purchase_lot_id",
                table: "fertilization_details",
                column: "material_purchase_lot_id");
        }
    }
}
