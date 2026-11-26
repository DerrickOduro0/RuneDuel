using Microsoft.EntityFrameworkCore;

namespace RuneReign.Api;

// A crafting material: hides, ores, crystals and the like. Gathered from duels now; exploration and
// dungeons are listed as sources so those modes can drop them when they arrive.
public sealed class MaterialDefinition
{
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public string Icon { get; set; } = "◆";
    public string Rarity { get; set; } = "common";   // common | rare | epic | legendary
    public string Description { get; set; } = "";
    public string Sources { get; set; } = "";         // comma list: duel, exploration, dungeon
    public string Hint { get; set; } = "";            // where exactly, in words ("Ranked wins", "Ashen caves")
    public int SortOrder { get; set; }
}

// How many of one material a player holds.
public sealed class PlayerMaterial
{
    public Guid PlayerId { get; set; }
    public string MaterialKey { get; set; } = "";
    public int Count { get; set; }
}

public sealed record MaterialStack(string Key, int Count);

public static class Materials
{
    public static readonly string[] LiveSources = ["duel"];   // add "exploration", "dungeon" when those modes ship

    private static readonly MaterialDefinition[] Starting =
    [
        new() { Key = "iron-ore", Name = "Iron Ore", Icon = "🪨", Rarity = "common", Sources = "duel,exploration", Hint = "Any finished ranked or casual duel; mined while exploring", Description = "Rough ore with veins of rust. The base of most forged gear.", SortOrder = 1 },
        new() { Key = "bone-shard", Name = "Bone Shard", Icon = "🦴", Rarity = "common", Sources = "duel,dungeon", Hint = "Ranked and casual duels; dungeon crypts", Description = "Splintered bone that still hums with old magic.", SortOrder = 2 },
        new() { Key = "ash-thread", Name = "Ash Thread", Icon = "🧵", Rarity = "common", Sources = "duel,exploration", Hint = "Casual and ranked duels; burnt fields", Description = "Grey thread spun from cooled cinders. Light and tough.", SortOrder = 3 },
        new() { Key = "mana-dew", Name = "Mana Dew", Icon = "💧", Rarity = "common", Sources = "duel,exploration", Hint = "Beating the Shade; dawn meadows", Description = "Beads of condensed mana gathered before sunrise.", SortOrder = 4 },
        new() { Key = "glowcap", Name = "Glowcap", Icon = "🍄", Rarity = "common", Sources = "exploration", Hint = "Damp forests and caves", Description = "A mushroom that glows faintly in the dark.", SortOrder = 5 },
        new() { Key = "heartwood", Name = "Heartwood", Icon = "🪵", Rarity = "rare", Sources = "exploration", Hint = "The oldest trees of the Elderwood", Description = "Dense wood from the core of an ancient tree. Holds warmth.", SortOrder = 6 },
        new() { Key = "raven-feather", Name = "Raven Feather", Icon = "🪶", Rarity = "rare", Sources = "duel,exploration", Hint = "Rare from ranked wins; cliffside nests", Description = "A black feather that never quite settles.", SortOrder = 7 },
        new() { Key = "ember-scale", Name = "Ember Scale", Icon = "🔥", Rarity = "rare", Sources = "duel,dungeon", Hint = "Rare from ranked wins; fire dungeons", Description = "A scale shed by a fire drake, still warm to the touch.", SortOrder = 8 },
        new() { Key = "frost-crystal", Name = "Frost Crystal", Icon = "❄️", Rarity = "rare", Sources = "exploration,dungeon", Hint = "Frozen peaks; ice dungeons", Description = "A crystal that frosts the air around it.", SortOrder = 9 },
        new() { Key = "shade-silk", Name = "Shade Silk", Icon = "🕸️", Rarity = "rare", Sources = "duel", Hint = "Beating the hard Shade", Description = "Silk pulled from the Shade's cloak as it fades.", SortOrder = 10 },
        new() { Key = "rune-tablet", Name = "Rune Tablet", Icon = "📜", Rarity = "rare", Sources = "duel,dungeon", Hint = "Each level up; rare from ranked wins; dungeon vaults", Description = "A cracked tablet carved with an old rune. Talismans are bound with these.", SortOrder = 11 },
        new() { Key = "moonstone", Name = "Moonstone", Icon = "🌙", Rarity = "epic", Sources = "exploration,dungeon", Hint = "Night-only spots while exploring; dungeon bosses", Description = "Pale stone that brightens under the moon.", SortOrder = 12 },
        new() { Key = "wyrm-hide", Name = "Wyrm Hide", Icon = "🐉", Rarity = "epic", Sources = "dungeon", Hint = "Wyrm lairs deep in dungeons", Description = "Thick hide from a wyrm. Turns blades and spells alike.", SortOrder = 13 },
        new() { Key = "starsteel", Name = "Starsteel Ingot", Icon = "⭐", Rarity = "legendary", Sources = "dungeon", Hint = "Dungeon final bosses only", Description = "Metal from a fallen star. Only the greatest gear needs it.", SortOrder = 14 }
    ];

