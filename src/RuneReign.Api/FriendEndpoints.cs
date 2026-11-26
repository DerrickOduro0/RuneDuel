using System.Security.Claims;
using Microsoft.EntityFrameworkCore;

namespace RuneReign.Api;

public sealed record FriendRequestBody(string Username);
public sealed record FriendPerson(Guid Id, string Username, string? ClassName, int League, int Points, bool Online, bool InDuel, DateTime? LastSeenAtUtc, string? AvatarUrl, int Level);

// Friends: requests, the friend list with who is online, and challenging a friend straight to a duel.
public static class FriendEndpoints
{
    private const int MaxFriends = 200;
    private const int MaxPendingSent = 30;
    // A player counts as online if the game heard from them this recently.
    private static readonly TimeSpan OnlineWindow = TimeSpan.FromMinutes(3);

    public static void MapFriendEndpoints(this WebApplication app)
    {
        var friends = app.MapGroup("/api/v1/friends").RequireAuthorization();

        // Small and cheap: the game calls this every minute for the badge and invites. It also marks the player as online.
        friends.MapGet("/summary", async (ClaimsPrincipal user, RuneDbContext db) =>
        {
            var me = await CurrentPlayer(user, db);
            if (me is null) return NotFound();
            await Touch(db, me);
            var requests = await db.Friendships.CountAsync(x => x.AddresseeId == me.Id && !x.Accepted);
            return Results.Ok(new { requests, invites = await Invites(db, me.Id) });
        });

        friends.MapGet("", async (ClaimsPrincipal user, RuneDbContext db) =>
        {
            var me = await CurrentPlayer(user, db);
            if (me is null) return NotFound();
            await Touch(db, me);
            var rows = await db.Friendships.AsNoTracking().Where(x => x.RequesterId == me.Id || x.AddresseeId == me.Id).ToListAsync();
            var otherIds = rows.Select(x => Other(x, me.Id)).Distinct().ToList();
            var players = await db.Players.AsNoTracking().Where(x => otherIds.Contains(x.Id) && !x.Banned).ToDictionaryAsync(x => x.Id);
            var friendIds = rows.Where(x => x.Accepted).Select(x => Other(x, me.Id)).ToList();

            // Who is in a duel right now, and the record between me and each friend.
            var since = DateTime.UtcNow.AddHours(-1);
            var dueling = await db.CombatMatches.AsNoTracking()
                .Where(x => x.CompletedAtUtc == null && x.AcceptedA && x.AcceptedB && x.CreatedAtUtc > since && (friendIds.Contains(x.PlayerAId) || friendIds.Contains(x.PlayerBId)))
                .Select(x => new { x.PlayerAId, x.PlayerBId }).ToListAsync();
            var busy = dueling.SelectMany(x => new[] { x.PlayerAId, x.PlayerBId }).ToHashSet();
            var between = await db.CombatMatches.AsNoTracking()
                .Where(x => x.CompletedAtUtc != null && x.ShadeDifficulty == null &&
                    ((x.PlayerAId == me.Id && friendIds.Contains(x.PlayerBId)) || (x.PlayerBId == me.Id && friendIds.Contains(x.PlayerAId))))
                .Select(x => new { x.PlayerAId, x.PlayerBId, x.WinnerId, x.IsDraw }).ToListAsync();

            FriendPerson Person(Player p) => new(p.Id, p.Username, p.ClassName, p.League, p.Points,
                p.LastSeenAtUtc is DateTime seen && DateTime.UtcNow - seen < OnlineWindow, busy.Contains(p.Id), p.LastSeenAtUtc,
                ProfileEndpoints.AvatarUrl(p), Progression.LevelFor(p.Xp));

            var list = rows.Where(x => x.Accepted && players.ContainsKey(Other(x, me.Id))).Select(x =>
            {
                var p = players[Other(x, me.Id)];
                var games = between.Where(g => g.PlayerAId == p.Id || g.PlayerBId == p.Id).ToList();
                return new
                {
                    friend = Person(p),
                    since = x.AcceptedAtUtc,
                    record = new { wins = games.Count(g => g.WinnerId == me.Id), losses = games.Count(g => g.WinnerId == p.Id), draws = games.Count(g => g.IsDraw) }
                };
            })
            .OrderByDescending(x => x.friend.InDuel || x.friend.Online)
            .ThenBy(x => x.friend.Username, StringComparer.OrdinalIgnoreCase)
            .ToList();

            return Results.Ok(new
            {
                friends = list,
                incoming = rows.Where(x => !x.Accepted && x.AddresseeId == me.Id && players.ContainsKey(x.RequesterId))
                    .OrderByDescending(x => x.CreatedAtUtc).Select(x => new { requestId = x.Id, player = Person(players[x.RequesterId]), sentAtUtc = x.CreatedAtUtc }),
                outgoing = rows.Where(x => !x.Accepted && x.RequesterId == me.Id && players.ContainsKey(x.AddresseeId))
                    .OrderByDescending(x => x.CreatedAtUtc).Select(x => new { requestId = x.Id, player = Person(players[x.AddresseeId]), sentAtUtc = x.CreatedAtUtc }),
                invites = await Invites(db, me.Id)
            });
        });

        // Find players to add. Shows how each one relates to you already.
        friends.MapGet("/search", async (string? q, ClaimsPrincipal user, RuneDbContext db) =>
        {
            var me = await CurrentPlayer(user, db);
            if (me is null) return NotFound();
            var term = (q ?? "").Trim().ToLowerInvariant();
            if (term.Length < 2) return Results.Ok(Array.Empty<object>());
            var found = await db.Players.AsNoTracking()
                .Where(x => x.Id != me.Id && !x.Banned && x.Username.ToLower().Contains(term))
                .OrderBy(x => x.Username.Length).Take(10).ToListAsync();
            var ids = found.Select(x => x.Id).ToList();
            var links = await db.Friendships.AsNoTracking()
                .Where(x => (x.RequesterId == me.Id && ids.Contains(x.AddresseeId)) || (x.AddresseeId == me.Id && ids.Contains(x.RequesterId))).ToListAsync();
            return Results.Ok(found.Select(p =>
            {
                var link = links.FirstOrDefault(x => x.RequesterId == p.Id || x.AddresseeId == p.Id);
                var relation = link is null ? "none" : link.Accepted ? "friend" : link.RequesterId == me.Id ? "sent" : "received";
                return new { id = p.Id, username = p.Username, className = p.ClassName, league = p.League, relation, requestId = link?.Id, avatarUrl = ProfileEndpoints.AvatarUrl(p), level = Progression.LevelFor(p.Xp) };
            }));
        });

        // Send a request. If they had already asked you, this accepts theirs.
        friends.MapPost("/requests", async (FriendRequestBody body, ClaimsPrincipal user, RuneDbContext db) =>
        {
            var me = await CurrentPlayer(user, db);
            if (me is null) return NotFound();
            var name = (body.Username ?? "").Trim().ToLowerInvariant();
            var target = await db.Players.FirstOrDefaultAsync(x => x.Username.ToLower() == name && !x.Banned);
            if (target is null) return Error("no_player", "No player has that name.", 404);
            if (target.Id == me.Id) return Error("self", "You cannot add yourself.", 400);
            var link = await db.Friendships.FirstOrDefaultAsync(x =>
                (x.RequesterId == me.Id && x.AddresseeId == target.Id) || (x.RequesterId == target.Id && x.AddresseeId == me.Id));
            if (link is not null)
            {
                if (link.Accepted) return Error("already", $"You and {target.Username} are already friends.", 409);
                if (link.RequesterId == me.Id) return Error("pending", $"Your request to {target.Username} is still waiting.", 409);
                if (await FriendCount(db, me.Id) >= MaxFriends) return Error("full", $"You can have up to {MaxFriends} friends.", 409);
                link.Accepted = true;
                link.AcceptedAtUtc = DateTime.UtcNow;
                await db.SaveChangesAsync();
                return Results.Ok(new { status = "friends", message = $"You and {target.Username} are now friends." });
            }
            if (await FriendCount(db, me.Id) >= MaxFriends) return Error("full", $"You can have up to {MaxFriends} friends.", 409);
            if (await db.Friendships.CountAsync(x => x.RequesterId == me.Id && !x.Accepted) >= MaxPendingSent)
                return Error("too_many", "You have too many requests waiting. Cancel some first.", 409);
            db.Friendships.Add(new Friendship { Id = Guid.NewGuid(), RequesterId = me.Id, AddresseeId = target.Id, CreatedAtUtc = DateTime.UtcNow });
            await db.SaveChangesAsync();
            return Results.Ok(new { status = "sent", message = $"Request sent to {target.Username}." });
        });

        friends.MapPost("/requests/{id:guid}/accept", async (Guid id, ClaimsPrincipal user, RuneDbContext db) =>
        {
            var me = await CurrentPlayer(user, db);
            if (me is null) return NotFound();
            var link = await db.Friendships.FirstOrDefaultAsync(x => x.Id == id && x.AddresseeId == me.Id && !x.Accepted);
            if (link is null) return Error("gone", "That request is no longer open.", 404);
            if (await FriendCount(db, me.Id) >= MaxFriends) return Error("full", $"You can have up to {MaxFriends} friends.", 409);
            link.Accepted = true;
            link.AcceptedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync();
            return Results.Ok(new { status = "friends" });
        });

        friends.MapPost("/requests/{id:guid}/decline", async (Guid id, ClaimsPrincipal user, RuneDbContext db) =>
        {
            var me = await CurrentPlayer(user, db);
            if (me is null) return NotFound();
            await db.Friendships.Where(x => x.Id == id && x.AddresseeId == me.Id && !x.Accepted).ExecuteDeleteAsync();
            return Results.Ok(new { status = "declined" });
        });

        // Take back a request you sent.
        friends.MapDelete("/requests/{id:guid}", async (Guid id, ClaimsPrincipal user, RuneDbContext db) =>
        {
            var me = await CurrentPlayer(user, db);
            if (me is null) return NotFound();
            await db.Friendships.Where(x => x.Id == id && x.RequesterId == me.Id && !x.Accepted).ExecuteDeleteAsync();
            return Results.Ok(new { status = "cancelled" });
        });

        friends.MapDelete("/{playerId:guid}", async (Guid playerId, ClaimsPrincipal user, RuneDbContext db) =>
        {
            var me = await CurrentPlayer(user, db);
            if (me is null) return NotFound();
            await db.Friendships.Where(x => x.Accepted &&
                ((x.RequesterId == me.Id && x.AddresseeId == playerId) || (x.RequesterId == playerId && x.AddresseeId == me.Id))).ExecuteDeleteAsync();
            return Results.Ok(new { status = "removed" });
        });

        // Challenge a friend: opens (or reuses) your challenge code and sends it straight to them as an invite.
        friends.MapPost("/{playerId:guid}/challenge", async (Guid playerId, ClaimsPrincipal user, RuneDbContext db, ServerSettings server) =>
        {
            var me = await CurrentPlayer(user, db);
            if (me is null) return NotFound();
            if (me.ClassName is null) return Error("class_required", "Choose a class before you challenge anyone.", 400);
            var friend = await db.Players.FindAsync(playerId);
            if (friend is null || friend.Banned || !await AreFriends(db, me.Id, playerId)) return Error("not_friends", "You can only challenge your friends this way.", 403);
            if (await InDuel(db, me.Id)) return Error("match_in_progress", "You still have a duel in progress. Continue it or forfeit it first.", 409);
            var open = await db.FriendChallenges.Where(x => x.HostId == me.Id && x.CombatMatchId == null && x.ExpiresAtUtc > DateTime.UtcNow)
                .OrderByDescending(x => x.CreatedAtUtc).FirstOrDefaultAsync();
            if (open is null)
            {
                string code;
                do code = NewCode(); while (await db.FriendChallenges.AnyAsync(x => x.Code == code));
                open = new FriendChallenge { Id = Guid.NewGuid(), Code = code, HostId = me.Id, CreatedAtUtc = DateTime.UtcNow };
                db.FriendChallenges.Add(open);
            }
            // A fresh invite gets the full time to answer.
            open.InviteeId = playerId;
            open.ExpiresAtUtc = DateTime.UtcNow.AddMinutes(server.ChallengeMinutes);
            await db.SaveChangesAsync();
            return Results.Ok(new
            {
                status = "open", matchId = (Guid?)null, code = open.Code,
                expiresInSeconds = (int)(open.ExpiresAtUtc - DateTime.UtcNow).TotalSeconds,
                invitee = friend.Username
            });
        });

        // Say no to a friend's duel invite. Their code stays open for anyone else they share it with.
        friends.MapPost("/invites/{challengeId:guid}/decline", async (Guid challengeId, ClaimsPrincipal user, RuneDbContext db) =>
        {
            var me = await CurrentPlayer(user, db);
            if (me is null) return NotFound();
            var invite = await db.FriendChallenges.FirstOrDefaultAsync(x => x.Id == challengeId && x.InviteeId == me.Id);
            if (invite is not null)
            {
                invite.InviteeId = null;
                await db.SaveChangesAsync();
            }
            return Results.Ok(new { status = "declined" });
        });
    }

