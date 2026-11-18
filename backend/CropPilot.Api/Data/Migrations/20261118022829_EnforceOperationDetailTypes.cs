using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CropPilot.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class EnforceOperationDetailTypes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_irrigation_details_field_operations_field_operation_id",
                table: "irrigation_details");

            migrationBuilder.DropForeignKey(
                name: "fk_material_applications_field_operations_field_operation_id",
                table: "material_applications");

            migrationBuilder.DropIndex(
                name: "ix_material_applications_field_operation_id",
                table: "material_applications");

            migrationBuilder.DropIndex(
                name: "ix_irrigation_details_field_operation_id",
                table: "irrigation_details");

            migrationBuilder.AddColumn<string>(
                name: "operation_type",
                table: "material_applications",
                type: "character varying(24)",
                maxLength: 24,
                nullable: false,
                defaultValue: "MATERIAL_APPLICATION");

            migrationBuilder.AddColumn<string>(
                name: "operation_type",
                table: "irrigation_details",
                type: "character varying(24)",
                maxLength: 24,
                nullable: false,
                defaultValue: "IRRIGATION");

            // 預設值只用來回填既有資料，之後由應用程式寫入固定值。
            migrationBuilder.Sql("""
                ALTER TABLE material_applications ALTER COLUMN operation_type DROP DEFAULT;
                ALTER TABLE irrigation_details ALTER COLUMN operation_type DROP DEFAULT;
                """);

            migrationBuilder.AddUniqueConstraint(
                name: "ak_field_operations_id_operation_type",
                table: "field_operations",
                columns: new[] { "id", "operation_type" });

            migrationBuilder.CreateIndex(
                name: "ix_material_applications_field_operation_id_operation_type",
                table: "material_applications",
                columns: new[] { "field_operation_id", "operation_type" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_material_applications_operation_type",
                table: "material_applications",
                sql: "operation_type = 'MATERIAL_APPLICATION'");

            migrationBuilder.CreateIndex(
                name: "ix_irrigation_details_field_operation_id_operation_type",
                table: "irrigation_details",
                columns: new[] { "field_operation_id", "operation_type" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_irrigation_details_operation_type",
                table: "irrigation_details",
                sql: "operation_type = 'IRRIGATION'");

            migrationBuilder.AddForeignKey(
                name: "fk_irrigation_details_field_operations",
                table: "irrigation_details",
                columns: new[] { "field_operation_id", "operation_type" },
                principalTable: "field_operations",
                principalColumns: new[] { "id", "operation_type" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_material_applications_field_operations",
                table: "material_applications",
                columns: new[] { "field_operation_id", "operation_type" },
                principalTable: "field_operations",
                principalColumns: new[] { "id", "operation_type" },
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_irrigation_details_field_operations",
                table: "irrigation_details");

            migrationBuilder.DropForeignKey(
                name: "fk_material_applications_field_operations",
                table: "material_applications");

            migrationBuilder.DropIndex(
                name: "ix_material_applications_field_operation_id_operation_type",
                table: "material_applications");

            migrationBuilder.DropCheckConstraint(
                name: "ck_material_applications_operation_type",
                table: "material_applications");

            migrationBuilder.DropIndex(
                name: "ix_irrigation_details_field_operation_id_operation_type",
                table: "irrigation_details");

            migrationBuilder.DropCheckConstraint(
                name: "ck_irrigation_details_operation_type",
                table: "irrigation_details");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_field_operations_id_operation_type",
                table: "field_operations");

            migrationBuilder.DropColumn(
                name: "operation_type",
                table: "material_applications");

            migrationBuilder.DropColumn(
                name: "operation_type",
                table: "irrigation_details");

            migrationBuilder.CreateIndex(
                name: "ix_material_applications_field_operation_id",
                table: "material_applications",
                column: "field_operation_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_irrigation_details_field_operation_id",
                table: "irrigation_details",
                column: "field_operation_id",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_irrigation_details_field_operations_field_operation_id",
                table: "irrigation_details",
                column: "field_operation_id",
                principalTable: "field_operations",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_material_applications_field_operations_field_operation_id",
                table: "material_applications",
                column: "field_operation_id",
                principalTable: "field_operations",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
