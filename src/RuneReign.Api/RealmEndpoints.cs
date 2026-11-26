using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using RuneReign.Domain;

namespace RuneReign.Api;

public sealed record SaveSettingsRequest(bool? ReduceMotion, bool? DamageNumbers, bool? DiceAnimation, bool? CompactHud);

// Everything the realm pages show (wallet, relics, shop, quests, rewards, letters, story) lives in the database.
public static class RealmEndpoints
{
    public static void MapRealmEndpoints(this WebApplication app)
    {
        app.MapGet("/api/v1/content", async (RuneDbContext db, GameRules rules, ServerSettings server) =>
            Results.Ok(await ContentStore.BuildContent(db, rules, server)));

        app.MapGet("/api/v1/me/settings", async (ClaimsPrincipal user, RuneDbContext db) =>
        {
            var player = await CurrentPlayer(user, db);
            if (player is null) return NotFound();
            var row = await db.PlayerSettings.FindAsync(player.Id);
            return Results.Ok(ToDto(row));
        }).RequireAuthorization();

        app.MapPut("/api/v1/me/settings", async (SaveSettingsRequest request, ClaimsPrincipal user, RuneDbContext db) =>
        {
            var player = await CurrentPlayer(user, db);
            if (player is null) return NotFound();
            var row = await db.PlayerSettings.FindAsync(player.Id);
            if (row is null)
            {
                row = new PlayerSettings { PlayerId = player.Id };
                db.PlayerSettings.Add(row);
            }
            if (request.ReduceMotion is bool reduceMotion) row.ReduceMotion = reduceMotion;
            if (request.DamageNumbers is bool damageNumbers) row.DamageNumbers = damageNumbers;
            if (request.DiceAnimation is bool diceAnimation) row.DiceAnimation = diceAnimation;
            if (request.CompactHud is bool compactHud) row.CompactHud = compactHud;
            row.UpdatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync();
            return Results.Ok(ToDto(row));
        }).RequireAuthorization();

        app.MapGet("/api/v1/realm", async (ClaimsPrincipal user, RuneDbContext db, ServerSettings server) =>
        {
            var player = await CurrentPlayer(user, db);
            if (player is null) return NotFound();
            await EnsureRealm(db, player, server);
            return Results.Ok(await BuildRealm(db, player));
        }).RequireAuthorization();

        app.MapPost("/api/v1/realm/shop/{key}/buy", async (string key, ClaimsPrincipal user, RuneDbContext db, ServerSettings server) =>
        {
            var player = await CurrentPlayer(user, db);
            if (player is null) return NotFound();
            await EnsureRealm(db, player, server);
            var item = await db.Items.FindAsync(key);
            if (item is null || !item.InShop) return Error("unknown_item", "That item is not for sale.", 404);
            if (await db.PlayerItems.AnyAsync(x => x.PlayerId == player.Id && x.ItemKey == key))
                return Error("already_owned", "You already own this item.", 409);
            if (player.Coins < item.PriceCoins || player.Gems < item.PriceGems)
                return Error("not_enough_currency", item.PriceGems > 0 ? "You do not have enough gems." : "You do not have enough coins.", 400);
            player.Coins -= item.PriceCoins;
            player.Gems -= item.PriceGems;
            db.PlayerItems.Add(new PlayerItem { Id = Guid.NewGuid(), PlayerId = player.Id, ItemKey = key });
            await db.SaveChangesAsync();
            return Results.Ok(await BuildRealm(db, player, $"{item.Name} added to your collection."));
        }).RequireAuthorization();

        app.MapPost("/api/v1/realm/inventory/{key}/equip", async (string key, ClaimsPrincipal user, RuneDbContext db, ServerSettings server) =>
        {
            var player = await CurrentPlayer(user, db);
            if (player is null) return NotFound();
            await EnsureRealm(db, player, server);
            var owned = await db.PlayerItems.Where(x => x.PlayerId == player.Id).ToListAsync();
            var target = owned.FirstOrDefault(x => x.ItemKey == key);
            var item = await db.Items.FindAsync(key);
            if (target is null || item is null) return Error("not_owned", "You do not own that item.", 404);
            var sameKind = await db.Items.Where(x => x.Kind == item.Kind).Select(x => x.Key).ToListAsync();
            // One item of each kind can be worn at a time.
            foreach (var other in owned.Where(x => sameKind.Contains(x.ItemKey)))
                other.Equipped = other.ItemKey == key;
            await db.SaveChangesAsync();
            return Results.Ok(await BuildRealm(db, player, $"{item.Name} equipped."));
        }).RequireAuthorization();

        app.MapPost("/api/v1/realm/inventory/{key}/unequip", async (string key, ClaimsPrincipal user, RuneDbContext db, ServerSettings server) =>
        {
            var player = await CurrentPlayer(user, db);
            if (player is null) return NotFound();
            var owned = await db.PlayerItems.FirstOrDefaultAsync(x => x.PlayerId == player.Id && x.ItemKey == key);
            var item = await db.Items.FindAsync(key);
            if (owned is null || item is null) return Error("not_owned", "You do not own that item.", 404);
            owned.Equipped = false;
            await db.SaveChangesAsync();
            return Results.Ok(await BuildRealm(db, player, $"{item.Name} taken off."));
        }).RequireAuthorization();

        // Talismans: bought with coins, one equipped at a time.
        app.MapPost("/api/v1/realm/talismans/{key}/buy", async (string key, ClaimsPrincipal user, RuneDbContext db, ServerSettings server) =>
        {
            var player = await CurrentPlayer(user, db);
            if (player is null) return NotFound();
            await EnsureRealm(db, player, server);
            var talisman = await db.Talismans.FindAsync(key);
            if (talisman is null || !talisman.Enabled) return Error("unknown_talisman", "That talisman is not for sale.", 404);
            if (await db.PlayerTalismans.AnyAsync(x => x.PlayerId == player.Id && x.TalismanKey == key))
                return Error("already_owned", "You already own this talisman.", 409);
            if (player.Coins < talisman.PriceCoins) return Error("not_enough_currency", "You do not have enough coins.", 400);
            player.Coins -= talisman.PriceCoins;
            // The first talisman bought is equipped straight away.
            var first = !await db.PlayerTalismans.AnyAsync(x => x.PlayerId == player.Id);
            db.PlayerTalismans.Add(new PlayerTalisman { Id = Guid.NewGuid(), PlayerId = player.Id, TalismanKey = key, Equipped = first });
            await db.SaveChangesAsync();
            return Results.Ok(await BuildRealm(db, player, first ? $"{talisman.Name} bought and equipped." : $"{talisman.Name} added to your talismans."));
        }).RequireAuthorization();

        app.MapPost("/api/v1/realm/talismans/{key}/equip", async (string key, ClaimsPrincipal user, RuneDbContext db, ServerSettings server) =>
        {
            var player = await CurrentPlayer(user, db);
            if (player is null) return NotFound();
            var owned = await db.PlayerTalismans.Where(x => x.PlayerId == player.Id).ToListAsync();
            var talisman = await db.Talismans.FindAsync(key);
            if (talisman is null || owned.All(x => x.TalismanKey != key)) return Error("not_owned", "You do not own that talisman.", 404);
            foreach (var row in owned) row.Equipped = row.TalismanKey == key;
            await db.SaveChangesAsync();
            return Results.Ok(await BuildRealm(db, player, $"{talisman.Name} equipped. It takes effect in your next duel."));
        }).RequireAuthorization();

        app.MapPost("/api/v1/realm/talismans/unequip", async (ClaimsPrincipal user, RuneDbContext db) =>
        {
            var player = await CurrentPlayer(user, db);
            if (player is null) return NotFound();
            await db.PlayerTalismans.Where(x => x.PlayerId == player.Id && x.Equipped).ExecuteUpdateAsync(x => x.SetProperty(t => t.Equipped, false));
            return Results.Ok(await BuildRealm(db, player, "Talisman taken off."));
        }).RequireAuthorization();

        app.MapPost("/api/v1/realm/objectives/{key}/claim", async (string key, ClaimsPrincipal user, RuneDbContext db, ServerSettings server) =>
        {
            var player = await CurrentPlayer(user, db);
            if (player is null) return NotFound();
            await EnsureRealm(db, player, server);
            var objective = await db.Objectives.FindAsync(key);
            if (objective is null) return Error("unknown_objective", "That objective does not exist.", 404);
            var metrics = await Metrics(db, player);
            var period = PeriodKey(objective.Repeat);
            if (await db.ObjectiveClaims.AnyAsync(x => x.PlayerId == player.Id && x.ObjectiveKey == key && x.PeriodKey == period))
                return Error("already_claimed", "You have already claimed this reward.", 409);
            if (metrics.GetValueOrDefault(objective.Metric) < objective.Target)
                return Error("not_complete", "Finish the objective before claiming it.", 400);
            player.Coins += objective.RewardCoins;
            player.Gems += objective.RewardGems;
            db.ObjectiveClaims.Add(new ObjectiveClaim { Id = Guid.NewGuid(), PlayerId = player.Id, ObjectiveKey = key, PeriodKey = period });
            await db.SaveChangesAsync();
            return Results.Ok(await BuildRealm(db, player, $"{objective.Name} claimed: {RewardLabel(objective)}."));
        }).RequireAuthorization();

        app.MapPost("/api/v1/realm/messages/{id:guid}/read", async (Guid id, ClaimsPrincipal user, RuneDbContext db) =>
        {
            var player = await CurrentPlayer(user, db);
            if (player is null) return NotFound();
            var message = await db.PlayerMessages.FirstOrDefaultAsync(x => x.Id == id && x.PlayerId == player.Id);
            if (message is null) return Error("unknown_message", "That letter does not exist.", 404);
            message.ReadAtUtc ??= DateTime.UtcNow;
            await db.SaveChangesAsync();
            return Results.Ok(await BuildRealm(db, player));
        }).RequireAuthorization();

        app.MapPost("/api/v1/realm/story/{number:int}/begin", async (int number, ClaimsPrincipal user, RuneDbContext db) =>
        {
            var player = await CurrentPlayer(user, db);
            if (player is null) return NotFound();
            var chapter = await db.StoryChapters.FindAsync(number);
            if (chapter is null) return Error("unknown_chapter", "That chapter does not exist.", 404);
            if (await TotalMatches(db, player.Id) < chapter.UnlockMatches)
                return Error("chapter_locked", $"Finish {chapter.UnlockMatches} matches to unlock this chapter.", 400);
            player.StoryChapter = Math.Max(player.StoryChapter, number);
            await db.SaveChangesAsync();
            return Results.Ok(await BuildRealm(db, player, $"Chapter {number} begun."));
        }).RequireAuthorization();
    }

