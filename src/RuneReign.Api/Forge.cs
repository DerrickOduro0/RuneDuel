using System.Security.Claims;
using Microsoft.EntityFrameworkCore;

namespace RuneReign.Api;

// A Forge recipe: what it makes (an item or a talisman) and what it costs in materials.
public sealed class ForgeRecipe
{
    public string Key { get; set; } = "";
    public string ResultType { get; set; } = "item";   // item | talisman
    public string ResultKey { get; set; } = "";
    public int Dust { get; set; }
    public int Shards { get; set; }
    public int Coins { get; set; }
    public int MinLevel { get; set; } = 1;
    public bool Enabled { get; set; } = true;
    public int SortOrder { get; set; }
}

public sealed record ForgeSalvageRequest(string Type, string Key);
public sealed record AdminRecipeEdit(string? Key, string? ResultType, string? ResultKey, int? Dust, int? Shards, int? Coins, int? MinLevel, bool? Enabled, int? SortOrder);

public sealed record ForgeSettings(
    int DustRankedWin, int DustRankedLoss, int DustCasualWin, int DustCasualLoss, int DustShadeHardWin, int DustShadeEasyWin,
    int ShardRankedWin, int ShardLevelUp,
    int SalvageCommonDust, int SalvageRareDust, int SalvageRareShards, int SalvageEpicDust, int SalvageEpicShards);

// The Forge: Rune Dust and Ember Shards drop from duels and from salvaging, and are spent on recipes.
public static class Forge
{
    public static ForgeSettings Settings { get; private set; } = Defaults;
    public static ForgeSettings Defaults => new(12, 5, 8, 3, 4, 1, 1, 1, 15, 40, 1, 100, 3);

    public static readonly (string Key, string Value, string Description)[] SettingRows =
    [
        ("DustRankedWin", "12", "Rune Dust for winning a ranked duel."),
        ("DustRankedLoss", "5", "Rune Dust for finishing a ranked duel you lost (a draw gives this too)."),
        ("DustCasualWin", "8", "Rune Dust for winning a casual duel."),
        ("DustCasualLoss", "3", "Rune Dust for finishing a casual duel you lost (a draw gives this too)."),
        ("DustShadeHardWin", "4", "Rune Dust for beating the hard Shade."),
        ("DustShadeEasyWin", "1", "Rune Dust for beating the easy Shade."),
        ("ShardRankedWin", "1", "Ember Shards for winning a ranked duel."),
        ("ShardLevelUp", "1", "Ember Shards for every level gained."),
        ("SalvageCommonDust", "15", "Rune Dust from salvaging a common item or talisman."),
        ("SalvageRareDust", "40", "Rune Dust from salvaging a rare item or talisman."),
        ("SalvageRareShards", "1", "Ember Shards from salvaging a rare item or talisman."),
        ("SalvageEpicDust", "100", "Rune Dust from salvaging an epic item or talisman."),
        ("SalvageEpicShards", "3", "Ember Shards from salvaging an epic item or talisman.")
    ];

    public static void Load(Dictionary<string, string> s)
    {
        var d = Defaults;
        int G(string key, int fallback) => s.TryGetValue(key, out var raw) && int.TryParse(raw, out var v) ? Math.Max(0, v) : fallback;
        Settings = new ForgeSettings(
            G("DustRankedWin", d.DustRankedWin), G("DustRankedLoss", d.DustRankedLoss), G("DustCasualWin", d.DustCasualWin), G("DustCasualLoss", d.DustCasualLoss),
            G("DustShadeHardWin", d.DustShadeHardWin), G("DustShadeEasyWin", d.DustShadeEasyWin), G("ShardRankedWin", d.ShardRankedWin), G("ShardLevelUp", d.ShardLevelUp),
            G("SalvageCommonDust", d.SalvageCommonDust), G("SalvageRareDust", d.SalvageRareDust), G("SalvageRareShards", d.SalvageRareShards),
            G("SalvageEpicDust", d.SalvageEpicDust), G("SalvageEpicShards", d.SalvageEpicShards));
    }

    // Materials one player gets for a finished duel. Friend duels give none, so two accounts cannot farm each other.
    public static (int Dust, int Shards) DuelDrops(CombatMatch match, Guid playerId, Guid? winnerId)
    {
        var s = Settings;
        var won = winnerId == playerId;
        if (match.ShadeDifficulty is not null)
            return !won ? (0, 0) : match.ShadeDifficulty == "hard" ? (s.DustShadeHardWin, 0) : match.ShadeDifficulty == "easy" ? (s.DustShadeEasyWin, 0) : (0, 0);
        if (match.Mode == DuelMode.Friend) return (0, 0);
        if (match.Mode == DuelMode.Casual) return (won ? s.DustCasualWin : s.DustCasualLoss, 0);
        return won ? (s.DustRankedWin, s.ShardRankedWin) : (s.DustRankedLoss, 0);
    }

