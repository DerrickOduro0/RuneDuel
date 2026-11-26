namespace RuneReign.Api;

// XP amounts and level rewards. Read from the GameSettings table, so they can be tuned on the admin page.
public sealed record ProgressionSettings(
    int XpRankedWin, int XpRankedLoss, int XpCasualWin, int XpCasualLoss, int XpFriendDuel,
    int XpPracticeEasyWin, int XpPracticeHardWin, int XpTutorial, int LevelUpCoins, int LevelUpGems, int GemLevelEvery);

// Levels come from experience (XP), which every duel gives and which never goes down.
// This is separate from league points, which move up and down with ranked results and reset with the Wheel.
public static class Progression
{
    public const int MaxLevel = 100;
    public static ProgressionSettings Settings { get; private set; } = Defaults;

    public static ProgressionSettings Defaults => new(50, 20, 35, 15, 10, 8, 20, 50, 50, 10, 5);

    // Settings rows added on first start; existing values are never overwritten.
    public static readonly (string Key, string Value, string Description)[] SettingRows =
    [
        ("XpRankedWin", "50", "XP for winning a ranked duel. A draw gives the average of win and loss."),
        ("XpRankedLoss", "20", "XP for finishing a ranked duel you lost."),
        ("XpCasualWin", "35", "XP for winning a casual duel."),
        ("XpCasualLoss", "15", "XP for finishing a casual duel you lost."),
        ("XpFriendDuel", "10", "XP each player gets for finishing a friend duel, whatever the result."),
        ("XpPracticeEasyWin", "8", "XP for beating the easy Shade."),
        ("XpPracticeHardWin", "20", "XP for beating the hard Shade."),
        ("XpTutorial", "50", "XP for finishing the tutorial duel."),
        ("LevelUpCoins", "50", "Coins given for every level gained."),
        ("LevelUpGems", "10", "Gems given on every milestone level (see GemLevelEvery)."),
        ("GemLevelEvery", "5", "Every this many levels also gives LevelUpGems.")
    ];

    public static void Load(Dictionary<string, string> s)
    {
        var d = Defaults;
        int Get(string key, int fallback) => s.TryGetValue(key, out var raw) && int.TryParse(raw, out var v) ? Math.Max(0, v) : fallback;
        Settings = new ProgressionSettings(
            Get("XpRankedWin", d.XpRankedWin), Get("XpRankedLoss", d.XpRankedLoss), Get("XpCasualWin", d.XpCasualWin), Get("XpCasualLoss", d.XpCasualLoss),
            Get("XpFriendDuel", d.XpFriendDuel), Get("XpPracticeEasyWin", d.XpPracticeEasyWin), Get("XpPracticeHardWin", d.XpPracticeHardWin),
            Get("XpTutorial", d.XpTutorial), Get("LevelUpCoins", d.LevelUpCoins), Get("LevelUpGems", d.LevelUpGems), Math.Max(1, Get("GemLevelEvery", d.GemLevelEvery)));
    }

    // XP needed to go from this level to the next: 100 at level 1, then 25 more for each level after.
    public static int XpToNext(int level) => 100 + 25 * (level - 1);

    public static int LevelFor(int xp)
    {
        var level = 1;
        while (level < MaxLevel && xp >= XpToNext(level)) { xp -= XpToNext(level); level++; }
        return level;
    }

    // Where a player stands inside their current level.
    public static object Progress(int xp)
    {
        var level = 1;
        var left = xp;
        while (level < MaxLevel && left >= XpToNext(level)) { left -= XpToNext(level); level++; }
        var need = level >= MaxLevel ? 0 : XpToNext(level);
        return new { level, xp = left, xpToNext = need, totalXp = xp, maxLevel = level >= MaxLevel };
    }

    // Adds XP and pays level-up rewards. Returns the new level if the player levelled up (0 if not), and what they got.
    public static (int NewLevel, int Coins, int Gems) Grant(Player player, int amount)
    {
        if (amount <= 0) return (0, 0, 0);
        var before = LevelFor(player.Xp);
        player.Xp += amount;
        var after = LevelFor(player.Xp);
        if (after <= before) return (0, 0, 0);
        var s = Settings;
        var coins = (after - before) * s.LevelUpCoins;
        var gems = Enumerable.Range(before + 1, after - before).Count(l => l % s.GemLevelEvery == 0) * s.LevelUpGems;
        player.Coins += coins;
        player.Gems += gems;
        return (after, coins, gems);
    }

    // XP for one player at the end of a duel.
    public static int DuelXp(CombatMatch match, Guid playerId, Guid? winnerId)
    {
        var s = Settings;
        var won = winnerId == playerId;
        var draw = winnerId is null;
        if (match.ShadeDifficulty is not null)
            return !won ? 0 : match.ShadeDifficulty == "hard" ? s.XpPracticeHardWin : match.ShadeDifficulty == "easy" ? s.XpPracticeEasyWin : 0;
        if (match.Mode == DuelMode.Friend) return s.XpFriendDuel;
        var (win, loss) = match.Mode == DuelMode.Casual ? (s.XpCasualWin, s.XpCasualLoss) : (s.XpRankedWin, s.XpRankedLoss);
        return draw ? (win + loss) / 2 : won ? win : loss;
    }
}
