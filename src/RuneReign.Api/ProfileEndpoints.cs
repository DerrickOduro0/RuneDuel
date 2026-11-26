using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using RuneReign.Domain;

namespace RuneReign.Api;

public sealed record ProfileEdit(string? Motto);
public sealed record PasswordChange(string CurrentPassword, string NewPassword);

// The profile panel: an overview of the account, the avatar picture, the motto and changing the password.
public static class ProfileEndpoints
{
    private const int MaxAvatarBytes = 300 * 1024;
    private const int MaxMotto = 60;

    public static string? AvatarUrl(Player p) => string.IsNullOrEmpty(p.AvatarFile) ? null : $"/api/v1/files/{p.AvatarFile}";

    public static void MapProfileEndpoints(this WebApplication app)
    {
        var me = app.MapGroup("/api/v1/me").RequireAuthorization();

        me.MapGet("/profile", async (ClaimsPrincipal user, RuneDbContext db, GameRules rules) =>
        {
            var player = await CurrentPlayer(user, db);
            return player is null ? NotFound() : Results.Ok(await Overview(db, player, rules, true));
        });

        me.MapPut("/profile", async (ProfileEdit body, ClaimsPrincipal user, RuneDbContext db, GameRules rules) =>
        {
            var player = await CurrentPlayer(user, db);
            if (player is null) return NotFound();
            var motto = (body.Motto ?? "").Trim();
            if (motto.Length > MaxMotto) return Error("too_long", $"Keep your motto to {MaxMotto} characters.", 400);
            player.Motto = motto;
            await db.SaveChangesAsync();
            return Results.Ok(await Overview(db, player, rules, true));
        });

        // The picture arrives already cropped and shrunk by the game (256 x 256), so it stays small.
        me.MapPost("/avatar", async (HttpRequest http, ClaimsPrincipal user, RuneDbContext db, GameRules rules) =>
        {
            var player = await CurrentPlayer(user, db);
            if (player is null) return NotFound();
            if (!http.HasFormContentType) return Error("no_file", "Choose a picture to upload.", 400);
            var form = await http.ReadFormAsync();
            var file = form.Files.GetFile("file");
            if (file is null || file.Length == 0) return Error("no_file", "Choose a picture to upload.", 400);
            if (file.Length > MaxAvatarBytes) return Error("too_large", "That picture is too large. Try a smaller one.", 400);
            using var memory = new MemoryStream();
            await file.CopyToAsync(memory);
            var bytes = memory.ToArray();
            var ext = AdminEndpoints.ImageExtension(bytes);
            if (ext is null) return Error("bad_type", "Use a PNG, JPEG or WebP picture.", 400);
            await RemoveAvatar(db, player);
            var name = $"avatar-{player.Id:N}-{DateTime.UtcNow:yyyyMMddHHmmssfff}{Random.Shared.Next(4096):x3}{ext}";
            db.UploadedFiles.Add(new UploadedFile { Name = name, ContentType = AdminEndpoints.ContentTypeFor(ext), Data = bytes, CreatedAtUtc = DateTime.UtcNow });
            player.AvatarFile = name;
            await db.SaveChangesAsync();
            return Results.Ok(await Overview(db, player, rules, true));
        }).DisableAntiforgery();

        me.MapDelete("/avatar", async (ClaimsPrincipal user, RuneDbContext db, GameRules rules) =>
        {
            var player = await CurrentPlayer(user, db);
            if (player is null) return NotFound();
            await RemoveAvatar(db, player);
            await db.SaveChangesAsync();
            return Results.Ok(await Overview(db, player, rules, true));
        });

        me.MapPost("/password", async (PasswordChange body, ClaimsPrincipal user, RuneDbContext db, IPasswordHasher<Player> hasher) =>
        {
            var player = await CurrentPlayer(user, db);
            if (player is null) return NotFound();
            if (hasher.VerifyHashedPassword(player, player.PasswordHash, body.CurrentPassword ?? "") == PasswordVerificationResult.Failed)
                return Error("wrong_password", "Your current password is not right.", 400);
            if (string.IsNullOrEmpty(body.NewPassword) || body.NewPassword.Length < 8) return Error("too_short", "The new password needs at least 8 characters.", 400);
            player.PasswordHash = hasher.HashPassword(player, body.NewPassword);
            await db.SaveChangesAsync();
            return Results.Ok(new { done = true });
        }).RequireRateLimiting("login");

        // Another player's public profile: no email, coins or gems.
        app.MapGet("/api/v1/players/{username}/profile", async (string username, ClaimsPrincipal user, RuneDbContext db, GameRules rules) =>
        {
            var viewer = await CurrentPlayer(user, db);
            if (viewer is null) return NotFound();
            var name = username.Trim().ToLowerInvariant();
            var player = await db.Players.AsNoTracking().FirstOrDefaultAsync(x => x.Username.ToLower() == name && !x.Banned);
            if (player is null) return Error("not_found", "No player has that name.", 404);
            return Results.Ok(await Overview(db, player, rules, player.Id == viewer.Id, viewer.Id));
        }).RequireAuthorization();
    }

