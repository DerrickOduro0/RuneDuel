using Microsoft.EntityFrameworkCore;

namespace RuneReign.Api;

// Picks the database: PostgreSQL when a connection is configured (hosting), SQLite otherwise (local development).
public static class DatabaseSetup
{
    // Keep DateTime values as plain timestamps, the same way SQLite stored them, so no value is rejected for its Kind.
    public static void UsePlainTimestamps() => AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

    // Reads ConnectionStrings:Postgres, or DATABASE_URL as most hosts name it.
    public static string? PostgresConnection(IConfiguration config)
    {
        var raw = config.GetConnectionString("Postgres");
        if (string.IsNullOrWhiteSpace(raw)) raw = config["DATABASE_URL"];
        return string.IsNullOrWhiteSpace(raw) ? null : ToNpgsql(raw.Trim());
    }

    // Accepts either "Host=...;Password=..." or the postgresql://user:password@host:port/database link Supabase shows.
    public static string ToNpgsql(string raw)
    {
        var text = raw;
        if (raw.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) || raw.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
        {
            var uri = new Uri(raw);
            var userInfo = uri.UserInfo.Split(':', 2);
            var user = Uri.UnescapeDataString(userInfo[0]);
            var password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : "";
            var database = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/'));
            text = $"Host={uri.Host};Port={(uri.Port > 0 ? uri.Port : 5432)};Database={(database.Length > 0 ? database : "postgres")};Username={user};Password={Quote(password)}";
        }
        // Hosted databases need an encrypted connection; free tiers also allow only a few connections at once.
        if (!Has(text, "SSL Mode") && !Has(text, "SslMode") && !IsLocal(text)) text += ";SSL Mode=Require";
        if (!Has(text, "Maximum Pool Size") && !Has(text, "MaxPoolSize")) text += ";Maximum Pool Size=8";
        return text;
    }

    private static bool Has(string text, string key) => text.Contains(key + "=", StringComparison.OrdinalIgnoreCase);
    private static bool IsLocal(string text) => text.Contains("Host=localhost", StringComparison.OrdinalIgnoreCase) || text.Contains("Host=127.0.0.1");
    private static string Quote(string value) => value.IndexOfAny([';', '=', '\'', '"', ' ']) >= 0 ? "'" + value.Replace("'", "''") + "'" : value;
}
