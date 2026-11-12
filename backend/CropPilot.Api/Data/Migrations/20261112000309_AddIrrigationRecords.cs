using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CropPilot.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddIrrigationRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_field_operations_type",
                table: "field_operations");

            migrationBuilder.CreateTable(
                name: "irrigation_details",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    field_operation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    method = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    water_volume_liters = table.Column<decimal>(type: "numeric(15,6)", precision: 15, scale: 6, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_irrigation_details", x => x.id);
                    table.CheckConstraint("ck_irrigation_details_method", "method IN ('drip', 'sprinkler', 'furrow', 'manual', 'other')");
                    table.CheckConstraint("ck_irrigation_details_water_volume_liters", "water_volume_liters > 0 AND water_volume_liters < 1000000000");
                    table.ForeignKey(
                        name: "fk_irrigation_details_field_operations_field_operation_id",
                        column: x => x.field_operation_id,
                        principalTable: "field_operations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_field_operations_type",
                table: "field_operations",
                sql: "operation_type IN ('FERTILIZATION', 'IRRIGATION')");

            migrationBuilder.CreateIndex(
                name: "ix_irrigation_details_field_operation_id",
                table: "irrigation_details",
                column: "field_operation_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM field_operations WHERE operation_type = 'IRRIGATION';");

            migrationBuilder.DropTable(
                name: "irrigation_details");

            migrationBuilder.DropCheckConstraint(
                name: "ck_field_operations_type",
                table: "field_operations");

            migrationBuilder.AddCheckConstraint(
                name: "ck_field_operations_type",
                table: "field_operations",
                sql: "operation_type = 'FERTILIZATION'");
        }
    }
}
