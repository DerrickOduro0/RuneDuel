using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RuneReign.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddForge : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Dust",
                table: "Players",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Shards",
                table: "Players",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "DustA",
                table: "CombatMatches",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "DustB",
                table: "CombatMatches",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ShardsA",
                table: "CombatMatches",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ShardsB",
                table: "CombatMatches",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "ForgeRecipes",
                columns: table => new
                {
                    Key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ResultType = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ResultKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Dust = table.Column<int>(type: "integer", nullable: false),
                    Shards = table.Column<int>(type: "integer", nullable: false),
                    Coins = table.Column<int>(type: "integer", nullable: false),
                    MinLevel = table.Column<int>(type: "integer", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ForgeRecipes", x => x.Key);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ForgeRecipes");

            migrationBuilder.DropColumn(
                name: "Dust",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "Shards",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "DustA",
                table: "CombatMatches");

            migrationBuilder.DropColumn(
                name: "DustB",
                table: "CombatMatches");

            migrationBuilder.DropColumn(
                name: "ShardsA",
                table: "CombatMatches");

            migrationBuilder.DropColumn(
                name: "ShardsB",
                table: "CombatMatches");
        }
    }
}
