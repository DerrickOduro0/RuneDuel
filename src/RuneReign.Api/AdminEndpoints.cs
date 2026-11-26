using System.Globalization;
using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using RuneReign.Domain;

namespace RuneReign.Api;

public sealed record AdminItemEdit(
    string? Key, string? Name, string? Kind, string? Icon, string? Description, string? Rarity, string? Image,
    int? PriceCoins, int? PriceGems, bool? InShop,
    int? Power, int? Guard, int? Vigor, int? Focus, int? Grit, int? Edge, int? Pierce, int? Insight, int? Resolve, int? SortOrder);
public sealed record AdminTalismanEdit(string? Name, string? Icon, string? Effect, string? Drawback, int? PriceCoins, bool? Enabled, bool? Ranked, int? SortOrder, string? Rarity = null, int? Budget = null);
public sealed record AdminAttributeEdit(string? Name, string? Effect, int? Cap);
public sealed record AdminSettingEdit(string Value);
public sealed record AdminPlayerEdit(int? Coins, int? Gems, bool? Banned, bool? IsAdmin, Dictionary<string, int>? Materials = null);
public sealed record AdminAnnouncement(string Subject, string Body);

// Rules and server settings, read from the database. Reloaded when an admin changes a setting,
// so most changes take effect straight away without restarting the server.
public static class LiveConfig
{
    public static GameRules Rules { get; private set; } = null!;
    public static ServerSettings Server { get; private set; } = null!;

    public static void Reload(RuneDbContext db, GameRules seed)
    {
        Rules = ContentStore.LoadRules(db, seed);
        Server = ContentStore.LoadServerSettings(db);
        Progression.Load(ContentStore.ReadSettings(db));
        Maintenance.Load(ContentStore.ReadSettings(db));
        TalismanCatalog.Load(db);
        Materials.Load(db);
    }
}

// The admin page's API. Every route checks that the signed-in player is an admin.
public static class AdminEndpoints
{
    private static readonly string[] Rarities = ["common", "rare", "epic"];
    private const long MaxImageBytes = 1024 * 1024;

    public static string UploadFolder(IWebHostEnvironment env) => Path.Combine(env.ContentRootPath, "uploads");

