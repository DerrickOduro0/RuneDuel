using Microsoft.EntityFrameworkCore;

namespace RuneReign.Api;

public sealed class Player
{
    public Guid Id { get; set; }
    public required string Username { get; set; }
    public required string Email { get; set; }
    public required string PasswordHash { get; set; }
    public string? ClassName { get; set; }
    public int League { get; set; } = 10;
    public int Points { get; set; }
    public int SeasonWins { get; set; }
    public int SeasonMatches { get; set; }
    public int SkillRating { get; set; } = 1000;
    public int Coins { get; set; }
    public int Gems { get; set; }
    public int StoryChapter { get; set; }
    public bool TutorialDone { get; set; }
    // Admins can open the admin page. Set from the Admin:Users setting when the server starts.
    public bool IsAdmin { get; set; }
    // A banned player cannot log in or use the game.
    public bool Banned { get; set; }
    // Set the first time the player's wallet, starter relics and welcome letter are created.
    public DateTime? RealmInitializedAtUtc { get; set; }
    // When the account was made, and when the player last opened the game.
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? LastSeenAtUtc { get; set; }
}

public sealed class MatchQueueEntry
{
    public Guid Id { get; set; }
    public Guid PlayerId { get; set; }
    public DateTime JoinedAtUtc { get; set; }
    public DateTime? LastHeartbeatUtc { get; set; }
    public bool Matched { get; set; }
    public Guid? OpponentId { get; set; }
    public Guid? CombatMatchId { get; set; }
    // "ranked" or "casual". Players are only paired with someone searching for the same mode.
    public string Mode { get; set; } = DuelMode.Ranked;
}

public static class DuelMode
{
    public const string Ranked = "ranked";
    public const string Casual = "casual";
    public const string Friend = "friend";
    public static bool IsQueueMode(string? value) => value is Ranked or Casual;
}

// A private invitation. The host shares the code; whoever enters it first starts a duel with the host.
public sealed class FriendChallenge
{
    public Guid Id { get; set; }
    public string Code { get; set; } = "";
    public Guid HostId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAtUtc { get; set; }
    public Guid? CombatMatchId { get; set; }
    // Set when the challenge was sent straight to a friend; they see it as an invite. Anyone with the code can still join.
    public Guid? InviteeId { get; set; }
}

