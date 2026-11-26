using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RuneReign.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddProgressionProfile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AvatarFile",
                table: "Players",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BestLeague",
                table: "Players",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "BestWinStreak",
                table: "Players",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Motto",
                table: "Players",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "WinStreak",
                table: "Players",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Xp",
                table: "Players",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "LevelUpA",
                table: "CombatMatches",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "LevelUpB",
                table: "CombatMatches",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "XpA",
                table: "CombatMatches",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "XpB",
                table: "CombatMatches",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        
            // Players who existed before levels: start at their current league as their best, and credit XP for duels already played
            // (same amounts as the default settings). No level-up coins are paid for this catch-up.
            migrationBuilder.Sql("UPDATE \"Players\" SET \"BestLeague\" = \"League\";");
            migrationBuilder.Sql(@"
UPDATE ""Players"" p SET ""Xp"" = (CASE WHEN p.""TutorialDone"" THEN 50 ELSE 0 END) + COALESCE((
  SELECT SUM(CASE
    WHEN m.""ShadeDifficulty"" = 'hard' THEN (CASE WHEN m.""WinnerId"" = p.""Id"" THEN 20 ELSE 0 END)
    WHEN m.""ShadeDifficulty"" = 'easy' THEN (CASE WHEN m.""WinnerId"" = p.""Id"" THEN 8 ELSE 0 END)
    WHEN m.""ShadeDifficulty"" IS NOT NULL THEN 0
    WHEN m.""Mode"" = 'friend' THEN 10
    WHEN m.""Mode"" = 'casual' THEN (CASE WHEN m.""WinnerId"" = p.""Id"" THEN 35 WHEN m.""IsDraw"" THEN 25 ELSE 15 END)
    ELSE (CASE WHEN m.""WinnerId"" = p.""Id"" THEN 50 WHEN m.""IsDraw"" THEN 35 ELSE 20 END)
  END)
  FROM ""CombatMatches"" m
  WHERE m.""CompletedAtUtc"" IS NOT NULL AND (m.""PlayerAId"" = p.""Id"" OR m.""PlayerBId"" = p.""Id"")), 0);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AvatarFile",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "BestLeague",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "BestWinStreak",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "Motto",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "WinStreak",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "Xp",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "LevelUpA",
                table: "CombatMatches");

            migrationBuilder.DropColumn(
                name: "LevelUpB",
                table: "CombatMatches");

            migrationBuilder.DropColumn(
                name: "XpA",
                table: "CombatMatches");

            migrationBuilder.DropColumn(
                name: "XpB",
                table: "CombatMatches");
        }
    }
}