    private static async Task<List<object>> Invites(RuneDbContext db, Guid meId)
    {
        var now = DateTime.UtcNow;
        var rows = await db.FriendChallenges.AsNoTracking()
            .Where(x => x.InviteeId == meId && x.CombatMatchId == null && x.ExpiresAtUtc > now)
            .OrderByDescending(x => x.CreatedAtUtc).ToListAsync();
        var hostIds = rows.Select(x => x.HostId).ToList();
        var hosts = await db.Players.AsNoTracking().Where(x => hostIds.Contains(x.Id) && !x.Banned).ToDictionaryAsync(x => x.Id);
        return rows.Where(x => hosts.ContainsKey(x.HostId)).Select(x => (object)new
        {
            challengeId = x.Id, code = x.Code, from = hosts[x.HostId].Username, fromClass = hosts[x.HostId].ClassName, fromAvatar = ProfileEndpoints.AvatarUrl(hosts[x.HostId]),
            expiresInSeconds = (int)(x.ExpiresAtUtc - now).TotalSeconds
        }).ToList();
    }

    private static Guid Other(Friendship f, Guid me) => f.RequesterId == me ? f.AddresseeId : f.RequesterId;

    private static Task<int> FriendCount(RuneDbContext db, Guid id) =>
        db.Friendships.CountAsync(x => x.Accepted && (x.RequesterId == id || x.AddresseeId == id));

