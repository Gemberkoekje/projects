using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheCuratool.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSpecialCaseFields3 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // All ADD COLUMN statements use IF NOT EXISTS so this migration is safe
            // on both fresh databases (which already have these columns via InitialCreate
            // or AddBorrowedAbility) and the live deployed database (which is missing
            // some or all of them due to migrations being recorded before they were complete).
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
                    ADD COLUMN IF NOT EXISTS "IsStAssigned" boolean NOT NULL DEFAULT false,
                    ADD COLUMN IF NOT EXISTS "BorrowedAbilityCharacterId" character varying(128) NOT NULL DEFAULT '',
                    ADD COLUMN IF NOT EXISTS "IsAtheistCommitmentConfirmed" boolean NOT NULL DEFAULT false;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "UseMarionette", table: "GameSessions");
            migrationBuilder.DropColumn(name: "IsLegionGame", table: "GameSessions");
            migrationBuilder.DropColumn(name: "LegionCount", table: "GameSessions");
            migrationBuilder.DropColumn(name: "IsStAssigned", table: "PlayerSlots");
            migrationBuilder.DropColumn(name: "BorrowedAbilityCharacterId", table: "PlayerSlots");
            migrationBuilder.DropColumn(name: "IsAtheistCommitmentConfirmed", table: "PlayerSlots");
        }
    }
}
