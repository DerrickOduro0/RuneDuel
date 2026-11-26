using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RuneReign.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddFriendChallenges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Mode",
                table: "MatchQueue",
                type: "varchar(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "ranked")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "Mode",
                table: "CombatMatches",
                type: "varchar(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "ranked")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<bool>(
                name: "RematchA",
                table: "CombatMatches",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "RematchB",
                table: "CombatMatches",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "RematchMatchId",
                table: "CombatMatches",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.CreateTable(
                name: "FriendChallenges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Code = table.Column<string>(type: "varchar(12)", maxLength: 12, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    HostId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    CombatMatchId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FriendChallenges", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_FriendChallenges_Code",
                table: "FriendChallenges",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FriendChallenges_HostId",
                table: "FriendChallenges",
                column: "HostId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FriendChallenges");

            migrationBuilder.DropColumn(
                name: "Mode",
                table: "MatchQueue");

            migrationBuilder.DropColumn(
                name: "Mode",
                table: "CombatMatches");

            migrationBuilder.DropColumn(
                name: "RematchA",
                table: "CombatMatches");

            migrationBuilder.DropColumn(
                name: "RematchB",
                table: "CombatMatches");

            migrationBuilder.DropColumn(
                name: "RematchMatchId",
                table: "CombatMatches");
        }
    }
}
