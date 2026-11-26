using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using RuneReign.Domain;

namespace RuneReign.Api;

// A Forge recipe: what it makes (an item or a talisman) and what it costs in materials.
public sealed class ForgeRecipe
{
    public string Key { get; set; } = "";
    public string ResultType { get; set; } = "item";   // item | talisman
    public string ResultKey { get; set; } = "";
    public string Ingredients { get; set; } = "";       // "iron-ore:5,bone-shard:3"
    public int Coins { get; set; }
    public int MinLevel { get; set; } = 1;
    public bool Enabled { get; set; } = true;
    public int SortOrder { get; set; }
}

public sealed record ForgeSalvageRequest(string Type, string Key);
public sealed record AdminRecipeEdit(string? Key, string? ResultType, string? ResultKey, string? Ingredients, int? Coins, int? MinLevel, bool? Enabled, int? SortOrder);

// The Forge: recipes that turn gathered materials into gear and talismans, and salvage that turns spare gear back into materials.
public static class Forge
{
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

    private static readonly (string Item, int Level)[] ItemRecipes =
    [
        ("emberforged-helm", 3), ("slagglass-charm", 4), ("smoulder-ring", 5),
        ("anvilscale-coat", 8), ("hearthstone-sigil", 10), ("cinderweave-mantle", 12), ("forgefire-frame", 15)
    ];

