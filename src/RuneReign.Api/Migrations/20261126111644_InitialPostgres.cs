using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace RuneReign.Api.Migrations
{
    /// <inheritdoc />
    public partial class InitialPostgres : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ArenaModes",
                columns: table => new
                {
                    Key = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    Tag = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    Icon = table.Column<string>(type: "text", nullable: false),
                    IsLive = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ArenaModes", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "Attributes",
                columns: table => new
                {
                    Key = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Icon = table.Column<string>(type: "text", nullable: false),
                    Effect = table.Column<string>(type: "text", nullable: false),
                    Cap = table.Column<int>(type: "integer", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Attributes", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "ClassRunes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ClassName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Meaning = table.Column<string>(type: "text", nullable: false),
                    Symbol = table.Column<string>(type: "text", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClassRunes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CombatMatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PlayerAId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlayerBId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClassA = table.Column<string>(type: "text", nullable: false),
                    ClassB = table.Column<string>(type: "text", nullable: false),
                    HpA = table.Column<int>(type: "integer", nullable: false),
                    HpB = table.Column<int>(type: "integer", nullable: false),
                    ShieldA = table.Column<int>(type: "integer", nullable: false),
                    ShieldB = table.Column<int>(type: "integer", nullable: false),
                    ManaA = table.Column<int>(type: "integer", nullable: false),
                    ManaB = table.Column<int>(type: "integer", nullable: false),
                    Turn = table.Column<int>(type: "integer", nullable: false),
                    PendingA = table.Column<string>(type: "text", nullable: true),
                    PendingB = table.Column<string>(type: "text", nullable: true),
                    PendingD8AdjustmentA = table.Column<int>(type: "integer", nullable: false),
                    PendingD8AdjustmentB = table.Column<int>(type: "integer", nullable: false),
                    PendingRerollA = table.Column<bool>(type: "boolean", nullable: false),
                    PendingRerollB = table.Column<bool>(type: "boolean", nullable: false),
                    PendingFormA = table.Column<string>(type: "text", nullable: true),
                    PendingFormB = table.Column<string>(type: "text", nullable: true),
                    AcceptedA = table.Column<bool>(type: "boolean", nullable: false),
                    AcceptedB = table.Column<bool>(type: "boolean", nullable: false),
                    AcceptDeadlineUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    LastSeenA = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    LastSeenB = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    BonusesA = table.Column<string>(type: "text", nullable: false),
                    BonusesB = table.Column<string>(type: "text", nullable: false),
                    ShadeDifficulty = table.Column<string>(type: "text", nullable: true),
                    TalismanA = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    TalismanB = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    TalismanUsesA = table.Column<int>(type: "integer", nullable: false),
                    TalismanUsesB = table.Column<int>(type: "integer", nullable: false),
                    TalismanFlagsA = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TalismanFlagsB = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TalismanFiredA = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    TalismanFiredB = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Mode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    RematchA = table.Column<bool>(type: "boolean", nullable: false),
                    RematchB = table.Column<bool>(type: "boolean", nullable: false),
                    RematchMatchId = table.Column<Guid>(type: "uuid", nullable: true),
                    RuneA = table.Column<string>(type: "text", nullable: true),
                    FormA = table.Column<string>(type: "text", nullable: true),
                    RuneB = table.Column<string>(type: "text", nullable: true),
                    FormB = table.Column<string>(type: "text", nullable: true),
                    StatesA = table.Column<string>(type: "text", nullable: false),
                    StatesB = table.Column<string>(type: "text", nullable: false),
                    PointsDeltaA = table.Column<int>(type: "integer", nullable: false),
                    PointsDeltaB = table.Column<int>(type: "integer", nullable: false),
                    LeagueChangeA = table.Column<int>(type: "integer", nullable: false),
                    LeagueChangeB = table.Column<int>(type: "integer", nullable: false),
                    RatingDeltaA = table.Column<int>(type: "integer", nullable: false),
                    RatingDeltaB = table.Column<int>(type: "integer", nullable: false),
                    CoinsA = table.Column<int>(type: "integer", nullable: false),
                    CoinsB = table.Column<int>(type: "integer", nullable: false),
                    UsedRunesA = table.Column<string>(type: "text", nullable: false),
                    UsedRunesB = table.Column<string>(type: "text", nullable: false),
                    LastLog = table.Column<string>(type: "text", nullable: false),
                    LastRollA = table.Column<string>(type: "text", nullable: true),
                    LastRollB = table.Column<string>(type: "text", nullable: true),
                    WinnerId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDraw = table.Column<bool>(type: "boolean", nullable: false),
                    ResultsApplied = table.Column<bool>(type: "boolean", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CombatMatches", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CombatRounds",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    Turn = table.Column<int>(type: "integer", nullable: false),
                    RuneA = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    FormA = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    StateA = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    RuneB = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    FormB = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    StateB = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    HpA = table.Column<int>(type: "integer", nullable: false),
                    HpB = table.Column<int>(type: "integer", nullable: false),
                    Log = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CombatRounds", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CombatStates",
                columns: table => new
                {
                    Name = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Beats = table.Column<string>(type: "text", nullable: false),
                    LosesTo = table.Column<string>(type: "text", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CombatStates", x => x.Name);
                });

            migrationBuilder.CreateTable(
                name: "EquipmentSlots",
                columns: table => new
                {
                    Key = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EquipmentSlots", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "FriendChallenges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    HostId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CombatMatchId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FriendChallenges", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GameClasses",
                columns: table => new
                {
                    Name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Playstyle = table.Column<string>(type: "text", nullable: false),
                    CoreRule = table.Column<string>(type: "text", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameClasses", x => x.Name);
                });

            migrationBuilder.CreateTable(
                name: "GameSettings",
                columns: table => new
                {
                    Key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Value = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameSettings", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "GameTips",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Text = table.Column<string>(type: "text", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameTips", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "HallOfFame",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Username = table.Column<string>(type: "text", nullable: false),
                    SeasonName = table.Column<string>(type: "text", nullable: false),
                    League = table.Column<int>(type: "integer", nullable: false),
                    Matches = table.Column<int>(type: "integer", nullable: false),
                    CrownedAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HallOfFame", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Items",
                columns: table => new
                {
                    Key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Kind = table.Column<string>(type: "text", nullable: false),
                    Icon = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    PriceCoins = table.Column<int>(type: "integer", nullable: false),
                    PriceGems = table.Column<int>(type: "integer", nullable: false),
                    InShop = table.Column<bool>(type: "boolean", nullable: false),
                    IsStarter = table.Column<bool>(type: "boolean", nullable: false),
                    Power = table.Column<int>(type: "integer", nullable: false),
                    Guard = table.Column<int>(type: "integer", nullable: false),
                    Vigor = table.Column<int>(type: "integer", nullable: false),
                    Focus = table.Column<int>(type: "integer", nullable: false),
                    Grit = table.Column<int>(type: "integer", nullable: false),
                    Edge = table.Column<int>(type: "integer", nullable: false),
                    Pierce = table.Column<int>(type: "integer", nullable: false),
                    Insight = table.Column<int>(type: "integer", nullable: false),
                    Resolve = table.Column<int>(type: "integer", nullable: false),
                    Rarity = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Image = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Items", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "LeagueTiers",
                columns: table => new
                {
                    Number = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeagueTiers", x => x.Number);
                });

            migrationBuilder.CreateTable(
                name: "MatchQueue",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PlayerId = table.Column<Guid>(type: "uuid", nullable: false),
                    JoinedAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    LastHeartbeatUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    Matched = table.Column<bool>(type: "boolean", nullable: false),
                    OpponentId = table.Column<Guid>(type: "uuid", nullable: true),
                    CombatMatchId = table.Column<Guid>(type: "uuid", nullable: true),
                    Mode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MatchQueue", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ObjectiveClaims",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PlayerId = table.Column<Guid>(type: "uuid", nullable: false),
                    ObjectiveKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PeriodKey = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ClaimedAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ObjectiveClaims", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Objectives",
                columns: table => new
                {
                    Key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Kind = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Task = table.Column<string>(type: "text", nullable: false),
                    Metric = table.Column<string>(type: "text", nullable: false),
                    Target = table.Column<int>(type: "integer", nullable: false),
                    Repeat = table.Column<string>(type: "text", nullable: false),
                    RewardCoins = table.Column<int>(type: "integer", nullable: false),
                    RewardGems = table.Column<int>(type: "integer", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Objectives", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "PlayerItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PlayerId = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Equipped = table.Column<bool>(type: "boolean", nullable: false),
                    AcquiredAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerItems", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PlayerMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PlayerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sender = table.Column<string>(type: "text", nullable: false),
                    Subject = table.Column<string>(type: "text", nullable: false),
                    Body = table.Column<string>(type: "text", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    ReadAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerMessages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Players",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Username = table.Column<string>(type: "text", nullable: false),
                    Email = table.Column<string>(type: "text", nullable: false),
                    PasswordHash = table.Column<string>(type: "text", nullable: false),
                    ClassName = table.Column<string>(type: "text", nullable: true),
                    League = table.Column<int>(type: "integer", nullable: false),
                    Points = table.Column<int>(type: "integer", nullable: false),
                    SeasonWins = table.Column<int>(type: "integer", nullable: false),
                    SeasonMatches = table.Column<int>(type: "integer", nullable: false),
                    SkillRating = table.Column<int>(type: "integer", nullable: false),
                    Coins = table.Column<int>(type: "integer", nullable: false),
                    Gems = table.Column<int>(type: "integer", nullable: false),
                    StoryChapter = table.Column<int>(type: "integer", nullable: false),
                    TutorialDone = table.Column<bool>(type: "boolean", nullable: false),
                    IsAdmin = table.Column<bool>(type: "boolean", nullable: false),
                    Banned = table.Column<bool>(type: "boolean", nullable: false),
                    RealmInitializedAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Players", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PlayerSettings",
                columns: table => new
                {
                    PlayerId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReduceMotion = table.Column<bool>(type: "boolean", nullable: false),
                    DamageNumbers = table.Column<bool>(type: "boolean", nullable: false),
                    DiceAnimation = table.Column<bool>(type: "boolean", nullable: false),
                    CompactHud = table.Column<bool>(type: "boolean", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerSettings", x => x.PlayerId);
                });

            migrationBuilder.CreateTable(
                name: "PlayerTalismans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PlayerId = table.Column<Guid>(type: "uuid", nullable: false),
                    TalismanKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Equipped = table.Column<bool>(type: "boolean", nullable: false),
                    AcquiredAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerTalismans", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SpellForms",
                columns: table => new
                {
                    Name = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Face = table.Column<int>(type: "integer", nullable: false),
                    State = table.Column<string>(type: "text", nullable: false),
                    Blurb = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SpellForms", x => x.Name);
                });

            migrationBuilder.CreateTable(
                name: "StoryChapters",
                columns: table => new
                {
                    Number = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    Body = table.Column<string>(type: "text", nullable: false),
                    UnlockMatches = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoryChapters", x => x.Number);
                });

            migrationBuilder.CreateTable(
                name: "Talismans",
                columns: table => new
                {
                    Key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Effect = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Drawback = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Icon = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Rarity = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Budget = table.Column<int>(type: "integer", nullable: false),
                    PriceCoins = table.Column<int>(type: "integer", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    Ranked = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Talismans", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "UploadedFiles",
                columns: table => new
                {
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Data = table.Column<byte[]>(type: "bytea", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UploadedFiles", x => x.Name);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ClassRunes_ClassName",
                table: "ClassRunes",
                column: "ClassName");

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

            migrationBuilder.CreateIndex(
                name: "IX_FriendChallenges_Code",
                table: "FriendChallenges",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FriendChallenges_HostId",
                table: "FriendChallenges",
                column: "HostId");

            migrationBuilder.CreateIndex(
                name: "IX_MatchQueue_PlayerId_Matched",
                table: "MatchQueue",
                columns: new[] { "PlayerId", "Matched" });

            migrationBuilder.CreateIndex(
                name: "IX_ObjectiveClaims_PlayerId_ObjectiveKey_PeriodKey",
                table: "ObjectiveClaims",
                columns: new[] { "PlayerId", "ObjectiveKey", "PeriodKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlayerItems_PlayerId_ItemKey",
                table: "PlayerItems",
                columns: new[] { "PlayerId", "ItemKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlayerMessages_PlayerId",
                table: "PlayerMessages",
                column: "PlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_Players_Email",
                table: "Players",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Players_Username",
                table: "Players",
                column: "Username",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlayerTalismans_PlayerId_TalismanKey",
                table: "PlayerTalismans",
                columns: new[] { "PlayerId", "TalismanKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ArenaModes");

            migrationBuilder.DropTable(
                name: "Attributes");

            migrationBuilder.DropTable(
                name: "ClassRunes");

            migrationBuilder.DropTable(
                name: "CombatMatches");

            migrationBuilder.DropTable(
                name: "CombatRounds");

            migrationBuilder.DropTable(
                name: "CombatStates");

            migrationBuilder.DropTable(
                name: "EquipmentSlots");

            migrationBuilder.DropTable(
                name: "FriendChallenges");

            migrationBuilder.DropTable(
                name: "GameClasses");

            migrationBuilder.DropTable(
                name: "GameSettings");

            migrationBuilder.DropTable(
                name: "GameTips");

            migrationBuilder.DropTable(
                name: "HallOfFame");

            migrationBuilder.DropTable(
                name: "Items");

            migrationBuilder.DropTable(
                name: "LeagueTiers");

            migrationBuilder.DropTable(
                name: "MatchQueue");

            migrationBuilder.DropTable(
                name: "ObjectiveClaims");

            migrationBuilder.DropTable(
                name: "Objectives");

            migrationBuilder.DropTable(
                name: "PlayerItems");

            migrationBuilder.DropTable(
                name: "PlayerMessages");

            migrationBuilder.DropTable(
                name: "Players");

            migrationBuilder.DropTable(
                name: "PlayerSettings");

            migrationBuilder.DropTable(
                name: "PlayerTalismans");

            migrationBuilder.DropTable(
                name: "SpellForms");

            migrationBuilder.DropTable(
                name: "StoryChapters");

            migrationBuilder.DropTable(
                name: "Talismans");

            migrationBuilder.DropTable(
                name: "UploadedFiles");
        }
    }
}