    public static (int Dust, int Shards) SalvageValue(string rarity) => rarity switch
    {
        "epic" => (Settings.SalvageEpicDust, Settings.SalvageEpicShards),
        "rare" => (Settings.SalvageRareDust, Settings.SalvageRareShards),
        _ => (Settings.SalvageCommonDust, 0)
    };

    // Forge-only gear: never sold in the shop. Same attribute budget as shop gear of the same rarity, so crafting is a path, not an edge.
    private static readonly ItemDefinition[] ForgedItems =
    [
        new() { Key = "emberforged-helm", Name = "Emberforged helm", Kind = "helmet", Icon = "♜", Description = "Hammered in the Forge's first fire.", Power = 1, Focus = 1, Rarity = "rare", SortOrder = 60 },
        new() { Key = "slagglass-charm", Name = "Slag-glass charm", Kind = "charm", Icon = "✧", Description = "Cooled from the Forge's spill. Sharp at every edge.", Pierce = 1, Guard = 1, Rarity = "rare", SortOrder = 61 },
        new() { Key = "smoulder-ring", Name = "Smoulder ring", Kind = "ring", Icon = "◎", Description = "Never quite cold. Shows you the opening.", Edge = 1, Insight = 1, Rarity = "rare", SortOrder = 62 },
        new() { Key = "anvilscale-coat", Name = "Anvil-scale coat", Kind = "armor", Icon = "▣", Description = "Scales struck one by one on the great anvil.", Guard = 1, Grit = 1, Vigor = 1, Rarity = "epic", SortOrder = 63 },
        new() { Key = "hearthstone-sigil", Name = "Hearthstone sigil", Kind = "sigil", Icon = "❖", Description = "Holds the warmth of a hearth that never goes out.", Vigor = 1, Resolve = 1, Focus = 1, Rarity = "epic", SortOrder = 64 },
        new() { Key = "cinderweave-mantle", Name = "Cinderweave mantle", Kind = "mantle", Icon = "☾", Description = "Thread spun from ash and ember.", Power = 1, Pierce = 1, Resolve = 1, Rarity = "epic", SortOrder = 65 },
        new() { Key = "forgefire-frame", Name = "Forgefire frame", Kind = "frame", Icon = "◆", Description = "A molten frame for your rune dice. Cosmetic.", Rarity = "epic", SortOrder = 66 }
    ];

    private static readonly (string Item, int Dust, int Shards, int Level)[] ItemRecipes =
    [
        ("emberforged-helm", 120, 2, 3), ("slagglass-charm", 130, 2, 4), ("smoulder-ring", 150, 3, 5),
        ("anvilscale-coat", 300, 6, 8), ("hearthstone-sigil", 320, 7, 10), ("cinderweave-mantle", 360, 8, 12), ("forgefire-frame", 400, 10, 15)
    ];

    // Adds the forge items if missing, and the starting recipes once. Recipes an admin removes later stay removed.
    public static void Seed(RuneDbContext db)
    {
        var keys = db.Items.Select(x => x.Key).ToHashSet();
        foreach (var item in ForgedItems.Where(x => !keys.Contains(x.Key))) db.Items.Add(item);
        if (!db.GameSettings.Any(x => x.Key == "ForgeSeeded"))
        {
            var order = 0;
            foreach (var (item, dust, shards, level) in ItemRecipes)
                db.ForgeRecipes.Add(new ForgeRecipe { Key = item, ResultType = "item", ResultKey = item, Dust = dust, Shards = shards, MinLevel = level, SortOrder = order++ });
            foreach (var t in db.Talismans.OrderBy(x => x.SortOrder).ToList())
                db.ForgeRecipes.Add(new ForgeRecipe { Key = $"talisman-{t.Key}", ResultType = "talisman", ResultKey = t.Key, Dust = Math.Max(60, t.PriceCoins / 4), Shards = 1, MinLevel = 2, SortOrder = 100 + order++ });
            db.GameSettings.Add(new GameSetting { Key = "ForgeSeeded", Value = "1", Description = "The Forge's starting recipes were added. Leave as 1." });
        }
        db.SaveChanges();
    }

