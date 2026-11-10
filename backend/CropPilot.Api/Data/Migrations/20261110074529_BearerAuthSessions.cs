using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CropPilot.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class BearerAuthSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "auth_sessions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    main_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    security_stamp = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_auth_sessions", x => x.id);
                    table.ForeignKey(
                        name: "fk_auth_sessions_main_accounts_main_account_id",
                        column: x => x.main_account_id,
                        principalTable: "main_account",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_auth_sessions_main_account_id",
                table: "auth_sessions",
                column: "main_account_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "auth_sessions");
        }
    }
}