    // Gives a player their wallet and welcome letter the first time they reach the realm,
    // and hands out any starter gear they do not have yet (new starters reach existing players too).
    public static async Task EnsureRealm(RuneDbContext db, Player player, ServerSettings server)
    {
        var starters = await db.Items.Where(x => x.IsStarter).OrderBy(x => x.SortOrder).ToListAsync();
        var owned = await db.PlayerItems.Where(x => x.PlayerId == player.Id).ToListAsync();
        var allItems = await db.Items.ToDictionaryAsync(x => x.Key, x => x.Kind);
        var wornKinds = owned.Where(x => x.Equipped && allItems.ContainsKey(x.ItemKey)).Select(x => allItems[x.ItemKey]).ToHashSet();
        var changed = false;
        foreach (var item in starters.Where(x => owned.All(o => o.ItemKey != x.Key)))
        {
            // Wear a starter only if nothing else is in that slot.
            db.PlayerItems.Add(new PlayerItem { Id = Guid.NewGuid(), PlayerId = player.Id, ItemKey = item.Key, Equipped = wornKinds.Add(item.Kind) });
            changed = true;
        }
        if (player.RealmInitializedAtUtc is null)
        {
            player.Coins += server.StartingCoins;
            player.Gems += server.StartingGems;
            db.PlayerMessages.Add(new PlayerMessage
            {
                Id = Guid.NewGuid(),
                PlayerId = player.Id,
                Sender = "The Rune Council",
                Subject = "Welcome to the arena",
                Body = $"Welcome, {player.Username}. Your starter gear is waiting in your inventory. Better helmets, armor, rings and charms can be bought in the shop with the coins you earn."
            });
            player.RealmInitializedAtUtc = DateTime.UtcNow;
            changed = true;
        }
        if (changed) await db.SaveChangesAsync();
    }

