using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CropPilot.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class StandardizeAccountKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "pk_asp_net_user_tokens",
                table: "asp_net_user_tokens");

            migrationBuilder.DropPrimaryKey(
                name: "pk_asp_net_user_logins",
                table: "asp_net_user_logins");

            migrationBuilder.RenameTable(
                name: "asp_net_users",
                newName: "main_account");

            migrationBuilder.RenameTable(
                name: "asp_net_user_logins",
                newName: "user_external_logins");

            migrationBuilder.Sql("""
                ALTER TABLE main_account RENAME CONSTRAINT pk_asp_net_users TO pk_main_account;
                ALTER TABLE asp_net_user_claims RENAME CONSTRAINT fk_asp_net_user_claims_asp_net_users_user_id TO fk_asp_net_user_claims_main_account_user_id;
                ALTER TABLE asp_net_user_tokens RENAME CONSTRAINT fk_asp_net_user_tokens_asp_net_users_user_id TO fk_asp_net_user_tokens_main_account_user_id;
                ALTER TABLE user_external_logins RENAME CONSTRAINT fk_asp_net_user_logins_asp_net_users_user_id TO fk_user_external_logins_main_account_user_id;
                """);

            migrationBuilder.RenameIndex(
                name: "ux_asp_net_users_normalized_user_name",
                table: "main_account",
                newName: "ux_main_account_normalized_user_name");

            migrationBuilder.RenameIndex(
                name: "ux_asp_net_users_normalized_email",
                table: "main_account",
                newName: "ux_main_account_normalized_email");

            migrationBuilder.RenameIndex(
                name: "ix_asp_net_user_logins_user_id",
                table: "user_external_logins",
                newName: "ix_user_external_logins_user_id");

            migrationBuilder.AddColumn<long>(
                name: "id",
                table: "asp_net_user_tokens",
                type: "bigint",
                nullable: false)
                .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

            migrationBuilder.AddColumn<long>(
                name: "id",
                table: "user_external_logins",
                type: "bigint",
                nullable: false)
                .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

            migrationBuilder.AddPrimaryKey(
                name: "pk_asp_net_user_tokens",
                table: "asp_net_user_tokens",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_user_external_logins",
                table: "user_external_logins",
                column: "id");

            migrationBuilder.CreateIndex(
                name: "ux_asp_net_user_tokens_user_provider_name",
                table: "asp_net_user_tokens",
                columns: new[] { "user_id", "login_provider", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_user_external_logins_provider_key",
                table: "user_external_logins",
                columns: new[] { "login_provider", "provider_key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "pk_asp_net_user_tokens",
                table: "asp_net_user_tokens");

            migrationBuilder.DropIndex(
                name: "ux_asp_net_user_tokens_user_provider_name",
                table: "asp_net_user_tokens");

            migrationBuilder.DropPrimaryKey(
                name: "pk_user_external_logins",
                table: "user_external_logins");

            migrationBuilder.DropIndex(
                name: "ux_user_external_logins_provider_key",
                table: "user_external_logins");

            migrationBuilder.DropColumn(
                name: "id",
                table: "asp_net_user_tokens");

            migrationBuilder.DropColumn(
                name: "id",
                table: "user_external_logins");

            migrationBuilder.Sql("""
                ALTER TABLE main_account RENAME CONSTRAINT pk_main_account TO pk_asp_net_users;
                ALTER TABLE asp_net_user_claims RENAME CONSTRAINT fk_asp_net_user_claims_main_account_user_id TO fk_asp_net_user_claims_asp_net_users_user_id;
                ALTER TABLE asp_net_user_tokens RENAME CONSTRAINT fk_asp_net_user_tokens_main_account_user_id TO fk_asp_net_user_tokens_asp_net_users_user_id;
                ALTER TABLE user_external_logins RENAME CONSTRAINT fk_user_external_logins_main_account_user_id TO fk_asp_net_user_logins_asp_net_users_user_id;
                """);

            migrationBuilder.RenameTable(
                name: "user_external_logins",
                newName: "asp_net_user_logins");

            migrationBuilder.RenameTable(
                name: "main_account",
                newName: "asp_net_users");

            migrationBuilder.RenameIndex(
                name: "ix_user_external_logins_user_id",
                table: "asp_net_user_logins",
                newName: "ix_asp_net_user_logins_user_id");

            migrationBuilder.RenameIndex(
                name: "ux_main_account_normalized_user_name",
                table: "asp_net_users",
                newName: "ux_asp_net_users_normalized_user_name");

            migrationBuilder.RenameIndex(
                name: "ux_main_account_normalized_email",
                table: "asp_net_users",
                newName: "ux_asp_net_users_normalized_email");

            migrationBuilder.AddPrimaryKey(
                name: "pk_asp_net_user_tokens",
                table: "asp_net_user_tokens",
                columns: new[] { "user_id", "login_provider", "name" });

            migrationBuilder.AddPrimaryKey(
                name: "pk_asp_net_user_logins",
                table: "asp_net_user_logins",
                columns: new[] { "login_provider", "provider_key" });
        }
    }
}