    public static void MapForgeEndpoints(this WebApplication app)
    {
        var forge = app.MapGroup("/api/v1/forge").RequireAuthorization();

        forge.MapGet("", async (ClaimsPrincipal user, RuneDbContext db, ServerSettings server) =>
        {
            var me = await CurrentPlayer(user, db);
            if (me is null) return NotFound();
            // A brand-new player gets their starting coins and gear first, as on every other screen.
            await RealmEndpoints.EnsureRealm(db, me, server);
            return Results.Ok(await View(db, me));
        });

        forge.MapPost("/craft/{key}", async (string key, ClaimsPrincipal user, RuneDbContext db) =>
        {
            var me = await CurrentPlayer(user, db);
            if (me is null) return NotFound();
            var recipe = await db.ForgeRecipes.FirstOrDefaultAsync(x => x.Key == key && x.Enabled);
            if (recipe is null) return Error("no_recipe", "That recipe is not available.", 404);
            var level = Progression.LevelFor(me.Xp);
            if (level < recipe.MinLevel) return Error("level", $"Reach level {recipe.MinLevel} to forge this.", 400);
            if (recipe.ResultType == "item")
            {
                if (!await db.Items.AnyAsync(x => x.Key == recipe.ResultKey)) return Error("no_item", "That item no longer exists.", 404);
                if (await db.PlayerItems.AnyAsync(x => x.PlayerId == me.Id && x.ItemKey == recipe.ResultKey)) return Error("owned", "You already own this.", 409);
            }
            else
            {
                if (!await db.Talismans.AnyAsync(x => x.Key == recipe.ResultKey && x.Enabled)) return Error("no_item", "That talisman is not available.", 404);
                if (await db.PlayerTalismans.AnyAsync(x => x.PlayerId == me.Id && x.TalismanKey == recipe.ResultKey)) return Error("owned", "You already own this.", 409);
            }
            if (me.Dust < recipe.Dust || me.Shards < recipe.Shards || me.Coins < recipe.Coins)
                return Error("materials", "You do not have enough materials yet.", 400);
            me.Dust -= recipe.Dust;
            me.Shards -= recipe.Shards;
            me.Coins -= recipe.Coins;
            if (recipe.ResultType == "item") db.PlayerItems.Add(new PlayerItem { Id = Guid.NewGuid(), PlayerId = me.Id, ItemKey = recipe.ResultKey, AcquiredAtUtc = DateTime.UtcNow });
            else db.PlayerTalismans.Add(new PlayerTalisman { Id = Guid.NewGuid(), PlayerId = me.Id, TalismanKey = recipe.ResultKey, AcquiredAtUtc = DateTime.UtcNow });
            await db.SaveChangesAsync();
            return Results.Ok(await View(db, me));
        });

        forge.MapPost("/salvage", async (ForgeSalvageRequest body, ClaimsPrincipal user, RuneDbContext db) =>
        {
            var me = await CurrentPlayer(user, db);
            if (me is null) return NotFound();
            string rarity;
            if (body.Type == "item")
            {
                var owned = await db.PlayerItems.FirstOrDefaultAsync(x => x.PlayerId == me.Id && x.ItemKey == body.Key);
                var item = await db.Items.AsNoTracking().FirstOrDefaultAsync(x => x.Key == body.Key);
                if (owned is null || item is null) return Error("not_owned", "You do not own that item.", 404);
                if (owned.Equipped) return Error("equipped", "Take it off before salvaging it.", 400);
                if (item.IsStarter) return Error("starter", "Starter gear cannot be salvaged.", 400);
                db.PlayerItems.Remove(owned);
                rarity = item.Rarity;
            }
            else if (body.Type == "talisman")
            {
                var owned = await db.PlayerTalismans.FirstOrDefaultAsync(x => x.PlayerId == me.Id && x.TalismanKey == body.Key);
                var talisman = await db.Talismans.AsNoTracking().FirstOrDefaultAsync(x => x.Key == body.Key);
                if (owned is null || talisman is null) return Error("not_owned", "You do not own that talisman.", 404);
                if (owned.Equipped) return Error("equipped", "Unequip it before salvaging it.", 400);
                db.PlayerTalismans.Remove(owned);
                rarity = talisman.Rarity;
            }
            else return Error("bad_type", "Choose an item or a talisman.", 400);
            var (dust, shards) = SalvageValue(rarity);
            me.Dust += dust;
            me.Shards += shards;
            await db.SaveChangesAsync();
            var view = await View(db, me);
            return Results.Ok(new { gained = new { dust, shards }, forge = view });
        });
    }

