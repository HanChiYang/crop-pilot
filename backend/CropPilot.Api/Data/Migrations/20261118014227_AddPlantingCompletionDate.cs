using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CropPilot.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPlantingCompletionDate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "completion_date",
                table: "plantings",
                type: "date",
                nullable: true);

            // 既有已結束紀錄沒有結束日，以實際栽種日、最後採收日及最後作業日中最晚的一天回填。
            migrationBuilder.Sql("""
                UPDATE plantings AS planting
                SET completion_date = GREATEST(
                    planting.actual_planting_date,
                    (SELECT max(harvest.harvest_date) FROM harvests AS harvest WHERE harvest.planting_id = planting.id),
                    (SELECT max(operation.operation_date) FROM field_operations AS operation
                     WHERE operation.planting_id = planting.id))
                WHERE planting.status = 'COMPLETED';
                """);

            migrationBuilder.AddCheckConstraint(
                name: "ck_plantings_completion_date",
                table: "plantings",
                sql: "(status = 'COMPLETED') = (completion_date IS NOT NULL) AND completion_date >= actual_planting_date");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_plantings_completion_date",
                table: "plantings");

            migrationBuilder.DropColumn(
                name: "completion_date",
                table: "plantings");
        }
    }
}
