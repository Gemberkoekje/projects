using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheCuratool.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUseAtheistOption : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "UseAtheist",
                table: "GameSessions",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "UseAtheist",
                table: "GameSessions");
        }
    }
}