    private static async Task<object> BuildRealm(RuneDbContext db, Player player, string? notice = null)
    {
        var items = await db.Items.AsNoTracking().OrderBy(x => x.SortOrder).ToListAsync();
        var owned = await db.PlayerItems.AsNoTracking().Where(x => x.PlayerId == player.Id).ToListAsync();
        var objectives = await db.Objectives.AsNoTracking().OrderBy(x => x.SortOrder).ToListAsync();
        var claims = await db.ObjectiveClaims.AsNoTracking().Where(x => x.PlayerId == player.Id).ToListAsync();
        var messages = await db.PlayerMessages.AsNoTracking().Where(x => x.PlayerId == player.Id).OrderByDescending(x => x.CreatedAtUtc).ToListAsync();
        var chapters = await db.StoryChapters.AsNoTracking().OrderBy(x => x.Number).ToListAsync();
        var metrics = await Metrics(db, player);
        var total = await TotalMatches(db, player.Id);
        var slots = await db.EquipmentSlots.AsNoTracking().OrderBy(x => x.SortOrder).ToListAsync();
        var (rawTotals, cappedTotals) = await Equipment.Totals(db, player.Id);
        var talismanDefs = await db.Talismans.AsNoTracking().Where(x => x.Enabled).OrderBy(x => x.SortOrder).ToListAsync();
        var ownedTalismans = await db.PlayerTalismans.AsNoTracking().Where(x => x.PlayerId == player.Id).ToListAsync();

        return new
        {
            notice,
            wallet = new { coins = player.Coins, gems = player.Gems },
            inventory = owned
                .Join(items, o => o.ItemKey, i => i.Key, (o, i) => new { Item = i, o.Equipped })
                .OrderBy(x => x.Item.SortOrder)
                .Select(x => new { x.Item.Key, x.Item.Name, x.Item.Kind, x.Item.Icon, image = Equipment.ImageUrl(x.Item), x.Item.Description, x.Item.Rarity, x.Equipped, stats = Equipment.StatsObject(Equipment.Stats(x.Item)) }),
            shop = items.Where(x => x.InShop).OrderBy(x => x.PriceGems > 0).ThenBy(x => x.PriceCoins).Select(x => new { x.Key, x.Name, x.Kind, x.Icon, image = Equipment.ImageUrl(x), x.Description, x.Rarity, x.PriceCoins, x.PriceGems, owned = owned.Any(o => o.ItemKey == x.Key), stats = Equipment.StatsObject(Equipment.Stats(x)) }),
            talismans = talismanDefs.Select(t => new
            {
                t.Key, t.Name, t.Icon, t.Effect, t.Drawback, t.Rarity, t.PriceCoins, t.Ranked,
                charges = Talismans.Charges(t.Key),
                active = Talismans.IsActive(t.Key),
                owned = ownedTalismans.Any(o => o.TalismanKey == t.Key),
                equipped = ownedTalismans.Any(o => o.TalismanKey == t.Key && o.Equipped)
            }),
            build = new
            {
                slots = slots.Select(slot =>
                {
                    var worn = owned.Where(o => o.Equipped).Select(o => items.FirstOrDefault(i => i.Key == o.ItemKey)).FirstOrDefault(i => i is not null && i.Kind == slot.Key);
                    return new { slot.Key, slot.Name, item = worn is null ? null : new { worn.Key, worn.Name, worn.Icon, image = Equipment.ImageUrl(worn), worn.Rarity, stats = Equipment.StatsObject(Equipment.Stats(worn)) } };
                }),
                totals = Equipment.StatsObject(rawTotals),
                active = Equipment.StatsObject(cappedTotals)
            },
            objectives = objectives.Select(o =>
            {
                var progress = Math.Min(o.Target, metrics.GetValueOrDefault(o.Metric));
                var claimed = claims.Any(c => c.ObjectiveKey == o.Key && c.PeriodKey == PeriodKey(o.Repeat));
                return new
                {
                    o.Key, o.Kind, o.Name, o.Task, o.Repeat, progress, o.Target,
                    reward = RewardLabel(o),
                    state = claimed ? "claimed" : progress >= o.Target ? "ready" : "progress"
                };
            }),
            messages = messages.Select(m => new { m.Id, m.Sender, m.Subject, m.Body, m.CreatedAtUtc, read = m.ReadAtUtc is not null }),
            story = new
            {
                current = player.StoryChapter,
                totalMatches = total,
                chapters = chapters.Select(c => new { c.Number, c.Title, c.Body, c.UnlockMatches, unlocked = total >= c.UnlockMatches, started = player.StoryChapter >= c.Number })
            },
            friends = Array.Empty<object>()
        };
    }

