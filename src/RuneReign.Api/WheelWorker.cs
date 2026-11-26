using Microsoft.EntityFrameworkCore;
using RuneReign.Domain;

namespace RuneReign.Api;

public sealed class WheelWorker(IServiceScopeFactory scopeFactory, GameRules rules, IWebHostEnvironment environment) : BackgroundService
{
    // Checks once at start-up and then every hour, but only turns the Wheel when the month has changed.
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                await TurnIfDue(scope.ServiceProvider.GetRequiredService<RuneDbContext>(), rules, DateTime.UtcNow);
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                scopeFactory.CreateScope().ServiceProvider.GetRequiredService<ILogger<WheelWorker>>().LogError(error, "Wheel check failed.");
            }
            await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
        }
    }

    // The current season is stored in GameSettings, so restarts never turn the Wheel twice.
    public static async Task<bool> TurnIfDue(RuneDbContext db, GameRules rules, DateTime utcNow)
    {
        var current = LeagueEngine.SeasonKey(utcNow);
        var row = await db.GameSettings.FindAsync("CurrentSeason");
        if (row is null)
        {
            db.GameSettings.Add(new GameSetting { Key = "CurrentSeason", Value = current, Description = "The season in progress (yyyy-MM). The Wheel turns when the month changes." });
            await db.SaveChangesAsync();
            return false;
        }
        if (!LeagueEngine.IsWheelDue(row.Value, utcNow)) return false;
        var finishedSeason = row.Value;
        row.Value = current;
        // Turn saves the new season together with the reset, so a crash cannot leave them out of step.
        await Turn(db, rules, finishedSeason);
        return true;
    }

    public static async Task Turn(RuneDbContext db, GameRules rules, string season)
    {
        var players = await db.Players.ToListAsync();
        var result = LeagueEngine.TurnWheel(players.Select(x => new WheelPlayer(x.Id.ToString(), x.Username, x.League, x.Points, x.SeasonMatches)), season, rules);
        if (result.Champion is not null)
            db.HallOfFame.Add(new HallOfFameEntry { Id = Guid.NewGuid(), Username = result.Champion.Username, SeasonName = result.Champion.SeasonName, League = result.Champion.League, Matches = result.Champion.Matches, CrownedAtUtc = DateTime.UtcNow });
        foreach (var player in players)
        {
            var next = result.Players[player.Id.ToString()];
            player.League = next.League;
            player.Points = next.Points;
            player.SeasonWins = next.SeasonWins;
            player.SeasonMatches = 0;
        }
        await db.SaveChangesAsync();
    }
}
