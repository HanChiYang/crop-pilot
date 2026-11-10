using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CropPilot.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class DesktopAccountModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "security_stamp",
                table: "main_account",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "status",
                table: "main_account",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "PENDING");

            migrationBuilder.CreateTable(
                name: "federated_identities",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    main_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    identity_provider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    provider_user_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_federated_identities", x => x.id);
                    table.CheckConstraint("ck_federated_identities_provider", "identity_provider = 'GOOGLE'");
                    table.ForeignKey(
                        name: "fk_federated_identities_main_accounts_main_account_id",
                        column: x => x.main_account_id,
                        principalTable: "main_account",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "local_credentials",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    main_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    username = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    password_hash = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    access_failed_count = table.Column<int>(type: "integer", nullable: false),
                    lockout_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    lockout_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    concurrency_stamp = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_local_credentials", x => x.id);
                    table.CheckConstraint("ck_local_credentials_username", "length(username) > 0");
                    table.ForeignKey(
                        name: "fk_local_credentials_main_accounts_main_account_id",
                        column: x => x.main_account_id,
                        principalTable: "main_account",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_profiles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    main_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    display_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    surname = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    given_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_profiles", x => x.id);
                    table.ForeignKey(
                        name: "fk_user_profiles_main_accounts_main_account_id",
                        column: x => x.main_account_id,
                        principalTable: "main_account",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_roles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    main_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_roles", x => x.id);
                    table.CheckConstraint("ck_user_roles_role", "role IN ('USER', 'ADMIN')");
                    table.ForeignKey(
                        name: "fk_user_roles_main_account_main_account_id",
                        column: x => x.main_account_id,
                        principalTable: "main_account",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_main_account_status",
                table: "main_account",
                sql: "status IN ('PENDING', 'ACTIVE', 'DISABLED')");

            migrationBuilder.CreateIndex(
                name: "ix_federated_identities_identity_provider_provider_user_id",
                table: "federated_identities",
                columns: new[] { "identity_provider", "provider_user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_federated_identities_main_account_id",
                table: "federated_identities",
                column: "main_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_local_credentials_main_account_id",
                table: "local_credentials",
                column: "main_account_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_user_profiles_email",
                table: "user_profiles",
                column: "email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_user_profiles_main_account_id",
                table: "user_profiles",
                column: "main_account_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_user_roles_main_account_id_role",
                table: "user_roles",
                columns: new[] { "main_account_id", "role" },
                unique: true);

            // Copy account data while the old tables and columns are still present.
            migrationBuilder.Sql("""
                DO $migration$
                BEGIN
                    IF EXISTS (SELECT 1 FROM asp_net_user_claims)
                        OR EXISTS (SELECT 1 FROM asp_net_user_tokens)
                        OR EXISTS (SELECT 1 FROM main_account
                            WHERE two_factor_enabled OR phone_number IS NOT NULL OR phone_number_confirmed)
                        OR EXISTS (SELECT 1 FROM user_external_logins
                            WHERE login_provider <> 'Google' OR length(provider_key) NOT BETWEEN 1 AND 255)
                    THEN
                        RAISE EXCEPTION 'Existing Identity data requires a separate migration review.';
                    END IF;
                END
                $migration$;

                UPDATE main_account SET status = 'ACTIVE',
                    security_stamp = COALESCE(NULLIF(security_stamp, ''), replace(gen_random_uuid()::text, '-', ''));

                INSERT INTO user_profiles (id, main_account_id, email)
                SELECT gen_random_uuid(), id, lower(NULLIF(btrim(email), ''))
                FROM main_account;

                INSERT INTO user_roles (id, main_account_id, role)
                SELECT gen_random_uuid(), id, 'USER' FROM main_account;

                INSERT INTO local_credentials
                    (id, main_account_id, username, password_hash, access_failed_count,
                     lockout_enabled, lockout_end, concurrency_stamp)
                SELECT gen_random_uuid(), id, btrim(user_name), password_hash, access_failed_count,
                       lockout_enabled, lockout_end, gen_random_uuid()
                FROM main_account
                WHERE password_hash IS NOT NULL;

                INSERT INTO federated_identities (id, main_account_id, identity_provider, provider_user_id)
                SELECT gen_random_uuid(), user_id, 'GOOGLE', provider_key FROM user_external_logins;

                CREATE UNIQUE INDEX ux_local_credentials_lower_username
                    ON local_credentials (lower(username));
                """);


            migrationBuilder.DropTable(
                name: "asp_net_user_claims");

            migrationBuilder.DropTable(
                name: "asp_net_user_tokens");

            migrationBuilder.DropTable(
                name: "user_external_logins");

            migrationBuilder.DropIndex(
                name: "ux_main_account_normalized_email",
                table: "main_account");

            migrationBuilder.DropIndex(
                name: "ux_main_account_normalized_user_name",
                table: "main_account");

            migrationBuilder.DropColumn(
                name: "access_failed_count",
                table: "main_account");

            migrationBuilder.DropColumn(
                name: "concurrency_stamp",
                table: "main_account");

            migrationBuilder.DropColumn(
                name: "email",
                table: "main_account");

            migrationBuilder.DropColumn(
                name: "email_confirmed",
                table: "main_account");

            migrationBuilder.DropColumn(
                name: "lockout_enabled",
                table: "main_account");

            migrationBuilder.DropColumn(
                name: "lockout_end",
                table: "main_account");

            migrationBuilder.DropColumn(
                name: "normalized_email",
                table: "main_account");

            migrationBuilder.DropColumn(
                name: "normalized_user_name",
                table: "main_account");

            migrationBuilder.DropColumn(
                name: "password_hash",
                table: "main_account");

            migrationBuilder.DropColumn(
                name: "phone_number",
                table: "main_account");

            migrationBuilder.DropColumn(
                name: "phone_number_confirmed",
                table: "main_account");

            migrationBuilder.DropColumn(
                name: "two_factor_enabled",
                table: "main_account");

            migrationBuilder.DropColumn(
                name: "user_name",
                table: "main_account");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) =>
            throw new NotSupportedException("Restore a database backup to revert the account model migration.");
    }
}