    public static void MapAdminEndpoints(this WebApplication app, GameRules seedRules)
    {
        // Uploaded pictures are public: the game shows them to every player.
        // They are stored in the database, so they survive redeploys and move with the data.
        app.MapGet("/api/v1/files/{name}", async (string name, RuneDbContext db, IWebHostEnvironment env, HttpContext http) =>
        {
            if (!Regex.IsMatch(name, "^[a-z0-9-]+\\.(png|jpg|webp)$")) return Results.NotFound();
            // Names are never reused, so browsers may keep a picture for good.
            http.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
            var stored = await db.UploadedFiles.AsNoTracking().FirstOrDefaultAsync(x => x.Name == name);
            if (stored is not null) return Results.File(stored.Data, stored.ContentType);
            // Older local uploads that were saved to disk before pictures moved into the database.
            var path = Path.Combine(UploadFolder(env), name);
            if (!File.Exists(path)) return Results.NotFound();
            var type = name.EndsWith(".png") ? "image/png" : name.EndsWith(".webp") ? "image/webp" : "image/jpeg";
            return Results.File(path, type, enableRangeProcessing: false);
        });

        var admin = app.MapGroup("/api/v1/admin").RequireAuthorization().AddEndpointFilter(async (context, next) =>
        {
            var db = context.HttpContext.RequestServices.GetRequiredService<RuneDbContext>();
            var player = await CurrentAdmin(context.HttpContext.User, db);
            return player is null ? Error("forbidden", "Only admins can do this.", 403) : await next(context);
        });

        admin.MapGet("/overview", async (RuneDbContext db) =>
        {
            var today = DateTime.UtcNow.Date;
            return Results.Ok(new
            {
                players = await db.Players.CountAsync(),
                admins = await db.Players.CountAsync(x => x.IsAdmin),
                banned = await db.Players.CountAsync(x => x.Banned),
                newToday = await db.Players.CountAsync(x => x.CreatedAtUtc >= today),
                duelsToday = await db.CombatMatches.CountAsync(x => x.CompletedAtUtc >= today && x.ShadeDifficulty == null),
                practiceToday = await db.CombatMatches.CountAsync(x => x.CompletedAtUtc >= today && x.ShadeDifficulty != null),
                activeDuels = await db.CombatMatches.CountAsync(x => x.CompletedAtUtc == null),
                searching = await db.MatchQueue.CountAsync(x => !x.Matched),
                coins = await db.Players.SumAsync(x => (long)x.Coins),
                gems = await db.Players.SumAsync(x => (long)x.Gems)
            });
        });

        // ---- Items ----
        admin.MapGet("/items", async (RuneDbContext db) =>
            Results.Ok(new
            {
                slots = await db.EquipmentSlots.AsNoTracking().OrderBy(x => x.SortOrder).Select(x => new { x.Key, x.Name }).ToListAsync(),
                items = await ItemsWithCounts(db)
            }));

        admin.MapPost("/items", async (AdminItemEdit edit, RuneDbContext db) =>
        {
            var key = (edit.Key ?? "").Trim().ToLowerInvariant();
            if (!Regex.IsMatch(key, "^[a-z0-9-]{2,64}$")) return Error("invalid_key", "Use 2-64 lowercase letters, numbers or dashes for the key.", 400);
            if (await db.Items.AnyAsync(x => x.Key == key)) return Error("exists", "An item with that key already exists.", 409);
            if (string.IsNullOrWhiteSpace(edit.Name) || string.IsNullOrWhiteSpace(edit.Kind)) return Error("missing", "Give the item a name and a slot.", 400);
            if (!await db.EquipmentSlots.AnyAsync(x => x.Key == edit.Kind)) return Error("invalid_slot", "That slot does not exist.", 400);
            var item = new ItemDefinition { Key = key, Name = edit.Name.Trim(), Kind = edit.Kind, Icon = "✦", SortOrder = await db.Items.CountAsync() };
            var problem = Apply(item, edit);
            if (problem is not null) return problem;
            db.Items.Add(item);
            await db.SaveChangesAsync();
            return Results.Ok(ItemDto(item));
        });

        admin.MapPut("/items/{key}", async (string key, AdminItemEdit edit, RuneDbContext db) =>
        {
            var item = await db.Items.FindAsync(key);
            if (item is null) return Error("not_found", "Item not found.", 404);
            if (edit.Kind is not null && !await db.EquipmentSlots.AnyAsync(x => x.Key == edit.Kind)) return Error("invalid_slot", "That slot does not exist.", 400);
            var problem = Apply(item, edit);
            if (problem is not null) return problem;
            await db.SaveChangesAsync();
            return Results.Ok(ItemDto(item));
        });

        // Upload a picture for an item. Accepts PNG, JPEG or WebP up to 1 MB.
        admin.MapPost("/items/{key}/image", async (string key, HttpRequest request, RuneDbContext db, IWebHostEnvironment env) =>
        {
            var item = await db.Items.FindAsync(key);
            if (item is null) return Error("not_found", "Item not found.", 404);
            if (!request.HasFormContentType) return Error("no_file", "Choose an image to upload.", 400);
            var form = await request.ReadFormAsync();
            var file = form.Files.FirstOrDefault();
            if (file is null || file.Length == 0) return Error("no_file", "Choose an image to upload.", 400);
            if (file.Length > MaxImageBytes) return Error("too_large", "Images must be 1 MB or smaller. Export as WebP or a smaller PNG.", 400);
            await using var stream = new MemoryStream();
            await file.CopyToAsync(stream);
            var bytes = stream.ToArray();
            var ext = ImageExtension(bytes);
            if (ext is null) return Error("not_an_image", "Upload a PNG, JPEG or WebP image.", 400);
            // A new name each time so browsers do not keep showing the old picture.
            var name = $"item-{key}-{DateTime.UtcNow:yyyyMMddHHmmss}{ext}";
            await RemoveStoredFile(db, item.Image);
            db.UploadedFiles.Add(new UploadedFile { Name = name, ContentType = ContentTypeFor(ext), Data = bytes, CreatedAtUtc = DateTime.UtcNow });
            item.Image = $"/api/v1/files/{name}";
            await db.SaveChangesAsync();
            return Results.Ok(ItemDto(item));
        }).DisableAntiforgery();

        admin.MapDelete("/items/{key}/image", async (string key, RuneDbContext db) =>
        {
            var item = await db.Items.FindAsync(key);
            if (item is null) return Error("not_found", "Item not found.", 404);
            await RemoveStoredFile(db, item.Image);
            item.Image = "";
            await db.SaveChangesAsync();
            return Results.Ok(ItemDto(item));
        });

        // ---- Talismans ----
        admin.MapGet("/talismans", async (RuneDbContext db) =>
        {
            var stats = await AdminInsights.TalismanStats(db);
            var rows = await db.Talismans.AsNoTracking().OrderBy(x => x.SortOrder).ToListAsync();
            return Results.Ok(rows.Select(t => TalismanDto(t, stats.GetValueOrDefault(t.Key))));
        });

        admin.MapPut("/talismans/{key}", async (string key, AdminTalismanEdit edit, RuneDbContext db) =>
        {
            var t = await db.Talismans.FindAsync(key);
            if (t is null) return Error("not_found", "Talisman not found.", 404);
            if (edit.Name is not null) { if (string.IsNullOrWhiteSpace(edit.Name)) return Error("missing", "A talisman needs a name.", 400); t.Name = edit.Name.Trim(); }
            if (edit.Icon is not null) t.Icon = edit.Icon.Trim();
            if (edit.Effect is not null) t.Effect = edit.Effect.Trim();
            if (edit.Drawback is not null) t.Drawback = edit.Drawback.Trim();
            if (edit.PriceCoins is not null) { if (edit.PriceCoins < 0) return Error("invalid_price", "Prices cannot be negative.", 400); t.PriceCoins = edit.PriceCoins.Value; }
            if (edit.Enabled is not null) t.Enabled = edit.Enabled.Value;
            if (edit.Ranked is not null) t.Ranked = edit.Ranked.Value;
            if (edit.SortOrder is not null) t.SortOrder = edit.SortOrder.Value;
            if (edit.Rarity is not null) { if (!Rarities.Contains(edit.Rarity)) return Error("invalid_rarity", "Rarity must be common, rare or epic.", 400); t.Rarity = edit.Rarity; }
            if (edit.Budget is not null) { if (edit.Budget is < 0 or > 10) return Error("invalid_budget", "Power points must be between 0 and 10.", 400); t.Budget = edit.Budget.Value; }
            await db.SaveChangesAsync();
            TalismanCatalog.Load(db);
            var stats = await AdminInsights.TalismanStats(db);
            return Results.Ok(TalismanDto(t, stats.GetValueOrDefault(t.Key)));
        });

        // ---- Attributes ----
        admin.MapGet("/attributes", async (RuneDbContext db) =>
            Results.Ok(await db.Attributes.AsNoTracking().OrderBy(x => x.SortOrder).Select(x => new { x.Key, x.Name, x.Icon, x.Effect, x.Cap }).ToListAsync()));

        admin.MapPut("/attributes/{key}", async (string key, AdminAttributeEdit edit, RuneDbContext db) =>
        {
            var a = await db.Attributes.FindAsync(key);
            if (a is null) return Error("not_found", "Attribute not found.", 404);
            if (edit.Name is not null && !string.IsNullOrWhiteSpace(edit.Name)) a.Name = edit.Name.Trim();
            if (edit.Effect is not null) a.Effect = edit.Effect.Trim();
            if (edit.Cap is not null) { if (edit.Cap is < 0 or > 10) return Error("invalid_cap", "Caps must be between 0 and 10.", 400); a.Cap = edit.Cap.Value; }
            await db.SaveChangesAsync();
            return Results.Ok(new { a.Key, a.Name, a.Icon, a.Effect, a.Cap });
        });

        // ---- Game settings ----
        // Maintenance rows are managed on their own tab, so they are left out here.
        admin.MapGet("/settings", async (RuneDbContext db) =>
            Results.Ok(await db.GameSettings.AsNoTracking().Where(x => !Maintenance.Keys.Contains(x.Key)).OrderBy(x => x.Key).Select(x => new { x.Key, x.Value, x.Description }).ToListAsync()));

        // ---- Maintenance mode ----
        admin.MapGet("/maintenance", async (RuneDbContext db) => Results.Ok(await MaintenanceDto(db)));
        admin.MapPut("/maintenance", async (MaintenanceEdit edit, RuneDbContext db) =>
        {
            if (edit.Message is { Length: > 300 }) return Error("too_long", "Keep the message under 300 characters.", 400);
            if (edit.Notice is { Length: > 200 }) return Error("too_long", "Keep the warning under 200 characters.", 400);
            await Maintenance.Save(db, edit);
            return Results.Ok(await MaintenanceDto(db));
        });

        admin.MapPut("/settings/{key}", async (string key, AdminSettingEdit edit, RuneDbContext db) =>
        {
            var setting = await db.GameSettings.FindAsync(key);
            if (setting is null) return Error("not_found", "Setting not found.", 404);
            var value = (edit.Value ?? "").Trim();
            // Numbers stay numbers, so a typo cannot break the game.
            if (int.TryParse(setting.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _) && !int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                return Error("not_a_number", "This setting must be a whole number.", 400);
            setting.Value = value;
            await db.SaveChangesAsync();
            LiveConfig.Reload(db, seedRules);
            return Results.Ok(new { setting.Key, setting.Value, setting.Description });
        });

        // ---- Players ----
        admin.MapGet("/players", async (string? q, RuneDbContext db) =>
        {
            var query = db.Players.AsNoTracking();
            if (!string.IsNullOrWhiteSpace(q))
            {
                var term = q.Trim().ToLowerInvariant();
                query = query.Where(x => x.Username.ToLower().Contains(term) || x.Email.Contains(term));
            }
            var rows = await query.OrderByDescending(x => x.LastSeenAtUtc ?? x.CreatedAtUtc).Take(50).ToListAsync();
            return Results.Ok(rows.Select(PlayerDto));
        });

        admin.MapPut("/players/{id:guid}", async (Guid id, AdminPlayerEdit edit, ClaimsPrincipal user, RuneDbContext db) =>
        {
            var target = await db.Players.FindAsync(id);
            if (target is null) return Error("not_found", "Player not found.", 404);
            var me = await CurrentAdmin(user, db);
            if (me!.Id == id && (edit.Banned == true || edit.IsAdmin == false))
                return Error("self", "You cannot ban yourself or remove your own admin access.", 400);
            if (edit.Coins is not null) { if (edit.Coins < 0) return Error("invalid", "Coins cannot be negative.", 400); target.Coins = edit.Coins.Value; }
            if (edit.Gems is not null) { if (edit.Gems < 0) return Error("invalid", "Gems cannot be negative.", 400); target.Gems = edit.Gems.Value; }
            if (edit.Materials is not null)
            {
                // Sets each listed material to the given count.
                var known = await db.Materials.Select(x => x.Key).ToListAsync();
                foreach (var (key, count) in edit.Materials)
                {
                    if (!known.Contains(key)) return Error("invalid", $"Unknown material: {key}.", 400);
                    if (count < 0) return Error("invalid", "Material counts cannot be negative.", 400);
                    var row = await db.PlayerMaterials.FindAsync(id, key);
                    if (row is null) db.PlayerMaterials.Add(new PlayerMaterial { PlayerId = id, MaterialKey = key, Count = count });
                    else row.Count = count;
                }
            }
            if (edit.Banned is not null) target.Banned = edit.Banned.Value;
            if (edit.IsAdmin is not null) target.IsAdmin = edit.IsAdmin.Value;
            if (target.Banned)
            {
                // A banned player leaves the queue straight away.
                await db.MatchQueue.Where(x => x.PlayerId == id && !x.Matched).ExecuteDeleteAsync();
            }
            await db.SaveChangesAsync();
            return Results.Ok(PlayerDto(target));
        });

        AdminInsights.Map(admin);
        Forge.MapForgeAdmin(admin);

        // ---- Announcements: a letter in every player's mailbox ----
        admin.MapPost("/announce", async (AdminAnnouncement message, RuneDbContext db) =>
        {
            if (string.IsNullOrWhiteSpace(message.Subject) || string.IsNullOrWhiteSpace(message.Body))
                return Error("missing", "Write a subject and a message.", 400);
            var ids = await db.Players.Where(x => !x.Banned).Select(x => x.Id).ToListAsync();
            var now = DateTime.UtcNow;
            db.PlayerMessages.AddRange(ids.Select(id => new PlayerMessage
            {
                Id = Guid.NewGuid(), PlayerId = id, Sender = "The Rune Council",
                Subject = message.Subject.Trim(), Body = message.Body.Trim(), CreatedAtUtc = now
            }));
            await db.SaveChangesAsync();
            return Results.Ok(new { sent = ids.Count });
        });
    }

    private static async Task<Player?> CurrentAdmin(ClaimsPrincipal user, RuneDbContext db) =>
        Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id) && await db.Players.FindAsync(id) is { IsAdmin: true, Banned: false } player ? player : null;