    // Everything the Forge screen shows.
    public static async Task<object> View(RuneDbContext db, Player me)
    {
        var level = Progression.LevelFor(me.Xp);
        var recipes = await db.ForgeRecipes.AsNoTracking().Where(x => x.Enabled).OrderBy(x => x.SortOrder).ToListAsync();
        var items = await db.Items.AsNoTracking().ToDictionaryAsync(x => x.Key);
        var talismans = await db.Talismans.AsNoTracking().ToDictionaryAsync(x => x.Key);
        var ownedItems = await db.PlayerItems.AsNoTracking().Where(x => x.PlayerId == me.Id).ToListAsync();
        var ownedTalismans = await db.PlayerTalismans.AsNoTracking().Where(x => x.PlayerId == me.Id).ToListAsync();

        object? Result(ForgeRecipe r)
        {
            if (r.ResultType == "item" && items.TryGetValue(r.ResultKey, out var i))
                return new { type = "item", i.Key, i.Name, i.Kind, i.Icon, i.Description, i.Rarity, imageUrl = Equipment.ImageUrl(i), stats = Equipment.StatsObject(Equipment.Stats(i)) };
            if (r.ResultType == "talisman" && talismans.TryGetValue(r.ResultKey, out var t) && t.Enabled)
                return new { type = "talisman", t.Key, t.Name, t.Icon, description = t.Effect, cost = t.Drawback, t.Rarity };
            return null;
        }

        var list = recipes.Select(r => new { r, result = Result(r) }).Where(x => x.result is not null).Select(x =>
        {
            var r = x.r;
            var owned = r.ResultType == "item" ? ownedItems.Any(o => o.ItemKey == r.ResultKey) : ownedTalismans.Any(o => o.TalismanKey == r.ResultKey);
            var missing = new List<string>();
            if (level < r.MinLevel) missing.Add($"Level {r.MinLevel}");
            if (me.Dust < r.Dust) missing.Add($"{r.Dust - me.Dust} more Rune Dust");
            if (me.Shards < r.Shards) missing.Add($"{r.Shards - me.Shards} more Ember Shards");
            if (me.Coins < r.Coins) missing.Add($"{r.Coins - me.Coins} more coins");
            return new { key = r.Key, result = x.result, cost = new { dust = r.Dust, shards = r.Shards, coins = r.Coins }, minLevel = r.MinLevel, owned, canCraft = !owned && missing.Count == 0, missing };
        }).ToList();

        var salvageItems = ownedItems.Where(o => !o.Equipped && items.TryGetValue(o.ItemKey, out var i) && !i.IsStarter)
            .Select(o => { var i = items[o.ItemKey]; var v = SalvageValue(i.Rarity); return new { type = "item", key = i.Key, name = i.Name, icon = i.Icon, rarity = i.Rarity, kind = i.Kind, imageUrl = Equipment.ImageUrl(i), dust = v.Dust, shards = v.Shards }; });
        var salvageTalismans = ownedTalismans.Where(o => !o.Equipped && talismans.ContainsKey(o.TalismanKey))
            .Select(o => { var t = talismans[o.TalismanKey]; var v = SalvageValue(t.Rarity); return new { type = "talisman", key = t.Key, name = t.Name, icon = t.Icon, rarity = t.Rarity, kind = "talisman", imageUrl = (string?)null, dust = v.Dust, shards = v.Shards }; });

        var s = Settings;
        return new
        {
            materials = new { dust = me.Dust, shards = me.Shards, coins = me.Coins },
            level,
            recipes = list,
            salvage = salvageItems.Concat(salvageTalismans).ToList(),
            sources = new
            {
                rankedWin = new { dust = s.DustRankedWin, shards = s.ShardRankedWin },
                rankedLoss = new { dust = s.DustRankedLoss },
                casualWin = new { dust = s.DustCasualWin },
                casualLoss = new { dust = s.DustCasualLoss },
                shadeHardWin = new { dust = s.DustShadeHardWin },
                levelUp = new { shards = s.ShardLevelUp }
            }
        };
    }

