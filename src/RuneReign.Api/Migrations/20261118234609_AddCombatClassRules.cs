using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RuneReign.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCombatClassRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PendingD8AdjustmentA",
                table: "CombatMatches",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "PendingD8AdjustmentB",
                table: "CombatMatches",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "UsedRunesA",
                table: "CombatMatches",
                type: "longtext",
                nullable: false)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "UsedRunesB",
                table: "CombatMatches",
                type: "longtext",
                nullable: false)
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PendingD8AdjustmentA",
                table: "CombatMatches");

            migrationBuilder.DropColumn(
                name: "PendingD8AdjustmentB",
                table: "CombatMatches");

            migrationBuilder.DropColumn(
                name: "UsedRunesA",
                table: "CombatMatches");

            migrationBuilder.DropColumn(
                name: "UsedRunesB",
                table: "CombatMatches");
        }
    }
}