    // Checks the file's first bytes, not its name, so only real images are stored.
    internal static string ContentTypeFor(string ext) => ext switch { ".png" => "image/png", ".webp" => "image/webp", _ => "image/jpeg" };

    // Deletes the picture an item used before, so replaced pictures do not fill the database.
    private static async Task RemoveStoredFile(RuneDbContext db, string? image)
    {
        const string prefix = "/api/v1/files/";
        if (string.IsNullOrEmpty(image) || !image.StartsWith(prefix)) return;
        var old = await db.UploadedFiles.FirstOrDefaultAsync(x => x.Name == image.Substring(prefix.Length));
        if (old is not null) db.UploadedFiles.Remove(old);
    }

    internal static string? ImageExtension(byte[] b)
    {
        if (b.Length > 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47) return ".png";
        if (b.Length > 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF) return ".jpg";
        if (b.Length > 12 && b[0] == 0x52 && b[1] == 0x49 && b[2] == 0x46 && b[3] == 0x46 && b[8] == 0x57 && b[9] == 0x45 && b[10] == 0x42 && b[11] == 0x50) return ".webp";
        return null;
    }

    private static IResult Error(string code, string message, int status) => Results.Json(new { error = new { code, message } }, statusCode: status);

    private static IResult? Apply(ItemDefinition item, AdminItemEdit e)
    {
        if (e.Name is not null) { if (string.IsNullOrWhiteSpace(e.Name)) return Error("missing", "An item needs a name.", 400); item.Name = e.Name.Trim(); }
        if (e.Kind is not null) item.Kind = e.Kind;
        if (e.Icon is not null && !string.IsNullOrWhiteSpace(e.Icon)) item.Icon = e.Icon.Trim();
        if (e.Description is not null) item.Description = e.Description.Trim();
        if (e.Rarity is not null) { if (!Rarities.Contains(e.Rarity)) return Error("invalid_rarity", "Rarity must be common, rare or epic.", 400); item.Rarity = e.Rarity; }
        if (e.Image is not null) item.Image = e.Image.Trim();
        if (e.PriceCoins is not null) { if (e.PriceCoins < 0) return Error("invalid_price", "Prices cannot be negative.", 400); item.PriceCoins = e.PriceCoins.Value; }
        if (e.PriceGems is not null) { if (e.PriceGems < 0) return Error("invalid_price", "Prices cannot be negative.", 400); item.PriceGems = e.PriceGems.Value; }
        if (e.InShop is not null) item.InShop = e.InShop.Value;
        if (e.SortOrder is not null) item.SortOrder = e.SortOrder.Value;
        int? stat(int? v) => v is null ? null : Math.Clamp(v.Value, 0, 5);
        item.Power = stat(e.Power) ?? item.Power; item.Guard = stat(e.Guard) ?? item.Guard; item.Vigor = stat(e.Vigor) ?? item.Vigor;
        item.Focus = stat(e.Focus) ?? item.Focus; item.Grit = stat(e.Grit) ?? item.Grit; item.Edge = stat(e.Edge) ?? item.Edge;
        item.Pierce = stat(e.Pierce) ?? item.Pierce; item.Insight = stat(e.Insight) ?? item.Insight; item.Resolve = stat(e.Resolve) ?? item.Resolve;
        return null;
    }

