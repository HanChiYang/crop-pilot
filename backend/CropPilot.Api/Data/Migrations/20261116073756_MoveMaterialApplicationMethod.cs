using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CropPilot.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class MoveMaterialApplicationMethod : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_material_applications_water",
                table: "material_applications");

            migrationBuilder.DropCheckConstraint(
                name: "ck_material_application_details_method",
                table: "material_application_details");

            migrationBuilder.AddColumn<string>(
                name: "application_method",
                table: "material_applications",
                type: "character varying(24)",
                maxLength: 24,
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE material_applications AS application
                SET application_method = methods.application_method
                FROM (
                    SELECT material_application_id, MIN(application_method) AS application_method
                    FROM material_application_details
                    GROUP BY material_application_id
                    HAVING COUNT(DISTINCT application_method) = 1
                ) AS methods
                WHERE application.id = methods.material_application_id;

                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM material_applications WHERE application_method IS NULL) THEN
                        RAISE EXCEPTION 'Existing material applications must have exactly one application method';
                    END IF;
                END $$;
                """);

            migrationBuilder.AlterColumn<string>(
                name: "application_method",
                table: "material_applications",
                type: "character varying(24)",
                maxLength: 24,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(24)",
                oldMaxLength: 24,
                oldNullable: true);

            migrationBuilder.DropColumn(
                name: "application_method",
                table: "material_application_details");

            migrationBuilder.AddCheckConstraint(
                name: "ck_material_applications_method",
                table: "material_applications",
                sql: "application_method IN ('spray', 'broadcast')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_material_applications_water",
                table: "material_applications",
                sql: "spray_water_volume_liters IS NULL OR (application_method = 'spray' AND spray_water_volume_liters BETWEEN 0.000001 AND 999999999.999999)");
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

            migrationBuilder.AddColumn<string>(
                name: "application_method",
                table: "material_application_details",
                type: "character varying(24)",
                maxLength: 24,
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE material_application_details AS detail
                SET application_method = application.application_method
                FROM material_applications AS application
                WHERE detail.material_application_id = application.id;
                """);

            migrationBuilder.AlterColumn<string>(
                name: "application_method",
                table: "material_application_details",
                type: "character varying(24)",
                maxLength: 24,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(24)",
                oldMaxLength: 24,
                oldNullable: true);

            migrationBuilder.DropColumn(
                name: "application_method",
                table: "material_applications");

            migrationBuilder.AddCheckConstraint(
                name: "ck_material_applications_water",
                table: "material_applications",
                sql: "spray_water_volume_liters BETWEEN 0.000001 AND 999999999.999999");

            migrationBuilder.AddCheckConstraint(
                name: "ck_material_application_details_method",
                table: "material_application_details",
                sql: "application_method IN ('spray', 'broadcast')");
        }
    }
}
