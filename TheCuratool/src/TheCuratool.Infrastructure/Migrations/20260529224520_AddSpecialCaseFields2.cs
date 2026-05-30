using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheCuratool.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSpecialCaseFields2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Use IF EXISTS because this index may not exist on databases that were
            // created before the index was introduced, or on fresh databases where
            // InitialCreate already omitted it.
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_PlayerSlots_SessionId"";");

            // These columns were added to the EF model without a migration being generated.
            // InitialCreate was retroactively edited so fresh databases already have them;
            // existing deployed databases do not. IF NOT EXISTS makes this safe for both.
            migrationBuilder.Sql(
                """
                ALTER TABLE "GameSessions"
                    ADD COLUMN IF NOT EXISTS "UseMarionette" boolean NOT NULL DEFAULT false,
                    ADD COLUMN IF NOT EXISTS "IsLegionGame" boolean NOT NULL DEFAULT false,
                    ADD COLUMN IF NOT EXISTS "LegionCount" integer NOT NULL DEFAULT 0;
                """);

            migrationBuilder.Sql(
                """
                ALTER TABLE "PlayerSlots"
                    ADD COLUMN IF NOT EXISTS "IsAtheistCommitmentConfirmed" boolean NOT NULL DEFAULT false;
                """);

            migrationBuilder.AlterColumn<string>(
                name: "ChosenCharacterId",
                table: "PlayerSlots",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldMaxLength: 128,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ActiveLorics",
                table: "GameSessions",
                type: "text",
                nullable: false,
                defaultValue: "[]",
                oldClrType: typeof(string),
                oldType: "text");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "ChosenCharacterId",
                table: "PlayerSlots",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldMaxLength: 128);

            migrationBuilder.AlterColumn<string>(
                name: "ActiveLorics",
                table: "GameSessions",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldDefaultValue: "[]");

            migrationBuilder.CreateIndex(
                name: "IX_PlayerSlots_SessionId",
                table: "PlayerSlots",
                column: "SessionId");
        }
    }
}