    private static object ItemDto(ItemDefinition x) => new
    {
        x.Key, x.Name, x.Kind, x.Icon, x.Description, x.Rarity, x.PriceCoins, x.PriceGems, x.InShop, x.IsStarter, x.SortOrder,
        image = x.Image, imageUrl = Equipment.ImageUrl(x),
        stats = Equipment.StatsObject(Equipment.Stats(x))
    };

    private static object TalismanDto(TalismanDefinition t, object? stats) => new
    {
        t.Key, t.Name, t.Icon, t.Effect, t.Drawback, t.Rarity, t.Budget, t.PriceCoins, t.Enabled, t.Ranked, t.SortOrder,
        rules = AdminInsights.Rules(t.Key),
        warnings = AdminInsights.TextWarnings(t),
        stats
    };

    private static object PlayerDto(Player p) => AdminInsights.PlayerRow(p);

    // The Maintenance tab: the current state, plus who would be affected right now.
    private static async Task<object> MaintenanceDto(RuneDbContext db)
    {
        var since = DateTime.UtcNow.AddMinutes(-3);
        var duelSince = DateTime.UtcNow.AddHours(-1);
        return new
        {
            on = Maintenance.On,
            message = Maintenance.Message,
            backAtUtc = Maintenance.BackAtUtc,
            letDuelsFinish = Maintenance.LetDuelsFinish,
            notice = Maintenance.Notice,
            startedAtUtc = Maintenance.StartedAtUtc,
            onlineNow = await db.Players.CountAsync(x => x.LastSeenAtUtc >= since),
            duelsInProgress = await db.CombatMatches.CountAsync(x => x.CompletedAtUtc == null && x.AcceptedA && x.AcceptedB && x.AcceptDeadlineUtc == null && x.CreatedAtUtc > duelSince),
            searching = await db.MatchQueue.CountAsync(x => x.CombatMatchId == null)
        };
    }

    private static async Task<List<object>> ItemsWithCounts(RuneDbContext db)
    {
        var counts = await db.PlayerItems.AsNoTracking().GroupBy(x => x.ItemKey)
            .Select(g => new { Key = g.Key, Owned = g.Count(), Equipped = g.Count(x => x.Equipped) }).ToDictionaryAsync(x => x.Key);
        return (await db.Items.AsNoTracking().OrderBy(x => x.SortOrder).ToListAsync()).Select(x =>
        {
            var c = counts.GetValueOrDefault(x.Key);
            return (object)new
            {
                x.Key, x.Name, x.Kind, x.Icon, x.Description, x.Rarity, x.PriceCoins, x.PriceGems, x.InShop, x.IsStarter, x.SortOrder,
                image = x.Image, imageUrl = Equipment.ImageUrl(x), stats = Equipment.StatsObject(Equipment.Stats(x)),
                owned = c?.Owned ?? 0, equipped = c?.Equipped ?? 0
            };
        }).ToList();
    }
}
