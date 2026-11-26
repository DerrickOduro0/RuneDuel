using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using RuneReign.Domain;

namespace RuneReign.Api;

public sealed record AdminPasswordReset(string Password);
public sealed record AdminGrant(string Key);
public sealed record AdminLetter(string Subject, string Body);

// The detailed parts of the admin page: stats, player profiles, matches, and the talisman rules as the code applies them.
public static class AdminInsights
{
    // ---------- Talisman rules, read from the code that runs them ----------

    // What each talisman does when it fires. This sits beside the code in RuneReign.Domain.Talismans and must change with it.
    private static string Behaviour(string key) => key switch
    {
        Talismans.LuckyKnot => "Your first reroll of the duel costs no mana.",
        Talismans.MirrorShard => "Flips your D8 to its opposite face: " + string.Join(", ", FormPairs()) + ".",
        Talismans.EmberCoal => $"Every clash you lose: +1 mana (never above the cap of {CombatEngine.ManaCap}).",
        Talismans.IronSeal => "The first clash you lose: the winner does not get the +2 clash bonus. Used up after that.",
        Talismans.GamblersCoin => "Rerolls your D6 and D8 together.",
        Talismans.PatientStone => "+1 mana straight away. For the rest of that turn you cannot reroll, pick a form or shift.",
        Talismans.SecondWind => "The first time your HP falls to 10 or less (and you are still standing): +1 mana, and your next reroll is free.",
        Talismans.TwinMark => "For that turn, whatever state you declare earns the +2 clash bonus.",
        _ => "No behaviour is written in the code for this key, so it does nothing in duels."
    };

    private static IEnumerable<string> FormPairs()
    {
        var forms = CombatEngine.GetForms();
        for (var i = 0; i < forms.Count / 2; i++) yield return $"{forms[i]} ↔ {Talismans.FlipForm(forms[i])}";
    }

    public static object Rules(string key)
    {
        var active = Talismans.IsActive(key);
        var charges = Talismans.Charges(key);
        var facts = new List<string>
        {
            active ? "Used by the player with the Use button while shaping dice." : "Works on its own; the player does nothing.",
            charges == 0 ? "No limit: it works every time its condition happens." : $"Works {(charges == 1 ? "once" : charges == 2 ? "twice" : $"{charges} times")} per duel."
        };
        var mana = Talismans.UseManaCost(key);
        var hp = Talismans.UseHpCost(key);
        if (mana > 0) facts.Add($"Each use costs {mana} mana.");
        if (hp > 0) facts.Add($"Each use costs {hp} HP.");
        if (Talismans.StartHp(key) != 30) facts.Add($"Starts the duel at {Talismans.StartHp(key)} HP instead of 30.");
        if (Talismans.StartManaChange(key) != 0) facts.Add($"Starts the duel with {Math.Abs(Talismans.StartManaChange(key))} {(Talismans.StartManaChange(key) < 0 ? "less" : "more")} mana.");
        if (mana == 0 && hp == 0 && Talismans.StartHp(key) == 30 && Talismans.StartManaChange(key) == 0) facts.Add("No cost.");
        return new { known = Talismans.IsKnown(key), behaviour = Behaviour(key), facts, active, charges, manaCost = mana, hpCost = hp, startHp = Talismans.StartHp(key), startManaChange = Talismans.StartManaChange(key) };
    }

    // Points out where the player-facing text leaves out or contradicts a rule, so the shop never misleads players.
    public static List<string> TextWarnings(TalismanDefinition t)
    {
        var text = $"{t.Effect} {t.Drawback}".ToLowerInvariant();
        var warnings = new List<string>();
        if (!Talismans.IsKnown(t.Key)) { warnings.Add("This key has no behaviour in the code; it does nothing in duels."); return warnings; }
        var mana = Talismans.UseManaCost(t.Key);
        var hp = Talismans.UseHpCost(t.Key);
        if (mana > 0 && !text.Contains($"{mana} mana")) warnings.Add($"The text should say each use costs {mana} mana.");
        if (hp > 0 && !text.Contains($"{hp} hp")) warnings.Add($"The text should say each use costs {hp} HP.");
        if (Talismans.StartHp(t.Key) != 30 && !text.Contains($"{Talismans.StartHp(t.Key)} hp")) warnings.Add($"The text should say the duel starts at {Talismans.StartHp(t.Key)} HP.");
        if (Talismans.StartManaChange(t.Key) < 0 && !text.Contains("less mana")) warnings.Add("The text should say the duel starts with less mana.");
        var charges = Talismans.Charges(t.Key);
        if (charges == 1 && !(text.Contains("once") || text.Contains("first"))) warnings.Add("The text should say it works once per duel.");
        if (charges == 2 && !text.Contains("twice")) warnings.Add("The text should say it works twice per duel.");
        if (string.IsNullOrWhiteSpace(t.Effect)) warnings.Add("The effect text is empty.");
        return warnings;
    }