    // Starting recipes: rare gear and talismans use materials duels already give; epic gear waits for dungeons and exploration.
    public static readonly Dictionary<string, string> ItemIngredients = new()
    {
        ["emberforged-helm"] = "iron-ore:5,bone-shard:3,ember-scale:1",
        ["slagglass-charm"] = "iron-ore:3,ash-thread:3,rune-tablet:1",
        ["smoulder-ring"] = "iron-ore:4,ember-scale:2,raven-feather:1",
        ["anvilscale-coat"] = "iron-ore:8,bone-shard:6,wyrm-hide:2",
        ["hearthstone-sigil"] = "heartwood:3,mana-dew:4,moonstone:1",
        ["cinderweave-mantle"] = "ash-thread:8,shade-silk:3,ember-scale:3",
        ["forgefire-frame"] = "iron-ore:10,moonstone:2,starsteel:1"
    };

    public static string TalismanIngredients(string rarity) => rarity switch
    {
        "epic" => "mana-dew:4,rune-tablet:2,raven-feather:2,moonstone:1",
        "rare" => "mana-dew:3,bone-shard:3,rune-tablet:1,raven-feather:1",
        _ => "mana-dew:3,bone-shard:3,rune-tablet:1"
    };

    // What salvaging gives back, by rarity of the thing salvaged.
    public static List<MaterialStack> SalvageYield(string rarity, bool talisman) => (rarity, talisman) switch
    {
        (_, true) => [new("mana-dew", 2), new("rune-tablet", 1)],
        ("epic", _) => [new("iron-ore", 5), new("bone-shard", 3), new("ember-scale", 1)],
        ("rare", _) => [new("iron-ore", 3), new("ash-thread", 2)],
        _ => [new("iron-ore", 2), new("ash-thread", 1)]
    };

    // ---- ingredient strings ("iron-ore:5,bone-shard:3") ----
    public static List<MaterialStack> Parse(string? text) =>
        (text ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => part.Split(':', 2, StringSplitOptions.TrimEntries))
            .Where(bits => bits.Length == 2 && bits[0].Length > 0 && int.TryParse(bits[1], out var n) && n > 0)
            .GroupBy(bits => bits[0].ToLowerInvariant())
            .Select(g => new MaterialStack(g.Key, g.Sum(bits => int.Parse(bits[1]))))
            .ToList();

    public static string Format(IEnumerable<MaterialStack> stacks) => string.Join(",", stacks.Where(x => x.Count > 0).Select(x => $"{x.Key}:{x.Count}"));

    // ---- duel drops ----
    // Each finished duel rolls a few times on a table. Friend duels give nothing, so two accounts cannot farm each other.
    public static List<MaterialStack> DuelDrops(CombatMatch match, Guid playerId, Guid? winnerId, int levelsGained)
    {
        var won = winnerId == playerId;
        var drops = new List<MaterialStack>();
        void Add(string key, int count) { if (count > 0) drops.Add(new(key, count)); }
        var rng = Random.Shared;

        if (match.ShadeDifficulty is not null)
        {
            if (won && match.ShadeDifficulty == "hard") { Add("mana-dew", 2); if (rng.NextDouble() < 0.5) Add("shade-silk", 1); }
            else if (won && match.ShadeDifficulty == "easy") Add("mana-dew", 1);
        }
        else if (match.Mode == DuelMode.Ranked)
        {
            Add("iron-ore", won ? 3 : 1);
            Add("bone-shard", won ? 2 : 1);
            if (won)
            {
                var roll = rng.NextDouble();
                if (roll < 0.35) Add("ember-scale", 1);
                else if (roll < 0.6) Add("raven-feather", 1);
                else if (roll < 0.75) Add("rune-tablet", 1);
            }
        }
        else if (match.Mode == DuelMode.Casual)
        {
            Add(rng.NextDouble() < 0.5 ? "iron-ore" : "ash-thread", won ? 2 : 1);
            if (won) Add("ash-thread", 1);
        }
        if (levelsGained > 0) Add("rune-tablet", levelsGained);
        return drops.GroupBy(x => x.Key).Select(g => new MaterialStack(g.Key, g.Sum(x => x.Count))).ToList();
    }