    // ---- Admin: the recipe list ----
    public static void MapForgeAdmin(RouteGroupBuilder admin)
    {
        admin.MapGet("/forge", async (RuneDbContext db) => Results.Ok(await AdminList(db)));

        admin.MapPost("/forge/recipes", async (AdminRecipeEdit edit, RuneDbContext db) =>
        {
            var type = edit.ResultType == "talisman" ? "talisman" : "item";
            var resultKey = (edit.ResultKey ?? "").Trim();
            var exists = type == "item" ? await db.Items.AnyAsync(x => x.Key == resultKey) : await db.Talismans.AnyAsync(x => x.Key == resultKey);
            if (!exists) return Error("no_result", "Pick an item or talisman that exists.", 400);
            var key = string.IsNullOrWhiteSpace(edit.Key) ? (type == "item" ? resultKey : $"talisman-{resultKey}") : edit.Key.Trim().ToLowerInvariant();
            if (await db.ForgeRecipes.AnyAsync(x => x.Key == key)) return Error("exists", "There is already a recipe for that.", 409);
            var recipe = new ForgeRecipe { Key = key, ResultType = type, ResultKey = resultKey, SortOrder = await db.ForgeRecipes.CountAsync() };
            var problem = Apply(recipe, edit);
            if (problem is not null) return Error("invalid", problem, 400);
            db.ForgeRecipes.Add(recipe);
            await db.SaveChangesAsync();
            return Results.Ok(await AdminList(db));
        });

        admin.MapPut("/forge/recipes/{key}", async (string key, AdminRecipeEdit edit, RuneDbContext db) =>
        {
            var recipe = await db.ForgeRecipes.FindAsync(key);
            if (recipe is null) return Error("not_found", "Recipe not found.", 404);
            var problem = Apply(recipe, edit);
            if (problem is not null) return Error("invalid", problem, 400);
            await db.SaveChangesAsync();
            return Results.Ok(await AdminList(db));
        });

        admin.MapDelete("/forge/recipes/{key}", async (string key, RuneDbContext db) =>
        {
            await db.ForgeRecipes.Where(x => x.Key == key).ExecuteDeleteAsync();
            return Results.Ok(await AdminList(db));
        });
    }

    private static string? Apply(ForgeRecipe r, AdminRecipeEdit e)
    {
        if (e.Dust is < 0 || e.Shards is < 0 || e.Coins is < 0) return "Costs cannot be negative.";
        if (e.MinLevel is < 1 or > Progression.MaxLevel) return $"Level must be between 1 and {Progression.MaxLevel}.";
        if (e.Dust is not null) r.Dust = e.Dust.Value;
        if (e.Shards is not null) r.Shards = e.Shards.Value;
        if (e.Coins is not null) r.Coins = e.Coins.Value;
        if (e.MinLevel is not null) r.MinLevel = e.MinLevel.Value;
        if (e.Enabled is not null) r.Enabled = e.Enabled.Value;
        if (e.SortOrder is not null) r.SortOrder = e.SortOrder.Value;
        return null;
    }

    private static async Task<object> AdminList(RuneDbContext db)
    {
        var items = await db.Items.AsNoTracking().ToDictionaryAsync(x => x.Key);
        var talismans = await db.Talismans.AsNoTracking().ToDictionaryAsync(x => x.Key);
        var crafted = await db.PlayerItems.AsNoTracking().GroupBy(x => x.ItemKey).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count);
        var recipes = (await db.ForgeRecipes.AsNoTracking().OrderBy(x => x.SortOrder).ToListAsync()).Select(r => new
        {
            r.Key, r.ResultType, r.ResultKey, r.Dust, r.Shards, r.Coins, r.MinLevel, r.Enabled, r.SortOrder,
            name = r.ResultType == "item" ? items.GetValueOrDefault(r.ResultKey)?.Name : talismans.GetValueOrDefault(r.ResultKey)?.Name,
            rarity = r.ResultType == "item" ? items.GetValueOrDefault(r.ResultKey)?.Rarity : talismans.GetValueOrDefault(r.ResultKey)?.Rarity,
            inShop = r.ResultType == "item" ? items.GetValueOrDefault(r.ResultKey)?.InShop : null,
            ownedBy = r.ResultType == "item" ? crafted.GetValueOrDefault(r.ResultKey) : 0
        });
        return new
        {
            recipes,
            items = items.Values.OrderBy(x => x.SortOrder).Select(x => new { x.Key, x.Name, x.Kind, x.Rarity, x.InShop }),
            talismans = talismans.Values.OrderBy(x => x.SortOrder).Select(x => new { x.Key, x.Name, x.Rarity }),
            materialsHeld = new { dust = await db.Players.SumAsync(x => (long)x.Dust), shards = await db.Players.SumAsync(x => (long)x.Shards) }
        };
    }

    private static async Task<Player?> CurrentPlayer(ClaimsPrincipal user, RuneDbContext db) =>
        Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id) && await db.Players.FindAsync(id) is { Banned: false } player ? player : null;
    private static IResult NotFound() => Error("not_found", "Player not found.", 404);
    private static IResult Error(string code, string message, int status) => Results.Json(new { error = new { code, message } }, statusCode: status);
}