    // ---------- Stats ----------

    private sealed record DuelRow(Guid Id, Guid A, Guid B, string ClassA, string ClassB, string? TalA, string? TalB, Guid? Winner, bool Draw, string Mode, int Turn, DateTime Created, DateTime? Completed);

    private static async Task<List<DuelRow>> FinishedPvp(RuneDbContext db) =>
        await db.CombatMatches.AsNoTracking()
            .Where(x => x.CompletedAtUtc != null && x.ShadeDifficulty == null && x.ResultsApplied)
            .OrderByDescending(x => x.CompletedAtUtc).Take(5000)
            .Select(x => new DuelRow(x.Id, x.PlayerAId, x.PlayerBId, x.ClassA, x.ClassB, x.TalismanA, x.TalismanB, x.WinnerId, x.IsDraw, x.Mode, x.Turn, x.CreatedAtUtc, x.CompletedAtUtc))
            .ToListAsync();

    private static object Rate(int duels, int wins, int draws) =>
        // A draw counts as half a win, so an even record reads 50%.
        new { duels, wins, draws, losses = duels - wins - draws, winRate = duels == 0 ? (double?)null : Math.Round(100.0 * (wins + 0.5 * draws) / duels, 1) };

    public static async Task<object> ClassStats(RuneDbContext db)
    {
        var duels = await FinishedPvp(db);
        var classes = duels.SelectMany(d => new[] { d.ClassA, d.ClassB }).Where(c => c != "").Distinct().OrderBy(c => c);
        return classes.Select(c =>
        {
            var sides = duels.SelectMany(d => new[] { (d.ClassA, d.A, d), (d.ClassB, d.B, d) }).Where(x => x.Item1 == c).ToList();
            var mirror = duels.Count(d => d.ClassA == c && d.ClassB == c);
            return new { className = c, stats = Rate(sides.Count, sides.Count(x => x.d.Winner == x.Item2), sides.Count(x => x.d.Draw)), mirrorMatches = mirror };
        }).ToList();
    }

    public static async Task<Dictionary<string, object>> TalismanStats(RuneDbContext db)
    {
        var duels = await FinishedPvp(db);
        var owned = await db.PlayerTalismans.AsNoTracking().GroupBy(x => x.TalismanKey)
            .Select(g => new { Key = g.Key, Owned = g.Count(), Equipped = g.Count(x => x.Equipped) }).ToListAsync();
        var keys = owned.Select(x => x.Key).Concat(duels.SelectMany(d => new[] { d.TalA, d.TalB }).OfType<string>()).Distinct();
        return keys.ToDictionary(k => k, k =>
        {
            var sides = duels.SelectMany(d => new[] { (d.TalA, d.A, d), (d.TalB, d.B, d) }).Where(x => x.Item1 == k).ToList();
            var o = owned.FirstOrDefault(x => x.Key == k);
            return (object)new { owned = o?.Owned ?? 0, equipped = o?.Equipped ?? 0, pvp = Rate(sides.Count, sides.Count(x => x.d.Winner == x.Item2), sides.Count(x => x.d.Draw)) };
        });
    }

    // ---------- Routes ----------

