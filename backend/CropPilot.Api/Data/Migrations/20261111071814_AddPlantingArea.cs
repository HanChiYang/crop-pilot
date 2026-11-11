using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CropPilot.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPlantingArea : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "area_square_meters",
                table: "plantings",
                type: "numeric(14,4)",
                precision: 14,
                scale: 4,
                nullable: false);

            migrationBuilder.AddCheckConstraint(
                name: "ck_plantings_area_square_meters",
                table: "plantings",
                sql: "area_square_meters > 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_plantings_area_square_meters",
                table: "plantings");

            migrationBuilder.DropColumn(
                name: "area_square_meters",
                table: "plantings");
        }
    }
}
