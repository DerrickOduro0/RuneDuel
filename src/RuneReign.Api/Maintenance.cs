using System.Security.Claims;
using Microsoft.EntityFrameworkCore;

namespace RuneReign.Api;

public sealed record MaintenanceEdit(bool? On, string? Message, DateTime? BackAtUtc, bool? LetDuelsFinish, string? Notice);

// Maintenance mode: while it is on, players get a "down for maintenance" screen and admins can still use everything.
// Kept in the GameSettings table so it survives restarts and redeploys; read into memory so checking it costs nothing.
public static class Maintenance
{
    private const string KeyOn = "MaintenanceOn";
    private const string KeyMessage = "MaintenanceMessage";
    private const string KeyBackAt = "MaintenanceBackAtUtc";
    private const string KeyLetDuels = "MaintenanceLetDuelsFinish";
    private const string KeyNotice = "MaintenanceNotice";
    private const string KeyStartedAt = "MaintenanceStartedAtUtc";
    public static readonly string[] Keys = [KeyOn, KeyMessage, KeyBackAt, KeyLetDuels, KeyNotice, KeyStartedAt];

    public const string DefaultMessage = "Rune Duel is being updated. We will be back shortly.";

    public static bool On { get; private set; }
    public static string Message { get; private set; } = DefaultMessage;
    public static DateTime? BackAtUtc { get; private set; }
    public static bool LetDuelsFinish { get; private set; } = true;
    public static string Notice { get; private set; } = "";
    public static DateTime? StartedAtUtc { get; private set; }

    public static void Load(Dictionary<string, string> s)
    {
        On = s.GetValueOrDefault(KeyOn) == "1";
        Message = string.IsNullOrWhiteSpace(s.GetValueOrDefault(KeyMessage)) ? DefaultMessage : s[KeyMessage];
        BackAtUtc = DateTime.TryParse(s.GetValueOrDefault(KeyBackAt), null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var back) ? back : null;
        LetDuelsFinish = s.GetValueOrDefault(KeyLetDuels, "1") != "0";
        Notice = s.GetValueOrDefault(KeyNotice) ?? "";
        StartedAtUtc = DateTime.TryParse(s.GetValueOrDefault(KeyStartedAt), null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var start) ? start : null;
    }

    // What players see. Public: the game asks for it every minute to show the warning banner or the maintenance screen.
    public static object Status() => new
    {
        maintenance = On,
        message = On ? Message : null,
        backAtUtc = On ? BackAtUtc : null,
        notice = string.IsNullOrWhiteSpace(Notice) ? null : Notice
    };

    public static async Task Save(RuneDbContext db, MaintenanceEdit edit)
    {
        var turningOn = edit.On == true && !On;
        var values = new Dictionary<string, string?>
        {
            [KeyOn] = edit.On is null ? null : edit.On.Value ? "1" : "0",
            [KeyMessage] = edit.Message?.Trim(),
            [KeyBackAt] = edit.BackAtUtc is null ? (edit.On == false ? "" : null) : edit.BackAtUtc.Value.ToUniversalTime().ToString("o"),
            [KeyLetDuels] = edit.LetDuelsFinish is null ? null : edit.LetDuelsFinish.Value ? "1" : "0",
            [KeyNotice] = edit.Notice?.Trim(),
            [KeyStartedAt] = turningOn ? DateTime.UtcNow.ToString("o") : edit.On == false ? "" : null
        };
        foreach (var (key, value) in values)
        {
            if (value is null) continue;
            var row = await db.GameSettings.FindAsync(key);
            if (row is null) db.GameSettings.Add(new GameSetting { Key = key, Value = value, Description = "Maintenance mode (set on the admin page's Maintenance tab)." });
            else row.Value = value;
        }
        // Nobody should be left searching for a match that can never start.
        if (turningOn) await db.MatchQueue.Where(x => x.CombatMatchId == null).ExecuteDeleteAsync();
        await db.SaveChangesAsync();
        Load(ContentStore.ReadSettings(db));
    }

    // Requests players can still make while maintenance is on.
    private static bool AlwaysAllowed(string path) =>
        path == "/" ||
        !path.StartsWith("/api/v1/") ||
        path.StartsWith("/api/v1/health") ||
        path.StartsWith("/api/v1/status") ||
        path.StartsWith("/api/v1/files/") ||
        path.StartsWith("/api/v1/auth/login");   // so an admin can sign in

    // With "let duels finish": a player in a duel can keep playing it and see the result.
    private static bool DuelAllowed(string path, string method) =>
        path.StartsWith("/api/v1/combat/") || (method == "GET" && path == "/api/v1/me");

    public static void UseMaintenance(this WebApplication app)
    {
        app.Use(async (http, next) =>
        {
            if (!On) { await next(); return; }
            var path = http.Request.Path.Value ?? "/";
            if (AlwaysAllowed(path)) { await next(); return; }

            // Public reads with no login (game content, classes) stay available, so the game can draw its screens.
            if (http.Request.Method == "GET" && !http.Request.Headers.ContainsKey("Authorization")) { await next(); return; }

            // Admins are never blocked.
            if (Guid.TryParse(http.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id))
            {
                var db = http.RequestServices.GetRequiredService<RuneDbContext>();
                if (await db.Players.AsNoTracking().AnyAsync(x => x.Id == id && x.IsAdmin && !x.Banned)) { await next(); return; }
                if (LetDuelsFinish && DuelAllowed(path, http.Request.Method)) { await next(); return; }
            }

            http.Response.StatusCode = 503;
            await http.Response.WriteAsJsonAsync(new { error = new { code = "maintenance", message = Message, backAtUtc = BackAtUtc } });
        });
    }
}