// A friend request, and once accepted, a friendship. One row per pair of players.
public sealed class Friendship
{
    public Guid Id { get; set; }
    public Guid RequesterId { get; set; }
    public Guid AddresseeId { get; set; }
    public bool Accepted { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? AcceptedAtUtc { get; set; }
}

public sealed class CombatMatch
{
    public Guid Id { get; set; }
    public Guid PlayerAId { get; set; }
    public Guid PlayerBId { get; set; }
    public string ClassA { get; set; } = "";
    public string ClassB { get; set; } = "";
    public int HpA { get; set; } = 30;
    public int HpB { get; set; } = 30;
    public int ShieldA { get; set; }
    public int ShieldB { get; set; }
    public int ManaA { get; set; }
    public int ManaB { get; set; }
    public int Turn { get; set; } = 1;
    public string? PendingA { get; set; }
    public string? PendingB { get; set; }
    public int PendingD8AdjustmentA { get; set; }
    public int PendingD8AdjustmentB { get; set; }
    public bool PendingRerollA { get; set; }
    public bool PendingRerollB { get; set; }
    public string? PendingFormA { get; set; }
    public string? PendingFormB { get; set; }
    public bool AcceptedA { get; set; }
    public bool AcceptedB { get; set; }
    // Set while the match is waiting for both players to press Enter Match; null once combat has begun.
    public DateTime? AcceptDeadlineUtc { get; set; }
    // The last time each player's client looked at the duel, used to spot a player who has left.
    public DateTime? LastSeenA { get; set; }
    public DateTime? LastSeenB { get; set; }
    // Each player's capped equipment bonuses, fixed when the duel was made: "power,guard,vigor,focus,grit".
    public string BonusesA { get; set; } = "";
    public string BonusesB { get; set; } = "";
    // Set for a practice duel against the Shade ("easy" or "hard"). Practice duels never change league, rating or quests.
    public string? ShadeDifficulty { get; set; }
    // Talismans, fixed when the duel starts. Uses = charges spent; Flags = short-lived state;
    // Fired = the talisman that went off in the last round (shown in the round banner).
    public string? TalismanA { get; set; }
    public string? TalismanB { get; set; }
    public int TalismanUsesA { get; set; }
    public int TalismanUsesB { get; set; }
    public string TalismanFlagsA { get; set; } = "";
    public string TalismanFlagsB { get; set; } = "";
    public string? TalismanFiredA { get; set; }
    public string? TalismanFiredB { get; set; }
    // "ranked", "casual" or "friend". Only ranked duels change league points and rating.
    public string Mode { get; set; } = DuelMode.Ranked;
    // Rematch requests after the duel ends (casual and friend duels only), and the duel they started.
    public bool RematchA { get; set; }
    public bool RematchB { get; set; }
    public Guid? RematchMatchId { get; set; }
    // The dice on the table this turn. They are rolled at the start of the turn and both players can see them.
    public string? RuneA { get; set; }
    public string? FormA { get; set; }
    public string? RuneB { get; set; }
    public string? FormB { get; set; }
    // Each player's last few declared states, oldest first: "Break,Flow,Bind".
    public string StatesA { get; set; } = "";
    public string StatesB { get; set; } = "";
    // What the duel changed for each player, kept for the results screen and match history.
    public int PointsDeltaA { get; set; }
    public int PointsDeltaB { get; set; }
    public int LeagueChangeA { get; set; }
    public int LeagueChangeB { get; set; }
    public int RatingDeltaA { get; set; }
    public int RatingDeltaB { get; set; }
    public int CoinsA { get; set; }
    public int CoinsB { get; set; }
    public string UsedRunesA { get; set; } = "";
    public string UsedRunesB { get; set; } = "";
    public string LastLog { get; set; } = "The dice are rolled. Read the table, then declare your state.";
    public string? LastRollA { get; set; }
    public string? LastRollB { get; set; }
    public Guid? WinnerId { get; set; }
    public bool IsDraw { get; set; }
    public bool ResultsApplied { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

// One resolved turn of a duel, for replays in the match history.
public sealed class CombatRound
{
    public Guid Id { get; set; }
    public Guid MatchId { get; set; }
    public int Turn { get; set; }
    public string RuneA { get; set; } = "";
    public string FormA { get; set; } = "";
    public string StateA { get; set; } = "";
    public string RuneB { get; set; } = "";
    public string FormB { get; set; } = "";
    public string StateB { get; set; } = "";
    public int HpA { get; set; }
    public int HpB { get; set; }
    public string Log { get; set; } = "";
}

public sealed class HallOfFameEntry
{
    public Guid Id { get; set; }
    public required string Username { get; set; }
    public required string SeasonName { get; set; }
    public int League { get; set; }
    public int Matches { get; set; }
    public DateTime CrownedAtUtc { get; set; }
}

/* ---------- Game content (seeded on first start, then read from the database) ---------- */

public sealed class GameSetting
{
    public required string Key { get; set; }
    public required string Value { get; set; }
    public string Description { get; set; } = "";
}

public sealed class GameClass
{
    public required string Name { get; set; }
    public required string Playstyle { get; set; }
    public required string CoreRule { get; set; }
    public int SortOrder { get; set; }
}

public sealed class ClassRune
{
    public int Id { get; set; }
    public required string ClassName { get; set; }
    public required string Name { get; set; }
    public required string Meaning { get; set; }
    public required string Symbol { get; set; }
    public int SortOrder { get; set; }
}

public sealed class LeagueTier
{
    public int Number { get; set; }
    public required string Name { get; set; }
}

public sealed class SpellForm
{
    public required string Name { get; set; }
    public int Face { get; set; }
    public required string State { get; set; }
    public required string Blurb { get; set; }
}

public sealed class CombatStateInfo
{
    public required string Name { get; set; }
    public required string Beats { get; set; }
    public required string LosesTo { get; set; }
    public int SortOrder { get; set; }
}

public sealed class GameTip
{
    public int Id { get; set; }
    public required string Text { get; set; }
    public int SortOrder { get; set; }
}

public sealed class ArenaMode
{
    public required string Key { get; set; }
    public required string Title { get; set; }
    public required string Tag { get; set; }
    public required string Description { get; set; }
    public required string Icon { get; set; }
    public bool IsLive { get; set; }
    public int SortOrder { get; set; }
}

public sealed class ItemDefinition
{
    public required string Key { get; set; }
    public required string Name { get; set; }
    public required string Kind { get; set; }
    public required string Icon { get; set; }
    public string Description { get; set; } = "";
    public int PriceCoins { get; set; }
    public int PriceGems { get; set; }
    public bool InShop { get; set; }
    public bool IsStarter { get; set; }
    // Attribute points this item gives while equipped.
    public int Power { get; set; }
    public int Guard { get; set; }
    public int Vigor { get; set; }
    public int Focus { get; set; }
    public int Grit { get; set; }
    public int Edge { get; set; }
    public int Pierce { get; set; }
    public int Insight { get; set; }
    public int Resolve { get; set; }
    // common, rare or epic
    public string Rarity { get; set; } = "common";
    // Picture shown for the item. Empty means /items/{Key}.png in the front end's public folder.
    // Set it to any URL (for example a CDN) to use a different image.
    public string Image { get; set; } = "";
    public int SortOrder { get; set; }
}

public sealed class AttributeDefinition
{
    public required string Key { get; set; }
    public required string Name { get; set; }
    public required string Icon { get; set; }
    public required string Effect { get; set; }
    public int Cap { get; set; }
    public int SortOrder { get; set; }
}

public sealed class EquipmentSlot
{
    public required string Key { get; set; }
    public required string Name { get; set; }
    public int SortOrder { get; set; }
}

public sealed class Objective
{
    public required string Key { get; set; }
    // quest, mission, or reward
    public required string Kind { get; set; }
    public required string Name { get; set; }
    public required string Task { get; set; }
    // season_matches, season_wins, matches_today, wins_today, login, promotions
    public required string Metric { get; set; }
    public int Target { get; set; } = 1;
    // once, daily, or season
    public required string Repeat { get; set; }
    public int RewardCoins { get; set; }
    public int RewardGems { get; set; }
    public int SortOrder { get; set; }
}

public sealed class StoryChapterDefinition
{
    public int Number { get; set; }
    public required string Title { get; set; }
    public required string Body { get; set; }
    public int UnlockMatches { get; set; }
}

/* ---------- Per-player records ---------- */

public sealed class PlayerSettings
{
    public Guid PlayerId { get; set; }
    public bool ReduceMotion { get; set; }
    public bool DamageNumbers { get; set; } = true;
    public bool DiceAnimation { get; set; } = true;
    public bool CompactHud { get; set; }
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

// A talisman for sale or crafted. Its behaviour is in RuneReign.Domain.Talismans, keyed by Key;
// these rows hold the names, text, price and an on/off switch for balance.
public sealed class TalismanDefinition
{
    public required string Key { get; set; }
    public required string Name { get; set; }
    public required string Effect { get; set; }
    public string Drawback { get; set; } = "";
    public string Icon { get; set; } = "\u2727";
    public string Rarity { get; set; } = "common";
    // Power points against the budget (Common 3, Rare 4, Epic 5).
    public int Budget { get; set; } = 3;
    public int PriceCoins { get; set; }
    public bool Enabled { get; set; } = true;
    // Allowed in ranked duels.
    public bool Ranked { get; set; } = true;
    public int SortOrder { get; set; }
}

public sealed class PlayerTalisman
{
    public Guid Id { get; set; }
    public Guid PlayerId { get; set; }
    public required string TalismanKey { get; set; }
    public bool Equipped { get; set; }
    public DateTime AcquiredAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class PlayerItem
{
    public Guid Id { get; set; }
    public Guid PlayerId { get; set; }
    public required string ItemKey { get; set; }
    public bool Equipped { get; set; }
    public DateTime AcquiredAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class ObjectiveClaim
{
    public Guid Id { get; set; }
    public Guid PlayerId { get; set; }
    public required string ObjectiveKey { get; set; }
    // "" for one-off objectives, the UTC date for daily ones, the season (yyyy-MM) for seasonal ones.
    public required string PeriodKey { get; set; }
    public DateTime ClaimedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class PlayerMessage
{
    public Guid Id { get; set; }
    public Guid PlayerId { get; set; }
    public required string Sender { get; set; }
    public required string Subject { get; set; }
    public required string Body { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ReadAtUtc { get; set; }
}

// A picture uploaded from the admin page. Kept in the database so it survives redeploys on hosts with no lasting disk.
public sealed class UploadedFile
{
    public string Name { get; set; } = "";
    public string ContentType { get; set; } = "";
    public byte[] Data { get; set; } = [];
    public DateTime CreatedAtUtc { get; set; }
}

public sealed class RuneDbContext(DbContextOptions<RuneDbContext> options) : DbContext(options)
{
    public DbSet<Player> Players => Set<Player>();
    public DbSet<MatchQueueEntry> MatchQueue => Set<MatchQueueEntry>();
    public DbSet<HallOfFameEntry> HallOfFame => Set<HallOfFameEntry>();
    public DbSet<CombatMatch> CombatMatches => Set<CombatMatch>();
    public DbSet<FriendChallenge> FriendChallenges => Set<FriendChallenge>();
    public DbSet<CombatRound> CombatRounds => Set<CombatRound>();
    public DbSet<GameSetting> GameSettings => Set<GameSetting>();
    public DbSet<GameClass> GameClasses => Set<GameClass>();
    public DbSet<ClassRune> ClassRunes => Set<ClassRune>();
    public DbSet<LeagueTier> LeagueTiers => Set<LeagueTier>();
    public DbSet<SpellForm> SpellForms => Set<SpellForm>();
    public DbSet<CombatStateInfo> CombatStates => Set<CombatStateInfo>();
    public DbSet<GameTip> GameTips => Set<GameTip>();
    public DbSet<ArenaMode> ArenaModes => Set<ArenaMode>();
    public DbSet<ItemDefinition> Items => Set<ItemDefinition>();
    public DbSet<Objective> Objectives => Set<Objective>();
    public DbSet<AttributeDefinition> Attributes => Set<AttributeDefinition>();
    public DbSet<EquipmentSlot> EquipmentSlots => Set<EquipmentSlot>();
    public DbSet<StoryChapterDefinition> StoryChapters => Set<StoryChapterDefinition>();
    public DbSet<PlayerSettings> PlayerSettings => Set<PlayerSettings>();
    public DbSet<PlayerItem> PlayerItems => Set<PlayerItem>();
    public DbSet<TalismanDefinition> Talismans => Set<TalismanDefinition>();
    public DbSet<PlayerTalisman> PlayerTalismans => Set<PlayerTalisman>();
    public DbSet<ObjectiveClaim> ObjectiveClaims => Set<ObjectiveClaim>();
    public DbSet<PlayerMessage> PlayerMessages => Set<PlayerMessage>();
    public DbSet<UploadedFile> UploadedFiles => Set<UploadedFile>();
    public DbSet<Friendship> Friendships => Set<Friendship>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.Entity<Player>().HasIndex(x => x.Username).IsUnique();
        builder.Entity<Player>().HasIndex(x => x.Email).IsUnique();
        builder.Entity<MatchQueueEntry>().HasIndex(x => new { x.PlayerId, x.Matched });
        builder.Entity<CombatRound>().HasIndex(x => new { x.MatchId, x.Turn });
        builder.Entity<CombatRound>().Property(x => x.RuneA).HasMaxLength(16);
        builder.Entity<CombatRound>().Property(x => x.RuneB).HasMaxLength(16);
        builder.Entity<CombatRound>().Property(x => x.FormA).HasMaxLength(16);
        builder.Entity<CombatRound>().Property(x => x.FormB).HasMaxLength(16);
        builder.Entity<CombatRound>().Property(x => x.StateA).HasMaxLength(8);
        builder.Entity<CombatRound>().Property(x => x.StateB).HasMaxLength(8);
        builder.Entity<MatchQueueEntry>().Property(x => x.Mode).HasMaxLength(16);
        builder.Entity<CombatMatch>().Property(x => x.Mode).HasMaxLength(16);
        builder.Entity<FriendChallenge>().HasIndex(x => x.Code).IsUnique();
        builder.Entity<FriendChallenge>().HasIndex(x => x.HostId);
        builder.Entity<FriendChallenge>().HasIndex(x => x.InviteeId);
        builder.Entity<Friendship>().HasIndex(x => new { x.RequesterId, x.AddresseeId }).IsUnique();
        builder.Entity<Friendship>().HasIndex(x => x.AddresseeId);
        builder.Entity<FriendChallenge>().Property(x => x.Code).HasMaxLength(12);
        builder.Entity<CombatMatch>().HasIndex(x => x.PlayerAId);
        builder.Entity<CombatMatch>().HasIndex(x => x.PlayerBId);

        builder.Entity<GameSetting>().HasKey(x => x.Key);
        builder.Entity<GameSetting>().Property(x => x.Key).HasMaxLength(64);
        builder.Entity<GameClass>().HasKey(x => x.Name);
        builder.Entity<GameClass>().Property(x => x.Name).HasMaxLength(64);
        builder.Entity<ClassRune>().Property(x => x.ClassName).HasMaxLength(64);
        builder.Entity<ClassRune>().HasIndex(x => x.ClassName);
        builder.Entity<LeagueTier>().HasKey(x => x.Number);
        builder.Entity<LeagueTier>().Property(x => x.Number).ValueGeneratedNever();
        builder.Entity<SpellForm>().HasKey(x => x.Name);
        builder.Entity<SpellForm>().Property(x => x.Name).HasMaxLength(32);
        builder.Entity<CombatStateInfo>().ToTable("CombatStates").HasKey(x => x.Name);
        builder.Entity<CombatStateInfo>().Property(x => x.Name).HasMaxLength(16);
        builder.Entity<ArenaMode>().HasKey(x => x.Key);
        builder.Entity<ArenaMode>().Property(x => x.Key).HasMaxLength(32);
        builder.Entity<ItemDefinition>().ToTable("Items").HasKey(x => x.Key);
        builder.Entity<ItemDefinition>().Property(x => x.Key).HasMaxLength(64);
        builder.Entity<ItemDefinition>().Property(x => x.Rarity).HasMaxLength(16);
        builder.Entity<ItemDefinition>().Property(x => x.Image).HasMaxLength(512);
        builder.Entity<AttributeDefinition>().ToTable("Attributes").HasKey(x => x.Key);
        builder.Entity<AttributeDefinition>().Property(x => x.Key).HasMaxLength(16);
        builder.Entity<EquipmentSlot>().HasKey(x => x.Key);
        builder.Entity<EquipmentSlot>().Property(x => x.Key).HasMaxLength(16);
        builder.Entity<Objective>().HasKey(x => x.Key);
        builder.Entity<Objective>().Property(x => x.Key).HasMaxLength(64);
        builder.Entity<StoryChapterDefinition>().ToTable("StoryChapters").HasKey(x => x.Number);
        builder.Entity<StoryChapterDefinition>().Property(x => x.Number).ValueGeneratedNever();

        builder.Entity<PlayerSettings>().HasKey(x => x.PlayerId);
        builder.Entity<PlayerItem>().Property(x => x.ItemKey).HasMaxLength(64);
        builder.Entity<PlayerItem>().HasIndex(x => new { x.PlayerId, x.ItemKey }).IsUnique();
        builder.Entity<TalismanDefinition>().HasKey(x => x.Key);
        builder.Entity<TalismanDefinition>().Property(x => x.Key).HasMaxLength(64);
        builder.Entity<TalismanDefinition>().Property(x => x.Name).HasMaxLength(64);
        builder.Entity<TalismanDefinition>().Property(x => x.Effect).HasMaxLength(300);
        builder.Entity<TalismanDefinition>().Property(x => x.Drawback).HasMaxLength(300);
        builder.Entity<TalismanDefinition>().Property(x => x.Icon).HasMaxLength(16);
        builder.Entity<TalismanDefinition>().Property(x => x.Rarity).HasMaxLength(16);
        builder.Entity<PlayerTalisman>().Property(x => x.TalismanKey).HasMaxLength(64);
        builder.Entity<PlayerTalisman>().HasIndex(x => new { x.PlayerId, x.TalismanKey }).IsUnique();
        builder.Entity<CombatMatch>().Property(x => x.TalismanA).HasMaxLength(64);
        builder.Entity<CombatMatch>().Property(x => x.TalismanB).HasMaxLength(64);
        builder.Entity<CombatMatch>().Property(x => x.TalismanFlagsA).HasMaxLength(64);
        builder.Entity<CombatMatch>().Property(x => x.TalismanFlagsB).HasMaxLength(64);
        builder.Entity<CombatMatch>().Property(x => x.TalismanFiredA).HasMaxLength(64);
        builder.Entity<CombatMatch>().Property(x => x.TalismanFiredB).HasMaxLength(64);
        builder.Entity<ObjectiveClaim>().Property(x => x.ObjectiveKey).HasMaxLength(64);
        builder.Entity<ObjectiveClaim>().Property(x => x.PeriodKey).HasMaxLength(16);
        builder.Entity<ObjectiveClaim>().HasIndex(x => new { x.PlayerId, x.ObjectiveKey, x.PeriodKey }).IsUnique();
        builder.Entity<PlayerMessage>().HasIndex(x => x.PlayerId);
        builder.Entity<UploadedFile>().HasKey(x => x.Name);
        builder.Entity<UploadedFile>().Property(x => x.Name).HasMaxLength(128);
        builder.Entity<UploadedFile>().Property(x => x.ContentType).HasMaxLength(32);
    }
}
