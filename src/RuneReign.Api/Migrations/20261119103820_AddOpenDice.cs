using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RuneReign.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddOpenDice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FormA",
                table: "CombatMatches",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "FormB",
                table: "CombatMatches",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "RuneA",
                table: "CombatMatches",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "RuneB",
                table: "CombatMatches",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "StatesA",
                table: "CombatMatches",
                type: "longtext",
                nullable: false)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "StatesB",
                table: "CombatMatches",
                type: "longtext",
                nullable: false)
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FormA",
                table: "CombatMatches");

            migrationBuilder.DropColumn(
                name: "FormB",
                table: "CombatMatches");

            migrationBuilder.DropColumn(
                name: "RuneA",
                table: "CombatMatches");

            migrationBuilder.DropColumn(
                name: "RuneB",
                table: "CombatMatches");

            migrationBuilder.DropColumn(
                name: "StatesA",
                table: "CombatMatches");

            migrationBuilder.DropColumn(
                name: "StatesB",
                table: "CombatMatches");
        }
    }
}