    private static Task<bool> AreFriends(RuneDbContext db, Guid a, Guid b) =>
        db.Friendships.AnyAsync(x => x.Accepted && ((x.RequesterId == a && x.AddresseeId == b) || (x.RequesterId == b && x.AddresseeId == a)));

    private static Task<bool> InDuel(RuneDbContext db, Guid id)
    {
        var since = DateTime.UtcNow.AddHours(-1);
        return db.CombatMatches.AnyAsync(x => (x.PlayerAId == id || x.PlayerBId == id) && x.CompletedAtUtc == null && x.AcceptedA && x.AcceptedB && x.AcceptDeadlineUtc == null && x.CreatedAtUtc > since);
    }

    // Marks the player as seen, at most once a minute so frequent checks do not write every time.
    private static async Task Touch(RuneDbContext db, Player me)
    {
        if (me.LastSeenAtUtc is DateTime seen && DateTime.UtcNow - seen < TimeSpan.FromMinutes(1)) return;
        me.LastSeenAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    private static string NewCode()
    {
        const string alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
        return new string(Enumerable.Range(0, 6).Select(_ => alphabet[Random.Shared.Next(alphabet.Length)]).ToArray());
    }

    private static async Task<Player?> CurrentPlayer(ClaimsPrincipal user, RuneDbContext db) =>
        Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id) && await db.Players.FindAsync(id) is { Banned: false } player ? player : null;

    private static IResult NotFound() => Error("not_found", "Player not found.", 404);
    private static IResult Error(string code, string message, int status) => Results.Json(new { error = new { code, message } }, statusCode: status);
}
