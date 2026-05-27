using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheCuratool.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Scripts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Author = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    RawJson = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP AT TIME ZONE 'UTC'")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Scripts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GameSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ScriptId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlayerCount = table.Column<int>(type: "integer", nullable: false),
                    ActiveLorics = table.Column<string>(type: "text", nullable: false, defaultValue: "[]"),
                    UseMarionette = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    IsLegionGame = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    LegionCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    Status = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP AT TIME ZONE 'UTC'")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GameSessions_Scripts_ScriptId",
                        column: x => x.ScriptId,
                        principalTable: "Scripts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PlayerSlots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    DraftOrder = table.Column<int>(type: "integer", nullable: false),
                    PlayerId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChosenCharacterId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    OfferedCharacterIds = table.Column<string>(type: "text", nullable: false, defaultValue: "[]"),
                    HiddenFlags = table.Column<string>(type: "text", nullable: false, defaultValue: "{\"isDrunk\":false,\"isLunatic\":false}"),
                    IsAtheistCommitmentConfirmed = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerSlots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlayerSlots_GameSessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "GameSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GameSessions_ScriptId",
                table: "GameSessions",
                column: "ScriptId");

            migrationBuilder.CreateIndex(
                name: "IX_PlayerSlots_SessionId",
                table: "PlayerSlots",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_PlayerSlots_SessionId_DraftOrder",
                table: "PlayerSlots",
                columns: new[] { "SessionId", "DraftOrder" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlayerSlots");

            migrationBuilder.DropTable(
                name: "GameSessions");

            migrationBuilder.DropTable(
                name: "Scripts");
        }
    }
}
