using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheCuratool.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSpecialCaseFields4 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The live database had AddSpecialCaseFields3 recorded in __EFMigrationsHistory
            // before the column SQL was present in that migration, so those columns were never
            // actually created. This migration adds them idempotently with IF NOT EXISTS.
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
                    ADD COLUMN IF NOT EXISTS "BorrowedAbilityCharacterId" character varying(128) NOT NULL DEFAULT '';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No-op: these columns are expected to exist in the model; removing them would
            // break the application. Down() is intentionally left empty.
        }
    }
}
