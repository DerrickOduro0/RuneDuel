using Microsoft.EntityFrameworkCore;
using RuneReign.Domain;

namespace RuneReign.Api;

// Works out a player's attribute totals from the gear they are wearing.
// Values are always in this order: power, guard, vigor, focus, grit, edge, pierce, insight, resolve.
public static class Equipment
{
    public static readonly string[] Keys = ["power", "guard", "vigor", "focus", "grit", "edge", "pierce", "insight", "resolve"];
    public const int Focus = 3;
    public const int Insight = 7;

    public static string ImageUrl(ItemDefinition item) =>
        string.IsNullOrWhiteSpace(item.Image) ? $"/items/{item.Key}.png" : item.Image;

    public static int[] Stats(ItemDefinition item) =>
        [item.Power, item.Guard, item.Vigor, item.Focus, item.Grit, item.Edge, item.Pierce, item.Insight, item.Resolve];

    public static object StatsObject(int[] v)
    {
        var padded = Pad(v);
        return Keys.Select((key, i) => (key, value: padded[i])).ToDictionary(x => x.key, x => x.value);
    }

    public static async Task<(int[] Raw, int[] Capped)> Totals(RuneDbContext db, Guid playerId)
    {
        var worn = await db.PlayerItems.AsNoTracking()
            .Where(x => x.PlayerId == playerId && x.Equipped)
            .Join(db.Items.AsNoTracking(), owned => owned.ItemKey, item => item.Key, (owned, item) => item)
            .ToListAsync();
        var raw = new int[Keys.Length];
        foreach (var item in worn)
        {
            var stats = Stats(item);
            for (var i = 0; i < raw.Length; i++) raw[i] += Math.Max(0, stats[i]);
        }
        var caps = await db.Attributes.AsNoTracking().ToDictionaryAsync(x => x.Key, x => x.Cap);
        var capped = raw.Select((value, i) => Math.Min(value, caps.GetValueOrDefault(Keys[i], 0))).ToArray();
        return (raw, capped);
    }

    public static string Encode(int[] values) => string.Join(",", Pad(values));

    // Older duels stored fewer values; missing ones count as zero.
    public static int[] Decode(string? text)
    {
        var parts = (text ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries);
        var values = new int[Keys.Length];
        for (var i = 0; i < values.Length && i < parts.Length; i++)
            values[i] = int.TryParse(parts[i], out var value) ? value : 0;
        return values;
    }

    private static int[] Pad(int[] values)
    {
        var padded = new int[Keys.Length];
        Array.Copy(values, padded, Math.Min(values.Length, padded.Length));
        return padded;
    }

    public static CombatBonuses ToBonuses(int[] v)
    {
        var p = Pad(v);
        return new(p[0], p[1], p[2], p[4], p[5], p[6], p[8]);
    }
}
