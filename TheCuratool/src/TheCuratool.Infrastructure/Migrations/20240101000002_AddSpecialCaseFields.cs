using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheCuratool.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSpecialCaseFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Use IF NOT EXISTS because InitialCreate was modified in-place to include
            // these columns, so fresh databases already have them while existing
            // deployed databases do not.
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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "UseMarionette",
                table: "GameSessions");

            migrationBuilder.DropColumn(
                name: "IsLegionGame",
                table: "GameSessions");

            migrationBuilder.DropColumn(
                name: "LegionCount",
                table: "GameSessions");

            migrationBuilder.DropColumn(
                name: "IsAtheistCommitmentConfirmed",
                table: "PlayerSlots");
        }
    }
}