    private static async Task<Dictionary<string, int>> Metrics(RuneDbContext db, Player player)
    {
        var today = DateTime.UtcNow.Date;
        var finishedToday = await db.CombatMatches.AsNoTracking()
            .Where(x => (x.PlayerAId == player.Id || x.PlayerBId == player.Id) && x.CompletedAtUtc != null && x.CompletedAtUtc >= today && x.ShadeDifficulty == null && x.Mode != DuelMode.Friend)
            .Select(x => x.WinnerId)
            .ToListAsync();
        return new Dictionary<string, int>
        {
            ["season_matches"] = player.SeasonMatches,
            ["season_wins"] = player.SeasonWins,
            ["matches_today"] = finishedToday.Count,
            ["wins_today"] = finishedToday.Count(x => x == player.Id),
            ["login"] = 1,
            ["promotions"] = Math.Max(0, 10 - player.League)
        };
    }

    private static Task<int> TotalMatches(RuneDbContext db, Guid playerId) =>
        db.CombatMatches.CountAsync(x => (x.PlayerAId == playerId || x.PlayerBId == playerId) && x.CompletedAtUtc != null && x.ShadeDifficulty == null && x.Mode != DuelMode.Friend);

    private static string PeriodKey(string repeat) => repeat switch
    {
        "daily" => DateTime.UtcNow.ToString("yyyy-MM-dd"),
        "season" => DateTime.UtcNow.ToString("yyyy-MM"),
        _ => ""
    };

    private static string RewardLabel(Objective o) =>
        string.Join(" + ", new[] { o.RewardCoins > 0 ? $"{o.RewardCoins} coins" : null, o.RewardGems > 0 ? $"{o.RewardGems} gems" : null }.Where(x => x is not null));

    private static PlayerSettingsDto ToDto(PlayerSettings? row) =>
        row is null ? new PlayerSettingsDto(false, true, true, false) : new PlayerSettingsDto(row.ReduceMotion, row.DamageNumbers, row.DiceAnimation, row.CompactHud);

    private static async Task<Player?> CurrentPlayer(ClaimsPrincipal user, RuneDbContext db) =>
        Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id) && await db.Players.FindAsync(id) is { Banned: false } player ? player : null;

    private static IResult NotFound() => Error("not_found", "Player not found.", 404);
    private static IResult Error(string code, string message, int status) => Results.Json(new { error = new { code, message } }, statusCode: status);
}