    public static void Map(RouteGroupBuilder admin)
    {
        admin.MapGet("/stats", async (RuneDbContext db) =>
        {
            var today = DateTime.UtcNow.Date;
            var since = today.AddDays(-13);
            var signups = await db.Players.AsNoTracking().Where(x => x.CreatedAtUtc >= since).Select(x => x.CreatedAtUtc).ToListAsync();
            var duelDays = await db.CombatMatches.AsNoTracking().Where(x => x.CreatedAtUtc >= since && x.ShadeDifficulty == null).Select(x => x.CreatedAtUtc).ToListAsync();
            var days = Enumerable.Range(0, 14).Select(i => since.AddDays(i)).Select(d => new
            {
                day = d.ToString("yyyy-MM-dd"),
                signups = signups.Count(x => x.Date == d),
                duels = duelDays.Count(x => x.Date == d)
            });
            return Results.Ok(new
            {
                activeToday = await db.Players.CountAsync(x => x.LastSeenAtUtc >= today),
                activeWeek = await db.Players.CountAsync(x => x.LastSeenAtUtc >= today.AddDays(-6)),
                days,
                classes = await ClassStats(db),
                recent = await MatchRows(db, db.CombatMatches.AsNoTracking().OrderByDescending(x => x.CreatedAtUtc).Take(10))
            });
        });

        // ---- One player in detail ----
        admin.MapGet("/players/{id:guid}", async (Guid id, RuneDbContext db) =>
        {
            var p = await db.Players.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            if (p is null) return Error("not_found", "Player not found.", 404);
            var mine = db.CombatMatches.AsNoTracking().Where(x => x.PlayerAId == id || x.PlayerBId == id);
            var pvp = await mine.Where(x => x.CompletedAtUtc != null && x.ShadeDifficulty == null)
                .Select(x => new { x.WinnerId, x.IsDraw }).ToListAsync();
            var itemNames = await db.Items.AsNoTracking().ToDictionaryAsync(x => x.Key, x => x.Name);
            var talismanNames = await db.Talismans.AsNoTracking().ToDictionaryAsync(x => x.Key, x => x.Name);
            var items = await db.PlayerItems.AsNoTracking().Where(x => x.PlayerId == id).OrderBy(x => x.AcquiredAtUtc).ToListAsync();
            var talismans = await db.PlayerTalismans.AsNoTracking().Where(x => x.PlayerId == id).OrderBy(x => x.AcquiredAtUtc).ToListAsync();
            return Results.Ok(new
            {
                player = PlayerRow(p),
                record = new
                {
                    pvp = Rate(pvp.Count, pvp.Count(x => x.WinnerId == id), pvp.Count(x => x.IsDraw)),
                    practice = await mine.CountAsync(x => x.ShadeDifficulty != null),
                    unfinished = await mine.CountAsync(x => x.CompletedAtUtc == null)
                },
                items = items.Select(x => new { key = x.ItemKey, name = itemNames.GetValueOrDefault(x.ItemKey, x.ItemKey), x.Equipped, x.AcquiredAtUtc }),
                talismans = talismans.Select(x => new { key = x.TalismanKey, name = talismanNames.GetValueOrDefault(x.TalismanKey, x.TalismanKey), x.Equipped, x.AcquiredAtUtc }),
                matches = await MatchRows(db, mine.OrderByDescending(x => x.CreatedAtUtc).Take(20)),
                unreadLetters = await db.PlayerMessages.CountAsync(x => x.PlayerId == id && x.ReadAtUtc == null)
            });
        });

        admin.MapPost("/players/{id:guid}/password", async (Guid id, AdminPasswordReset body, RuneDbContext db, IPasswordHasher<Player> hasher) =>
        {
            var p = await db.Players.FindAsync(id);
            if (p is null) return Error("not_found", "Player not found.", 404);
            if (string.IsNullOrWhiteSpace(body.Password) || body.Password.Length < 8) return Error("too_short", "Passwords need at least 8 characters.", 400);
            p.PasswordHash = hasher.HashPassword(p, body.Password);
            await db.SaveChangesAsync();
            LoginGuard.Clear(p.Username);
            LoginGuard.Clear(p.Email);
            return Results.Ok(new { done = true });
        });

        // Removes a player's picture, for example if it is not suitable. They can upload another.
        admin.MapDelete("/players/{id:guid}/avatar", async (Guid id, RuneDbContext db) =>
        {
            var p = await db.Players.FindAsync(id);
            if (p is null) return Error("not_found", "Player not found.", 404);
            await ProfileEndpoints.RemoveAvatar(db, p);
            await db.SaveChangesAsync();
            return Results.Ok(new { done = true });
        });

        // Clears a player's motto, for the same reason.
        admin.MapDelete("/players/{id:guid}/motto", async (Guid id, RuneDbContext db) =>
        {
            var p = await db.Players.FindAsync(id);
            if (p is null) return Error("not_found", "Player not found.", 404);
            p.Motto = "";
            await db.SaveChangesAsync();
            return Results.Ok(new { done = true });
        });

        admin.MapPost("/players/{id:guid}/items", async (Guid id, AdminGrant body, RuneDbContext db) =>
        {
            if (await db.Players.FindAsync(id) is null) return Error("not_found", "Player not found.", 404);
            if (!await db.Items.AnyAsync(x => x.Key == body.Key)) return Error("not_found", "That item does not exist.", 404);
            if (await db.PlayerItems.AnyAsync(x => x.PlayerId == id && x.ItemKey == body.Key)) return Error("owned", "The player already owns that item.", 409);
            db.PlayerItems.Add(new PlayerItem { Id = Guid.NewGuid(), PlayerId = id, ItemKey = body.Key, AcquiredAtUtc = DateTime.UtcNow });
            await db.SaveChangesAsync();
            return Results.Ok(new { done = true });
        });

        admin.MapDelete("/players/{id:guid}/items/{key}", async (Guid id, string key, RuneDbContext db) =>
        {
            var row = await db.PlayerItems.FirstOrDefaultAsync(x => x.PlayerId == id && x.ItemKey == key);
            if (row is null) return Error("not_found", "The player does not own that item.", 404);
            db.PlayerItems.Remove(row);
            await db.SaveChangesAsync();
            return Results.Ok(new { done = true });
        });

        admin.MapPost("/players/{id:guid}/talismans", async (Guid id, AdminGrant body, RuneDbContext db) =>
        {
            if (await db.Players.FindAsync(id) is null) return Error("not_found", "Player not found.", 404);
            if (!await db.Talismans.AnyAsync(x => x.Key == body.Key)) return Error("not_found", "That talisman does not exist.", 404);
            if (await db.PlayerTalismans.AnyAsync(x => x.PlayerId == id && x.TalismanKey == body.Key)) return Error("owned", "The player already owns that talisman.", 409);
            db.PlayerTalismans.Add(new PlayerTalisman { Id = Guid.NewGuid(), PlayerId = id, TalismanKey = body.Key, AcquiredAtUtc = DateTime.UtcNow });
            await db.SaveChangesAsync();
            return Results.Ok(new { done = true });
        });

        admin.MapDelete("/players/{id:guid}/talismans/{key}", async (Guid id, string key, RuneDbContext db) =>
        {
            var row = await db.PlayerTalismans.FirstOrDefaultAsync(x => x.PlayerId == id && x.TalismanKey == key);
            if (row is null) return Error("not_found", "The player does not own that talisman.", 404);
            db.PlayerTalismans.Remove(row);
            await db.SaveChangesAsync();
            return Results.Ok(new { done = true });
        });

        admin.MapPost("/players/{id:guid}/letter", async (Guid id, AdminLetter body, RuneDbContext db) =>
        {
            if (await db.Players.FindAsync(id) is null) return Error("not_found", "Player not found.", 404);
            if (string.IsNullOrWhiteSpace(body.Subject) || string.IsNullOrWhiteSpace(body.Body)) return Error("missing", "Write a subject and a message.", 400);
            db.PlayerMessages.Add(new PlayerMessage { Id = Guid.NewGuid(), PlayerId = id, Sender = "The Rune Council", Subject = body.Subject.Trim(), Body = body.Body.Trim(), CreatedAtUtc = DateTime.UtcNow });
            await db.SaveChangesAsync();
            return Results.Ok(new { done = true });
        });

        // ---- Matches ----
        admin.MapGet("/matches", async (string? status, string? mode, string? q, RuneDbContext db) =>
        {
            var query = db.CombatMatches.AsNoTracking();
            if (status == "active") query = query.Where(x => x.CompletedAtUtc == null);
            if (status == "finished") query = query.Where(x => x.CompletedAtUtc != null);
            if (mode == "practice") query = query.Where(x => x.ShadeDifficulty != null);
            else if (mode is DuelMode.Ranked or DuelMode.Casual) query = query.Where(x => x.ShadeDifficulty == null && x.Mode == mode);
            if (!string.IsNullOrWhiteSpace(q))
            {
                var term = q.Trim().ToLowerInvariant();
                var ids = await db.Players.Where(x => x.Username.ToLower().Contains(term)).Select(x => x.Id).Take(50).ToListAsync();
                query = query.Where(x => ids.Contains(x.PlayerAId) || ids.Contains(x.PlayerBId));
            }
            return Results.Ok(await MatchRows(db, query.OrderByDescending(x => x.CreatedAtUtc).Take(60)));
        });

        admin.MapGet("/matches/{id:guid}", async (Guid id, RuneDbContext db) =>
        {
            var m = await db.CombatMatches.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            if (m is null) return Error("not_found", "Match not found.", 404);
            var row = (await MatchRows(db, db.CombatMatches.AsNoTracking().Where(x => x.Id == id))).First();
            var rounds = await db.CombatRounds.AsNoTracking().Where(x => x.MatchId == id).OrderBy(x => x.Turn)
                .Select(x => new { x.Turn, x.RuneA, x.FormA, x.StateA, x.RuneB, x.FormB, x.StateB, x.HpA, x.HpB, x.Log }).ToListAsync();
            return Results.Ok(new
            {
                match = row,
                now = new { m.HpA, m.HpB, m.ManaA, m.ManaB, m.ShieldA, m.ShieldB, m.LastLog, waitingA = m.PendingA is null, waitingB = m.PendingB is null },
                rewards = new { m.PointsDeltaA, m.PointsDeltaB, m.RatingDeltaA, m.RatingDeltaB, m.CoinsA, m.CoinsB },
                talismanUses = new { a = m.TalismanUsesA, b = m.TalismanUsesB },
                rounds
            });
        });

        // Closes a stuck duel with no winner and no rewards.
        admin.MapPost("/matches/{id:guid}/close", async (Guid id, RuneDbContext db) =>
        {
            var m = await db.CombatMatches.FindAsync(id);
            if (m is null) return Error("not_found", "Match not found.", 404);
            if (m.CompletedAtUtc is not null) return Error("finished", "This duel has already ended.", 409);
            m.CompletedAtUtc = DateTime.UtcNow;
            m.IsDraw = true;
            m.ResultsApplied = true;
            m.LastLog = "This duel was closed by an admin. No result was recorded.";
            await db.MatchQueue.Where(x => x.CombatMatchId == id).ExecuteDeleteAsync();
            await db.SaveChangesAsync();
            return Results.Ok(new { done = true });
        });
    }

