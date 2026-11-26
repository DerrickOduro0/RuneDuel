namespace RuneReign.Domain;

public sealed record RuneDefinition(string Name, string Meaning, string Symbol);
public sealed record ClassDefinition(string Name, string Playstyle, string CoreRule, IReadOnlyList<RuneDefinition> Runes);
public sealed record LeagueDefinition(int Number, string Name);
public sealed record GameRules(
    IReadOnlyList<ClassDefinition> Classes,
    IReadOnlyList<LeagueDefinition> Leagues,
    int WinPoints,
    int LossPoints,
    int DoubleWinCount,
    int ProtectedBracketMatches,
    int DefaultSkillRating,
    int MinimumChampionMatches,
    int PointsPerLeague,
    int InitialMatchmakingGap = 100,
    int MaximumMatchmakingGap = 1000,
    int MatchmakingGapExpansionSeconds = 10,
    int QueueTimeoutSeconds = 15,
    int ProtectedBracketMaxWaitSeconds = 60);

public sealed record LeagueState(int League, int Points, int SeasonWins);
public sealed record WheelPlayer(string Id, string Username, int League, int Points, int SeasonMatches);
public sealed record Champion(string Username, string SeasonName, int League, int Matches);
public sealed record WheelResult(Champion? Champion, IReadOnlyDictionary<string, LeagueState> Players);

public static class LeagueEngine
{
    public static LeagueState RecordResult(LeagueState state, bool win, GameRules rules)
    {
        var points = win && state.SeasonWins < rules.DoubleWinCount
            ? rules.WinPoints * 2
            : win ? rules.WinPoints : rules.LossPoints;
        var nextPoints = state.Points + points;
        var nextLeague = state.League;
        while (nextPoints >= rules.PointsPerLeague && nextLeague > 1)
        {
            nextPoints -= rules.PointsPerLeague;
            nextLeague--;
        }

        while (nextPoints < 0 && nextLeague < 10)
        {
            nextLeague++;
            nextPoints += rules.PointsPerLeague;
        }

        return state with
        {
            League = nextLeague,
            Points = Math.Clamp(nextPoints, 0, rules.PointsPerLeague),
            SeasonWins = state.SeasonWins + (win ? 1 : 0)
        };
    }

    // League 1 is the top of the ladder, so the best player has the lowest league number and the most points.
    public static WheelResult TurnWheel(IEnumerable<WheelPlayer> players, string seasonName, GameRules rules)
    {
        var ordered = players.OrderBy(x => x.League).ThenByDescending(x => x.Points).ThenByDescending(x => x.SeasonMatches).ToList();
        var winner = ordered.FirstOrDefault(x => x.SeasonMatches >= rules.MinimumChampionMatches);
        var champion = winner is null ? null : new Champion(winner.Username, seasonName, winner.League, winner.SeasonMatches);
        var next = ordered.ToDictionary(
            x => x.Id,
            x => new LeagueState(Math.Min(10, x.League <= 3 ? x.League + 3 : x.League + 1), 0, 0));
        return new WheelResult(champion, next);
    }

    // The season a date belongs to. The Wheel turns when this changes.
    public static string SeasonKey(DateTime utc) => utc.ToString("yyyy-MM");

    public static bool IsWheelDue(string? lastSeason, DateTime utcNow) =>
        lastSeason is not null && lastSeason != SeasonKey(utcNow);

    // Standard Elo update. scoreA is 1 for a win by A, 0 for a loss and 0.5 for a draw.
    public static (int A, int B) UpdateRatings(int ratingA, int ratingB, double scoreA, int k = 32)
    {
        var expectedA = 1.0 / (1.0 + Math.Pow(10, (ratingB - ratingA) / 400.0));
        var changeA = (int)Math.Round(k * (scoreA - expectedA), MidpointRounding.AwayFromZero);
        return (Math.Max(100, ratingA + changeA), Math.Max(100, ratingB - changeA));
    }
}
