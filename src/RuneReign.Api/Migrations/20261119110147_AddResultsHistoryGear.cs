using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RuneReign.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddResultsHistoryGear : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "TutorialDone",
                table: "Players",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "Edge",
                table: "Items",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Insight",
                table: "Items",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Pierce",
                table: "Items",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Rarity",
                table: "Items",
                type: "varchar(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "Resolve",
                table: "Items",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "CoinsA",
                table: "CombatMatches",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "CoinsB",
                table: "CombatMatches",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "LeagueChangeA",
                table: "CombatMatches",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "LeagueChangeB",
                table: "CombatMatches",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "PointsDeltaA",
                table: "CombatMatches",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "PointsDeltaB",
                table: "CombatMatches",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "RatingDeltaA",
                table: "CombatMatches",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "RatingDeltaB",
                table: "CombatMatches",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "CombatRounds",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    MatchId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Turn = table.Column<int>(type: "int", nullable: false),
                    RuneA = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    FormA = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    StateA = table.Column<string>(type: "varchar(8)", maxLength: 8, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    RuneB = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    FormB = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    StateB = table.Column<string>(type: "varchar(8)", maxLength: 8, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    HpA = table.Column<int>(type: "int", nullable: false),
                    HpB = table.Column<int>(type: "int", nullable: false),
                    Log = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CombatRounds", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_CombatMatches_PlayerAId",
                table: "CombatMatches",
                column: "PlayerAId");

            migrationBuilder.CreateIndex(
                name: "IX_CombatMatches_PlayerBId",
                table: "CombatMatches",
                column: "PlayerBId");

            migrationBuilder.CreateIndex(
                name: "IX_CombatRounds_MatchId_Turn",
                table: "CombatRounds",
                columns: new[] { "MatchId", "Turn" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CombatRounds");

            migrationBuilder.DropIndex(
                name: "IX_CombatMatches_PlayerAId",
                table: "CombatMatches");

            migrationBuilder.DropIndex(
                name: "IX_CombatMatches_PlayerBId",
                table: "CombatMatches");

            migrationBuilder.DropColumn(
                name: "TutorialDone",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "Edge",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "Insight",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "Pierce",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "Rarity",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "Resolve",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "CoinsA",
                table: "CombatMatches");

            migrationBuilder.DropColumn(
                name: "CoinsB",
                table: "CombatMatches");

            migrationBuilder.DropColumn(
                name: "LeagueChangeA",
                table: "CombatMatches");

            migrationBuilder.DropColumn(
                name: "LeagueChangeB",
                table: "CombatMatches");

            migrationBuilder.DropColumn(
                name: "PointsDeltaA",
                table: "CombatMatches");

            migrationBuilder.DropColumn(
                name: "PointsDeltaB",
                table: "CombatMatches");

            migrationBuilder.DropColumn(
                name: "RatingDeltaA",
                table: "CombatMatches");

            migrationBuilder.DropColumn(
                name: "RatingDeltaB",
                table: "CombatMatches");
        }
    }
}