    // Everything the profile panel shows. Private parts (email, wallet) only for the player themselves.
    public static async Task<object> Overview(RuneDbContext db, Player p, GameRules rules, bool self, Guid? viewerId = null)
    {
        string LeagueName(int number) => rules.Leagues.FirstOrDefault(x => x.Number == number)?.Name ?? $"League {number}";
        var duels = await db.CombatMatches.AsNoTracking()
            .Where(x => (x.PlayerAId == p.Id || x.PlayerBId == p.Id) && x.CompletedAtUtc != null)
            .Select(x => new { x.Mode, x.ShadeDifficulty, x.WinnerId, x.IsDraw, x.PlayerAId, x.PlayerBId, x.CompletedAtUtc })
            .ToListAsync();
        object Record(IEnumerable<dynamic> rows)
        {
            var list = rows.ToList();
            int wins = list.Count(x => x.WinnerId == p.Id), draws = list.Count(x => x.IsDraw);
            return new { played = list.Count, wins, losses = list.Count - wins - draws, draws, winRate = list.Count == 0 ? (double?)null : Math.Round(100.0 * wins / list.Count, 1) };
        }
        var pvp = duels.Where(x => x.ShadeDifficulty is null).ToList();
        var practice = duels.Where(x => x.ShadeDifficulty is not null && x.ShadeDifficulty != ShadeBot.Tutorial).ToList();

        var friends = await db.Friendships.CountAsync(x => x.Accepted && (x.RequesterId == p.Id || x.AddresseeId == p.Id));
        var talisman = await db.PlayerTalismans.AsNoTracking().Where(x => x.PlayerId == p.Id && x.Equipped).Select(x => x.TalismanKey).FirstOrDefaultAsync();
        var talismanName = talisman is null ? null : await db.Talismans.AsNoTracking().Where(x => x.Key == talisman).Select(x => x.Name).FirstOrDefaultAsync();

        // Between the viewer and this player, when someone else is looking.
        object? versus = null;
        if (viewerId is Guid v && v != p.Id)
        {
            var between = pvp.Where(x => x.PlayerAId == v || x.PlayerBId == v).ToList();
            versus = new { wins = between.Count(x => x.WinnerId == v), losses = between.Count(x => x.WinnerId == p.Id), draws = between.Count(x => x.IsDraw) };
        }

        return new
        {
            self,
            username = p.Username,
            email = self ? p.Email : null,
            avatarUrl = AvatarUrl(p),
            motto = p.Motto,
            className = p.ClassName,
            memberSince = p.CreatedAtUtc,
            lastSeenAtUtc = p.LastSeenAtUtc,
            progress = Progression.Progress(p.Xp),
            league = new { number = p.League, name = LeagueName(p.League), points = p.Points, pointsPerLeague = rules.PointsPerLeague, best = LeagueName(p.BestLeague), bestNumber = p.BestLeague },
            rating = p.SkillRating,
            season = new { matches = p.SeasonMatches, wins = p.SeasonWins },
            pvp = Record(pvp),
            ranked = Record(pvp.Where(x => x.Mode == DuelMode.Ranked)),
            casual = Record(pvp.Where(x => x.Mode == DuelMode.Casual)),
            friendDuels = Record(pvp.Where(x => x.Mode == DuelMode.Friend)),
            practice = Record(practice),
            streak = new { current = p.WinStreak, best = p.BestWinStreak },
            friends,
            itemsOwned = await db.PlayerItems.CountAsync(x => x.PlayerId == p.Id),
            talismansOwned = await db.PlayerTalismans.CountAsync(x => x.PlayerId == p.Id),
            equippedTalisman = talismanName,
            wallet = self ? new { coins = p.Coins, gems = p.Gems, dust = p.Dust, shards = p.Shards } : null,
            versus
        };
    }

    public static async Task RemoveAvatar(RuneDbContext db, Player player)
    {
        if (string.IsNullOrEmpty(player.AvatarFile)) return;
        var old = await db.UploadedFiles.FirstOrDefaultAsync(x => x.Name == player.AvatarFile);
        if (old is not null) db.UploadedFiles.Remove(old);
        player.AvatarFile = null;
    }

    private static async Task<Player?> CurrentPlayer(ClaimsPrincipal user, RuneDbContext db) =>
        Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id) && await db.Players.FindAsync(id) is { Banned: false } player ? player : null;

    private static IResult NotFound() => Error("not_found", "Player not found.", 404);
    private static IResult Error(string code, string message, int status) => Results.Json(new { error = new { code, message } }, statusCode: status);
}
