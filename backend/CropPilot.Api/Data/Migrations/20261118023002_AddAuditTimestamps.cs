using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CropPilot.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAuditTimestamps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "created_at",
                table: "plantings",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "CURRENT_TIMESTAMP");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "updated_at",
                table: "plantings",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "CURRENT_TIMESTAMP");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "updated_at",
                table: "material_purchase_lots",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "CURRENT_TIMESTAMP");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "created_at",
                table: "fields",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "CURRENT_TIMESTAMP");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "updated_at",
                table: "fields",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "CURRENT_TIMESTAMP");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "updated_at",
                table: "agricultural_materials",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "CURRENT_TIMESTAMP");

            // 已有建立時間的資料表，以建立時間回填，表示升版前沒有已知的修改紀錄。
            migrationBuilder.Sql("""
                UPDATE agricultural_materials SET updated_at = created_at;
                UPDATE material_purchase_lots SET updated_at = created_at;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "created_at",
                table: "plantings");

            migrationBuilder.DropColumn(
                name: "updated_at",
                table: "plantings");

            migrationBuilder.DropColumn(
                name: "updated_at",
                table: "material_purchase_lots");

            migrationBuilder.DropColumn(
                name: "created_at",
                table: "fields");

            migrationBuilder.DropColumn(
                name: "updated_at",
                table: "fields");

            migrationBuilder.DropColumn(
                name: "updated_at",
                table: "agricultural_materials");
        }
    }
}
