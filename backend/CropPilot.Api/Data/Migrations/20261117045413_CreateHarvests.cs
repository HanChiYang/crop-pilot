using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CropPilot.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class CreateHarvests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "harvests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    planting_id = table.Column<Guid>(type: "uuid", nullable: false),
                    harvest_date = table.Column<DateOnly>(type: "date", nullable: false),
                    weight_kg = table.Column<decimal>(type: "numeric(15,6)", precision: 15, scale: 6, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_harvests", x => x.id);
                    table.CheckConstraint("ck_harvests_weight_kg", "weight_kg > 0 AND weight_kg < 1000000000");
                    table.ForeignKey(
                        name: "fk_harvests_plantings_planting_id",
                        column: x => x.planting_id,
                        principalTable: "plantings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_harvests_planting_id_harvest_date_created_at_id",
                table: "harvests",
                columns: new[] { "planting_id", "harvest_date", "created_at", "id" },
                descending: new[] { false, true, true, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "harvests");
        }
    }
}
