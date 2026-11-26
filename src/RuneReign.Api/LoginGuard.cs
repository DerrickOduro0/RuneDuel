using System.Collections.Concurrent;

namespace RuneReign.Api;

// Slows down password guessing: after too many wrong passwords for one account, that account is locked for a while.
// Kept in memory, so a server restart clears it; that is fine for a single game server.
public static class LoginGuard
{
    private const int MaxFailures = 8;
    private static readonly TimeSpan FailureWindow = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan LockTime = TimeSpan.FromMinutes(15);
    private static readonly ConcurrentDictionary<string, (int Count, DateTime First, DateTime? LockedUntil)> failures = new();

    public static bool IsLocked(string identifier, out TimeSpan wait)
    {
        wait = TimeSpan.Zero;
        if (!failures.TryGetValue(identifier, out var entry) || entry.LockedUntil is not DateTime until) return false;
        if (until <= DateTime.UtcNow) { failures.TryRemove(identifier, out _); return false; }
        wait = until - DateTime.UtcNow;
        return true;
    }

    public static void Failed(string identifier)
    {
        var now = DateTime.UtcNow;
        failures.AddOrUpdate(identifier,
            _ => (1, now, null),
            (_, e) =>
            {
                if (now - e.First > FailureWindow) return (1, now, null);
                var count = e.Count + 1;
                return (count, e.First, count >= MaxFailures ? now + LockTime : e.LockedUntil);
            });
        // Keep the table small.
        if (failures.Count > 10000)
            foreach (var old in failures.Where(x => now - x.Value.First > FailureWindow && x.Value.LockedUntil is null).Select(x => x.Key).ToList())
                failures.TryRemove(old, out _);
    }

    public static void Succeeded(string identifier) => failures.TryRemove(identifier, out _);

    public static void Clear(string identifier) => failures.TryRemove(identifier.ToLowerInvariant(), out _);

    // The player's address. Behind Vercel and Render the real one is the first entry of X-Forwarded-For.
    public static string ClientAddress(HttpContext http)
    {
        var forwarded = http.Request.Headers["X-Forwarded-For"].ToString();
        if (!string.IsNullOrWhiteSpace(forwarded)) return forwarded.Split(',')[0].Trim();
        return http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }
}
