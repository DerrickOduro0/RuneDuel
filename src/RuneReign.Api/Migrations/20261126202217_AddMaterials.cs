using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RuneReign.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddMaterials : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Dust",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "Shards",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "Dust",
                table: "ForgeRecipes");

            migrationBuilder.DropColumn(
                name: "Shards",
                table: "ForgeRecipes");

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

            migrationBuilder.AddColumn<string>(
                name: "Ingredients",
                table: "ForgeRecipes",
                type: "character varying(400)",
                maxLength: 400,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "DropsA",
                table: "CombatMatches",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "DropsB",
                table: "CombatMatches",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "Materials",
                columns: table => new
                {
                    Key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Icon = table.Column<string>(type: "text", nullable: false),
                    Rarity = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    Sources = table.Column<string>(type: "text", nullable: false),
                    Hint = table.Column<string>(type: "text", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Materials", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "PlayerMaterials",
                columns: table => new
                {
                    PlayerId = table.Column<Guid>(type: "uuid", nullable: false),
                    MaterialKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerMaterials", x => new { x.PlayerId, x.MaterialKey });
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Materials");

            migrationBuilder.DropTable(
                name: "PlayerMaterials");

            migrationBuilder.DropColumn(
                name: "Ingredients",
                table: "ForgeRecipes");

            migrationBuilder.DropColumn(
                name: "DropsA",
                table: "CombatMatches");

            migrationBuilder.DropColumn(
                name: "DropsB",
                table: "CombatMatches");

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
                name: "Dust",
                table: "ForgeRecipes",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Shards",
                table: "ForgeRecipes",
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
        }
    }
}
