using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CropPilot.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFertilizationInventory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "agricultural_materials",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    main_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, collation: "C"),
                    material_type = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    stock_unit = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_agricultural_materials", x => x.id);
                    table.CheckConstraint("ck_agricultural_materials_name", "btrim(name) <> ''");
                    table.CheckConstraint("ck_agricultural_materials_stock_unit", "btrim(stock_unit) <> ''");
                    table.CheckConstraint("ck_agricultural_materials_type", "material_type = 'FERTILIZER'");
                    table.ForeignKey(
                        name: "fk_agricultural_materials_main_accounts_main_account_id",
                        column: x => x.main_account_id,
                        principalTable: "main_account",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "field_operations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    planting_id = table.Column<Guid>(type: "uuid", nullable: false),
                    operation_type = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    operation_date = table.Column<DateOnly>(type: "date", nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_field_operations", x => x.id);
                    table.CheckConstraint("ck_field_operations_type", "operation_type = 'FERTILIZATION'");
                    table.ForeignKey(
                        name: "fk_field_operations_plantings_planting_id",
                        column: x => x.planting_id,
                        principalTable: "plantings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "material_purchase_lots",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    material_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    purchased_on = table.Column<DateOnly>(type: "date", nullable: true),
                    available_from = table.Column<DateOnly>(type: "date", nullable: false),
                    received_quantity = table.Column<decimal>(type: "numeric(15,6)", precision: 15, scale: 6, nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(15,6)", precision: 15, scale: 6, nullable: true),
                    currency_code = table.Column<string>(type: "char(3)", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_material_purchase_lots", x => x.id);
                    table.CheckConstraint("ck_material_purchase_lots_currency", "currency_code = 'TWD'");
                    table.CheckConstraint("ck_material_purchase_lots_dates", "(source = 'PURCHASE' AND purchased_on IS NOT NULL AND purchased_on = available_from)\nOR (source = 'OPENING_STOCK' AND (purchased_on IS NULL OR purchased_on <= available_from))");
                    table.CheckConstraint("ck_material_purchase_lots_price", "unit_price BETWEEN 0 AND 999999999.999999");
                    table.CheckConstraint("ck_material_purchase_lots_quantity", "received_quantity BETWEEN 0.000001 AND 999999999.999999");
                    table.ForeignKey(
                        name: "fk_material_purchase_lots_agricultural_materials_material_id",
                        column: x => x.material_id,
                        principalTable: "agricultural_materials",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "fertilization_details",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    field_operation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    material_purchase_lot_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fertilizer_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(15,6)", precision: 15, scale: 6, nullable: false),
                    unit = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(15,6)", precision: 15, scale: 6, nullable: true),
                    currency_code = table.Column<string>(type: "char(3)", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false)
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

            migrationBuilder.CreateIndex(
                name: "ux_agricultural_materials_account_type_name",
                table: "agricultural_materials",
                columns: new[] { "main_account_id", "material_type", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_fertilization_details_field_operation_id_sort_order",
                table: "fertilization_details",
                columns: new[] { "field_operation_id", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "ix_fertilization_details_material_purchase_lot_id",
                table: "fertilization_details",
                column: "material_purchase_lot_id");

            migrationBuilder.CreateIndex(
                name: "ix_field_operations_planting_id_operation_type_operation_date_",
                table: "field_operations",
                columns: new[] { "planting_id", "operation_type", "operation_date", "created_at", "id" },
                descending: new[] { false, false, true, true, true });

            migrationBuilder.CreateIndex(
                name: "ix_material_purchase_lots_material_id_available_from_id",
                table: "material_purchase_lots",
                columns: new[] { "material_id", "available_from", "id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "fertilization_details");

            migrationBuilder.DropTable(
                name: "field_operations");

            migrationBuilder.DropTable(
                name: "material_purchase_lots");

            migrationBuilder.DropTable(
                name: "agricultural_materials");
        }
    }
}