    public static object PlayerRow(Player p) => new
    {
        p.Id, p.Username, p.Email, p.ClassName, p.League, p.Points, p.SkillRating, p.SeasonMatches, p.SeasonWins, p.Coins, p.Gems,
        p.IsAdmin, p.Banned, p.TutorialDone, p.StoryChapter, p.CreatedAtUtc, p.LastSeenAtUtc,
        p.Xp, level = Progression.LevelFor(p.Xp), avatarUrl = ProfileEndpoints.AvatarUrl(p), p.Motto, p.WinStreak, p.BestWinStreak, p.BestLeague
    };

    private static async Task<List<object>> MatchRows(RuneDbContext db, IQueryable<CombatMatch> query)
    {
        var rows = await query.Select(x => new
        {
            x.Id, x.PlayerAId, x.PlayerBId, x.ClassA, x.ClassB, x.TalismanA, x.TalismanB, x.WinnerId, x.IsDraw, x.Mode,
            x.ShadeDifficulty, x.Turn, x.HpA, x.HpB, x.CreatedAtUtc, x.CompletedAtUtc
        }).ToListAsync();
        var ids = rows.SelectMany(x => new[] { x.PlayerAId, x.PlayerBId }).Distinct().ToList();
        var names = await db.Players.AsNoTracking().Where(x => ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Username);
        string Name(Guid id) => id == ShadeOpponent.Id ? "The Shade" : names.GetValueOrDefault(id, "(deleted)");
        return rows.Select(x => (object)new
        {
            x.Id,
            playerA = new { id = x.PlayerAId, name = Name(x.PlayerAId), className = x.ClassA, talisman = x.TalismanA, hp = x.HpA },
            playerB = new { id = x.PlayerBId, name = Name(x.PlayerBId), className = x.ClassB, talisman = x.TalismanB, hp = x.HpB },
            mode = x.ShadeDifficulty is not null ? $"practice ({x.ShadeDifficulty})" : x.Mode,
            status = x.CompletedAtUtc is null ? "in progress" : x.IsDraw ? "draw" : "finished",
            winner = x.WinnerId is Guid w ? Name(w) : null,
            turns = x.Turn,
            x.CreatedAtUtc,
            x.CompletedAtUtc
        }).ToList();
    }

    private static IResult Error(string code, string message, int status) => Results.Json(new { error = new { code, message } }, statusCode: status);
}
