using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheCuratool.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBorrowedAbility : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsStAssigned",
                table: "PlayerSlots",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "BorrowedAbilityCharacterId",
                table: "PlayerSlots",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsStAssigned",
                table: "PlayerSlots");

            migrationBuilder.DropColumn(
                name: "BorrowedAbilityCharacterId",
                table: "PlayerSlots");
        }
    }
}