    // ---- the player's bag ----
    public static async Task Give(RuneDbContext db, Guid playerId, IEnumerable<MaterialStack> stacks)
    {
        foreach (var stack in stacks.Where(x => x.Count > 0))
        {
            var row = await db.PlayerMaterials.FindAsync(playerId, stack.Key)
                ?? db.PlayerMaterials.Local.FirstOrDefault(x => x.PlayerId == playerId && x.MaterialKey == stack.Key);
            if (row is null) db.PlayerMaterials.Add(new PlayerMaterial { PlayerId = playerId, MaterialKey = stack.Key, Count = stack.Count });
            else row.Count += stack.Count;
        }
    }

    public static async Task<Dictionary<string, int>> Bag(RuneDbContext db, Guid playerId) =>
        await db.PlayerMaterials.AsNoTracking().Where(x => x.PlayerId == playerId && x.Count > 0).ToDictionaryAsync(x => x.MaterialKey, x => x.Count);

    // Takes the stacks if the player has all of them. Returns false (and changes nothing) otherwise.
    public static async Task<bool> Take(RuneDbContext db, Guid playerId, List<MaterialStack> cost)
    {
        var keys = cost.Select(x => x.Key).ToList();
        var rows = await db.PlayerMaterials.Where(x => x.PlayerId == playerId && keys.Contains(x.MaterialKey)).ToListAsync();
        if (cost.Any(c => (rows.FirstOrDefault(r => r.MaterialKey == c.Key)?.Count ?? 0) < c.Count)) return false;
        foreach (var c in cost) rows.First(r => r.MaterialKey == c.Key).Count -= c.Count;
        return true;
    }

    public static object Dto(MaterialDefinition m, int count = 0) => new
    {
        m.Key, m.Name, m.Icon, m.Rarity, m.Description, m.Hint, count,
        sources = m.Sources.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => new { key = s, live = LiveSources.Contains(s) })
    };

    // Kept in memory for the duel result screen, which is built without a database call.
    private static Dictionary<string, MaterialDefinition> catalog = new();
    public static void Load(RuneDbContext db) => catalog = db.Materials.AsNoTracking().ToDictionary(x => x.Key);
    public static List<object> DescribeCached(IEnumerable<MaterialStack> stacks) =>
        stacks.Where(x => catalog.ContainsKey(x.Key)).Select(x => (object)new { key = x.Key, name = catalog[x.Key].Name, icon = catalog[x.Key].Icon, rarity = catalog[x.Key].Rarity, count = x.Count }).ToList();

    public static async Task<List<object>> Describe(RuneDbContext db, IEnumerable<MaterialStack> stacks)
    {
        var defs = await db.Materials.AsNoTracking().ToDictionaryAsync(x => x.Key);
        return stacks.Where(x => defs.ContainsKey(x.Key)).Select(x => (object)new { key = x.Key, name = defs[x.Key].Name, icon = defs[x.Key].Icon, rarity = defs[x.Key].Rarity, count = x.Count }).ToList();
    }

    // Adds missing materials (never overwrites an admin's edits), and gives the Forge's recipes ingredients once.
    public static void Seed(RuneDbContext db)
    {
        var have = db.Materials.Select(x => x.Key).ToHashSet();
        foreach (var m in Starting.Where(x => !have.Contains(x.Key))) db.Materials.Add(m);
        if (!db.GameSettings.Any(x => x.Key == "ForgeMaterialsSeeded"))
        {
            var talismans = db.Talismans.AsNoTracking().ToDictionary(x => x.Key);
            foreach (var r in db.ForgeRecipes.ToList())
            {
                if (r.ResultType == "item" && ItemIngredients.TryGetValue(r.ResultKey, out var mix)) r.Ingredients = mix;
                else if (r.ResultType == "talisman") r.Ingredients = TalismanIngredients(talismans.GetValueOrDefault(r.ResultKey)?.Rarity ?? "common");
                else if (string.IsNullOrWhiteSpace(r.Ingredients)) r.Ingredients = "iron-ore:5";
            }
            db.GameSettings.Add(new GameSetting { Key = "ForgeMaterialsSeeded", Value = "1", Description = "Forge recipes were given material ingredients. Leave as 1." });
            // The old Rune Dust / Ember Shard settings no longer do anything.
            var old = new[] { "DustRankedWin", "DustRankedLoss", "DustCasualWin", "DustCasualLoss", "DustShadeHardWin", "DustShadeEasyWin", "ShardRankedWin", "ShardLevelUp", "SalvageCommonDust", "SalvageRareDust", "SalvageRareShards", "SalvageEpicDust", "SalvageEpicShards" };
            db.GameSettings.RemoveRange(db.GameSettings.Where(x => old.Contains(x.Key)).ToList());
        }
        db.SaveChanges();
    }
}