    // Adds the forge items if missing, and the starting recipes once. Recipes an admin removes later stay removed.
    public static void Seed(RuneDbContext db)
    {
        var keys = db.Items.Select(x => x.Key).ToHashSet();
        foreach (var item in ForgedItems.Where(x => !keys.Contains(x.Key))) db.Items.Add(item);
        if (!db.GameSettings.Any(x => x.Key == "ForgeSeeded"))
        {
            var order = 0;
            foreach (var (item, level) in ItemRecipes)
                db.ForgeRecipes.Add(new ForgeRecipe { Key = item, ResultType = "item", ResultKey = item, Ingredients = Materials.ItemIngredients[item], MinLevel = level, SortOrder = order++ });
            foreach (var t in db.Talismans.OrderBy(x => x.SortOrder).ToList())
                db.ForgeRecipes.Add(new ForgeRecipe { Key = $"talisman-{t.Key}", ResultType = "talisman", ResultKey = t.Key, Ingredients = Materials.TalismanIngredients(t.Rarity), MinLevel = 2, SortOrder = 100 + order++ });
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
            if (me.Coins < recipe.Coins) return Error("coins", "You do not have enough coins.", 400);
            if (!await Materials.Take(db, me.Id, Materials.Parse(recipe.Ingredients)))
                return Error("materials", "You do not have enough materials yet.", 400);
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
            var isTalisman = body.Type == "talisman";
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
            var yield = Materials.SalvageYield(rarity, isTalisman);
            await Materials.Give(db, me.Id, yield);
            await db.SaveChangesAsync();
            var view = await View(db, me);
            return Results.Ok(new { gained = await Materials.Describe(db, yield), forge = view });
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
        var materials = await db.Materials.AsNoTracking().OrderBy(x => x.SortOrder).ToListAsync();
        var names = materials.ToDictionary(x => x.Key);
        var bag = await Materials.Bag(db, me.Id);

        object? Result(ForgeRecipe r)
        {
            if (r.ResultType == "item" && items.TryGetValue(r.ResultKey, out var i))
                return new { type = "item", i.Key, i.Name, i.Kind, i.Icon, i.Description, i.Rarity, imageUrl = Equipment.ImageUrl(i), stats = Equipment.StatsObject(Equipment.Stats(i)) };
            if (r.ResultType == "talisman" && talismans.TryGetValue(r.ResultKey, out var t) && t.Enabled)
                return new { type = "talisman", t.Key, t.Name, t.Icon, description = t.Effect, cost = t.Drawback, t.Rarity, active = Talismans.IsActive(t.Key), charges = Talismans.Charges(t.Key) };
            return null;
        }

        var list = recipes.Select(r => new { r, result = Result(r) }).Where(x => x.result is not null).Select(x =>
        {
            var r = x.r;
            var owned = r.ResultType == "item" ? ownedItems.Any(o => o.ItemKey == r.ResultKey) : ownedTalismans.Any(o => o.TalismanKey == r.ResultKey);
            var need = Materials.Parse(r.Ingredients);
            var ingredients = need.Select(n => new
            {
                key = n.Key, need = n.Count, have = bag.GetValueOrDefault(n.Key),
                name = names.GetValueOrDefault(n.Key)?.Name ?? n.Key, icon = names.GetValueOrDefault(n.Key)?.Icon ?? "◆", rarity = names.GetValueOrDefault(n.Key)?.Rarity ?? "common"
            }).ToList();
            var missing = new List<string>();
            if (level < r.MinLevel) missing.Add($"Level {r.MinLevel}");
            foreach (var n in ingredients.Where(n => n.have < n.need)) missing.Add($"{n.need - n.have} more {n.name}");
            if (me.Coins < r.Coins) missing.Add($"{r.Coins - me.Coins} more coins");
            return new { key = r.Key, result = x.result, ingredients, coins = r.Coins, minLevel = r.MinLevel, owned, canCraft = !owned && missing.Count == 0, missing };
        }).ToList();

        var salvageItems = ownedItems.Where(o => !o.Equipped && items.TryGetValue(o.ItemKey, out var i) && !i.IsStarter)
            .Select(o => { var i = items[o.ItemKey]; return new { type = "item", key = i.Key, name = i.Name, icon = i.Icon, rarity = i.Rarity, kind = i.Kind, imageUrl = Equipment.ImageUrl(i), description = i.Description, gives = Materials.SalvageYield(i.Rarity, false).Select(y => new { key = y.Key, count = y.Count, name = names.GetValueOrDefault(y.Key)?.Name ?? y.Key, icon = names.GetValueOrDefault(y.Key)?.Icon ?? "◆" }) }; });
        var salvageTalismans = ownedTalismans.Where(o => !o.Equipped && talismans.ContainsKey(o.TalismanKey))
            .Select(o => { var t = talismans[o.TalismanKey]; return new { type = "talisman", key = t.Key, name = t.Name, icon = t.Icon, rarity = t.Rarity, kind = "talisman", imageUrl = (string?)null, description = t.Effect, gives = Materials.SalvageYield(t.Rarity, true).Select(y => new { key = y.Key, count = y.Count, name = names.GetValueOrDefault(y.Key)?.Name ?? y.Key, icon = names.GetValueOrDefault(y.Key)?.Icon ?? "◆" }) }; });

        return new
        {
            coins = me.Coins,
            level,
            materials = materials.Select(m => Materials.Dto(m, bag.GetValueOrDefault(m.Key))),
            recipes = list,
            salvage = salvageItems.Cast<object>().Concat(salvageTalismans).ToList()
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
            var problem = Apply(recipe, edit, (await db.Materials.Select(x => x.Key).ToListAsync()).ToHashSet());
            if (problem is not null) return Error("invalid", problem, 400);
            db.ForgeRecipes.Add(recipe);
            await db.SaveChangesAsync();
            return Results.Ok(await AdminList(db));
        });

        admin.MapPut("/forge/recipes/{key}", async (string key, AdminRecipeEdit edit, RuneDbContext db) =>
        {
            var recipe = await db.ForgeRecipes.FindAsync(key);
            if (recipe is null) return Error("not_found", "Recipe not found.", 404);
            var problem = Apply(recipe, edit, (await db.Materials.Select(x => x.Key).ToListAsync()).ToHashSet());
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

    private static string? Apply(ForgeRecipe r, AdminRecipeEdit e, HashSet<string> known)
    {
        if (e.Coins is < 0) return "Coins cannot be negative.";
        if (e.MinLevel is < 1 or > Progression.MaxLevel) return $"Level must be between 1 and {Progression.MaxLevel}.";
        if (e.Ingredients is not null)
        {
            var parsed = Materials.Parse(e.Ingredients);
            if (parsed.Count == 0) return "Give at least one ingredient, like iron-ore:5.";
            var unknown = parsed.Where(x => !known.Contains(x.Key)).Select(x => x.Key).ToList();
            if (unknown.Count > 0) return $"Unknown material: {string.Join(", ", unknown)}.";
            r.Ingredients = Materials.Format(parsed);
        }
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
            r.Key, r.ResultType, r.ResultKey, r.Ingredients, r.Coins, r.MinLevel, r.Enabled, r.SortOrder,
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
            materials = (await db.Materials.AsNoTracking().OrderBy(x => x.SortOrder).ToListAsync()).Select(m => new { m.Key, m.Name, m.Icon, m.Rarity, m.Sources, m.Hint, m.Description }),
            materialsHeld = await db.PlayerMaterials.AsNoTracking().GroupBy(x => x.MaterialKey).Select(g => new { key = g.Key, count = g.Sum(x => x.Count) }).ToListAsync()
        };
    }

    private static async Task<Player?> CurrentPlayer(ClaimsPrincipal user, RuneDbContext db) =>
        Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id) && await db.Players.FindAsync(id) is { Banned: false } player ? player : null;
    private static IResult NotFound() => Error("not_found", "Player not found.", 404);
    private static IResult Error(string code, string message, int status) => Results.Json(new { error = new { code, message } }, statusCode: status);
}
