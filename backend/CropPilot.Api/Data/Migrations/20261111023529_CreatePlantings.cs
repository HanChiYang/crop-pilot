using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CropPilot.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class CreatePlantings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "plantings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    field_id = table.Column<Guid>(type: "uuid", nullable: false),
                    crop_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    variety = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    planned_planting_date = table.Column<DateOnly>(type: "date", nullable: true),
                    actual_planting_date = table.Column<DateOnly>(type: "date", nullable: true),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false, defaultValue: "PLANNED"),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_plantings", x => x.id);
                    table.CheckConstraint("ck_plantings_actual_planting_date", "status NOT IN ('GROWING', 'COMPLETED') OR actual_planting_date IS NOT NULL");
                    table.CheckConstraint("ck_plantings_crop_name", "btrim(crop_name, U&'\\0009\\000A\\000B\\000C\\000D\\0020\\0085\\00A0\\1680\\2000\\2001\\2002\\2003\\2004\\2005\\2006\\2007\\2008\\2009\\200A\\2028\\2029\\202F\\205F\\3000') <> ''");
                    table.CheckConstraint("ck_plantings_status", "status IN ('PLANNED', 'GROWING', 'HARVESTING', 'COMPLETED')");
                    table.ForeignKey(
                        name: "fk_plantings_fields_field_id",
                        column: x => x.field_id,
                        principalTable: "fields",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_plantings_field_id",
                table: "plantings",
                column: "field_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "plantings");
        }
    }
}
