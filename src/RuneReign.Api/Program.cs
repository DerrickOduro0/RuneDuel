using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using RuneReign.Api;
using RuneReign.Domain;

var builder = WebApplication.CreateBuilder(args);
// GameRules.json only seeds the database on first start. After that the database is the source of truth.
var seedRules = ContentStore.ReadSeedRules(Path.Combine(AppContext.BaseDirectory, "GameRules.json"));

var connection = builder.Configuration.GetConnectionString("MySql");
var sqliteConnection = builder.Configuration.GetConnectionString("Sqlite") ?? "Data Source=runereign.db";
if (!string.IsNullOrWhiteSpace(connection))
{
    builder.Services.AddDbContext<RuneDbContext>(options => options.UseMySql(connection, ServerVersion.AutoDetect(connection)));
}
else
{
    builder.Services.AddDbContext<RuneDbContext>(options => options.UseSqlite(sqliteConnection));
}

var jwtKey = builder.Configuration["Jwt:Key"] ?? "development-only-change-this-jwt-key-32-chars";
builder.Services.AddSingleton(sp =>
{
    using var scope = sp.CreateScope();
    return ContentStore.LoadRules(scope.ServiceProvider.GetRequiredService<RuneDbContext>(), seedRules);
});
builder.Services.AddSingleton(sp =>
{
    using var scope = sp.CreateScope();
    return ContentStore.LoadServerSettings(scope.ServiceProvider.GetRequiredService<RuneDbContext>());
});
builder.Services.AddSingleton<IPasswordHasher<Player>, PasswordHasher<Player>>();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
        ValidateIssuer = false,
        ValidateAudience = false,
        ClockSkew = TimeSpan.FromMinutes(1)
    };
});
builder.Services.AddAuthorization();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHostedService<WheelWorker>();

var app = builder.Build();
app.UseSwagger();
app.UseSwaggerUI();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
if (!string.IsNullOrWhiteSpace(connection))
{
    using var migrationScope = app.Services.CreateScope();
    migrationScope.ServiceProvider.GetRequiredService<RuneDbContext>().Database.Migrate();
}
else
{
    using var developmentScope = app.Services.CreateScope();
    var developmentDb = developmentScope.ServiceProvider.GetRequiredService<RuneDbContext>();
    developmentDb.Database.EnsureCreated();
    EnsureSqliteSchema(developmentDb);
}
using (var seedScope = app.Services.CreateScope())
    ContentStore.Seed(seedScope.ServiceProvider.GetRequiredService<RuneDbContext>(), seedRules);

app.MapRealmEndpoints();

app.MapGet("/api/v1/classes", (GameRules r) => Results.Ok(r.Classes));

app.MapPost("/api/v1/auth/register", async (RegisterRequest request, RuneDbContext db, IPasswordHasher<Player> hasher) =>
{
    if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
        return ApiError("validation_error", "Username, email, and password are required.", 400);
    if (request.Password.Length < 8)
        return ApiError("validation_error", "Password must be at least 8 characters.", 400);
    if (await db.Players.AnyAsync(x => x.Username == request.Username.Trim() || x.Email == request.Email.Trim().ToLowerInvariant()))
        return ApiError("account_exists", "Username or email is already in use.", 409);

    var player = new Player { Id = Guid.NewGuid(), Username = request.Username.Trim(), Email = request.Email.Trim().ToLowerInvariant(), PasswordHash = "" };
    player.PasswordHash = hasher.HashPassword(player, request.Password);
    db.Players.Add(player);
    await db.SaveChangesAsync();
    return Results.Ok(new { token = CreateToken(player, jwtKey), needsClass = true });
});

app.MapPost("/api/v1/auth/login", async (LoginRequest request, RuneDbContext db, IPasswordHasher<Player> hasher) =>
{
    if (string.IsNullOrWhiteSpace(request.Identifier) || string.IsNullOrWhiteSpace(request.Password))
        return ApiError("validation_error", "Username/email and password are required.", 400);
    var identifier = request.Identifier.Trim().ToLowerInvariant();
    var player = await db.Players.FirstOrDefaultAsync(x => x.Email == identifier || x.Username.ToLower() == identifier);
    if (player is null)
        return ApiError("unknown_account", "No account matches those details.", 401);
    if (hasher.VerifyHashedPassword(player, player.PasswordHash, request.Password) == PasswordVerificationResult.Failed)
        return ApiError("wrong_password", "The password is incorrect.", 401);
    return Results.Ok(new { token = CreateToken(player, jwtKey), needsClass = player.ClassName is null });
});

app.MapGet("/api/v1/me", async (ClaimsPrincipal user, RuneDbContext db, GameRules r, ServerSettings server) =>
{
    var player = await CurrentPlayer(user, db);
    if (player is null) return ApiError("not_found", "Player not found.", 404);
    await RealmEndpoints.EnsureRealm(db, player, server);
    return Results.Ok(PlayerDto(player, r));
}).RequireAuthorization();

app.MapPut("/api/v1/me/class", async (ChooseClassRequest request, ClaimsPrincipal user, RuneDbContext db, GameRules r) =>
{
    var player = await CurrentPlayer(user, db);
    if (player is null) return ApiError("not_found", "Player not found.", 404);
    if (player.ClassName is not null) return ApiError("class_locked", "Class selection is already complete.", 409);
    if (r.Classes.All(x => x.Name != request.ClassName)) return ApiError("invalid_class", "Choose one of the listed classes.", 400);
    player.ClassName = request.ClassName;
    await db.SaveChangesAsync();
    return Results.Ok(PlayerDto(player, r));
}).RequireAuthorization();

app.MapGet("/api/v1/league", async (RuneDbContext db, GameRules r) =>
{
    var players = await db.Players.OrderBy(x => x.League).ThenByDescending(x => x.Points).ThenBy(x => x.Username).Take(100).ToListAsync();
    return Results.Ok(new { leagues = r.Leagues, leaderboard = players.Select(x => new { x.Username, x.ClassName, x.League, leagueName = r.Leagues.First(l => l.Number == x.League).Name, x.Points }), hallOfFame = await db.HallOfFame.OrderByDescending(x => x.CrownedAtUtc).ToListAsync() });
});

app.MapPost("/api/v1/matchmaking/queue", async (ClaimsPrincipal user, RuneDbContext db, GameRules r, ServerSettings server, QueueRequest? request) =>
{
    var mode = (request?.Mode ?? DuelMode.Ranked).ToLowerInvariant();
    if (!DuelMode.IsQueueMode(mode)) return ApiError("invalid_mode", "Choose ranked or casual.", 400);
    var player = await CurrentPlayer(user, db);
    if (player is null || player.ClassName is null) return ApiError("class_required", "Choose a class before matchmaking.", 400);
    if (await ActiveMatch(db, player.Id, server) is not null)
        return ApiError("match_in_progress", "You still have a duel in progress. Continue it or forfeit it first.", 409);
    await RemoveExpiredQueueEntries(db, r);
    var existing = await db.MatchQueue.FirstOrDefaultAsync(x => x.PlayerId == player.Id && !x.Matched);
    if (existing is not null)
    {
        existing.LastHeartbeatUtc = DateTime.UtcNow;
        // Switching modes starts the wait again.
        if (existing.Mode != mode) { existing.Mode = mode; existing.JoinedAtUtc = DateTime.UtcNow; }
        await db.SaveChangesAsync();
        await TryMatch(db, r, server);
        return Results.Ok(new { queued = true, status = "searching", mode });
    }

    var now = DateTime.UtcNow;
    var entry = new MatchQueueEntry { Id = Guid.NewGuid(), PlayerId = player.Id, JoinedAtUtc = now, LastHeartbeatUtc = now, Mode = mode };
    db.MatchQueue.Add(entry);
    await db.SaveChangesAsync();
    await TryMatch(db, r, server);
    return Results.Ok(new { queued = true, status = "searching", mode });
}).RequireAuthorization();

app.MapGet("/api/v1/matchmaking/status", async (ClaimsPrincipal user, RuneDbContext db, GameRules r, ServerSettings server) =>
{
    var player = await CurrentPlayer(user, db);
    if (player is null) return ApiError("not_found", "Player not found.", 404);
    await ResolveExpiredPendingMatches(db, server);
    await RemoveExpiredQueueEntries(db, r);
    var entry = await db.MatchQueue.FirstOrDefaultAsync(x => x.PlayerId == player.Id && !x.Matched);
    if (entry is not null)
    {
        entry.LastHeartbeatUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();
        await TryMatch(db, r, server);
    }

    var matched = await db.MatchQueue.FirstOrDefaultAsync(x => x.PlayerId == player.Id && x.Matched && x.CombatMatchId != null);
    if (matched?.CombatMatchId is Guid combatId)
    {
        var combat = await db.CombatMatches.FindAsync(combatId);
        if (combat is null || combat.CompletedAtUtc is not null)
        {
            await db.MatchQueue.Where(x => x.CombatMatchId == combatId).ExecuteDeleteAsync();
            return Results.Ok(new { status = "idle", matchId = (Guid?)null, opponent = (object?)null });
        }

        matched.LastHeartbeatUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();
        var isA = combat.PlayerAId == player.Id;
        if (combat.AcceptDeadlineUtc is DateTime deadline)
        {
            // Both players must press Enter Match before the countdown ends. The opponent stays hidden until then.
            var secondsLeft = (int)Math.Ceiling(Math.Max(0, (deadline - DateTime.UtcNow).TotalSeconds));
            return Results.Ok(new { status = "found", mode = combat.Mode, matchId = (Guid?)null, opponent = (object?)null, accepted = isA ? combat.AcceptedA : combat.AcceptedB, secondsLeft, acceptWindowSeconds = server.AcceptWindowSeconds });
        }

        var opponent = await db.Players.FindAsync(isA ? combat.PlayerBId : combat.PlayerAId);
        return Results.Ok(new { status = "ready", mode = combat.Mode, matchId = (Guid?)combat.Id, opponent = opponent is null ? null : new { opponent.Username, opponent.ClassName, opponent.League } });
    }
    return Results.Ok(new { status = entry is null ? "idle" : "searching", mode = entry?.Mode, matchId = (Guid?)null, opponent = (object?)null });
}).RequireAuthorization();

app.MapPost("/api/v1/matchmaking/accept", async (ClaimsPrincipal user, RuneDbContext db, ServerSettings server) =>
{
    var player = await CurrentPlayer(user, db);
    if (player is null) return ApiError("not_found", "Player not found.", 404);
    await ResolveExpiredPendingMatches(db, server);
    var entry = await db.MatchQueue.FirstOrDefaultAsync(x => x.PlayerId == player.Id && x.Matched && x.CombatMatchId != null);
    var combat = entry?.CombatMatchId is Guid combatId ? await db.CombatMatches.FindAsync(combatId) : null;
    if (combat is null || combat.AcceptDeadlineUtc is null)
        return ApiError("no_pending_match", "There is no match waiting for you to accept.", 409);
    if (combat.PlayerAId == player.Id) combat.AcceptedA = true; else combat.AcceptedB = true;
    if (combat.AcceptedA && combat.AcceptedB)
    {
        combat.AcceptDeadlineUtc = null;
        RollTurnDice(combat);
        combat.CreatedAtUtc = DateTime.UtcNow;
    }
    await db.SaveChangesAsync();
    return Results.Ok(new { accepted = true, ready = combat.AcceptDeadlineUtc is null });
}).RequireAuthorization();

app.MapDelete("/api/v1/matchmaking/queue", async (ClaimsPrincipal user, RuneDbContext db) =>
{
    var player = await CurrentPlayer(user, db);
    if (player is not null)
    {
        var mine = await db.MatchQueue.Where(x => x.PlayerId == player.Id).ToListAsync();
        var startedIds = new List<Guid>();
        foreach (var entry in mine.Where(x => x.Matched && x.CombatMatchId != null))
        {
            var combat = await db.CombatMatches.FindAsync(entry.CombatMatchId!.Value);
            if (combat is not null && combat.AcceptDeadlineUtc is not null)
            {
                // Leaving a match that is still waiting for acceptance sends the other player back to searching.
                var others = await db.MatchQueue.Where(x => x.CombatMatchId == combat.Id && x.PlayerId != player.Id).ToListAsync();
                foreach (var other in others)
                {
                    other.Matched = false; other.OpponentId = null; other.CombatMatchId = null;
                    other.LastHeartbeatUtc = DateTime.UtcNow;
                }
                db.CombatMatches.Remove(combat);
            }
            else startedIds.Add(entry.CombatMatchId!.Value);
        }
        db.MatchQueue.RemoveRange(mine);
        await db.SaveChangesAsync();
        if (startedIds.Count > 0)
            await db.MatchQueue.Where(x => x.CombatMatchId != null && startedIds.Contains(x.CombatMatchId.Value)).ExecuteDeleteAsync();
    }
    return Results.NoContent();
}).RequireAuthorization();

// Practice against the Shade: a computer opponent. Nothing in the league, rating or quests changes.
app.MapPost("/api/v1/shade/start", async (StartShadeRequest request, ClaimsPrincipal user, RuneDbContext db, GameRules rules, ServerSettings server) =>
{
    var player = await CurrentPlayer(user, db);
    if (player is null || player.ClassName is null) return ApiError("class_required", "Choose a class before you duel.", 400);
    var difficulty = (request.Difficulty ?? ShadeBot.Easy).ToLowerInvariant();
    if (!ShadeBot.IsDifficulty(difficulty)) return ApiError("invalid_difficulty", "Choose easy or hard.", 400);
    if (await ActiveMatch(db, player.Id, server) is not null)
        return ApiError("match_in_progress", "You still have a duel in progress. Continue it or forfeit it first.", 409);
    // Leave the ranked queue if the player was still searching.
    await db.MatchQueue.Where(x => x.PlayerId == player.Id && !x.Matched).ExecuteDeleteAsync();

    var classes = rules.Classes.Select(x => x.Name).ToList();
    var shadeClass = classes.Count > 0 ? classes[Random.Shared.Next(classes.Count)] : "Gravebound";
    var (_, gear) = await Equipment.Totals(db, player.Id);
    // The hard Shade wears modest gear; the easy one wears none.
    int[] shadeGear = difficulty == ShadeBot.Hard ? [1, 1, 0, 0, 1] : [0, 0, 0, 0, 0];
    var now = DateTime.UtcNow;
    var match = new CombatMatch
    {
        Id = Guid.NewGuid(),
        PlayerAId = player.Id,
        PlayerBId = ShadeOpponent.Id,
        ClassA = player.ClassName,
        ClassB = shadeClass,
        BonusesA = Equipment.Encode(gear),
        BonusesB = Equipment.Encode(shadeGear),
        ManaA = Math.Min(CombatEngine.ManaCap, CombatEngine.StartMana + gear[3]),
        ManaB = CombatEngine.StartMana,
        AcceptedA = true,
        AcceptedB = true,
        LastSeenA = now,
        LastSeenB = now,
        ShadeDifficulty = difficulty,
        LastLog = "The Shade rises from the mist. Read the dice, then declare your state."
    };
    RollTurnDice(match);
    db.CombatMatches.Add(match);
    await db.SaveChangesAsync();
    return Results.Ok(new { matchId = match.Id });
}).RequireAuthorization();

// Match history: the player's finished duels, newest first.
app.MapGet("/api/v1/matches", async (ClaimsPrincipal user, RuneDbContext db, int? take) =>
{
    var player = await CurrentPlayer(user, db);
    if (player is null) return ApiError("not_found", "Player not found.", 404);
    var limit = Math.Clamp(take ?? 20, 1, 50);
    var matches = await db.CombatMatches.AsNoTracking()
        .Where(x => (x.PlayerAId == player.Id || x.PlayerBId == player.Id) && x.CompletedAtUtc != null)
        .OrderByDescending(x => x.CompletedAtUtc)
        .Take(limit)
        .ToListAsync();
    var opponentIds = matches.Select(x => x.PlayerAId == player.Id ? x.PlayerBId : x.PlayerAId).Distinct().ToList();
    var names = await db.Players.AsNoTracking().Where(x => opponentIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Username);
    return Results.Ok(matches.Select(x => HistoryEntry(x, player.Id, names)));
}).RequireAuthorization();

app.MapGet("/api/v1/matches/{matchId:guid}", async (Guid matchId, ClaimsPrincipal user, RuneDbContext db) =>
{
    var player = await CurrentPlayer(user, db);
    var match = await db.CombatMatches.AsNoTracking().FirstOrDefaultAsync(x => x.Id == matchId);
    if (player is null || match is null || (match.PlayerAId != player.Id && match.PlayerBId != player.Id) || match.CompletedAtUtc is null)
        return ApiError("not_found", "Match not found.", 404);
    var isA = match.PlayerAId == player.Id;
    var names = await db.Players.AsNoTracking().Where(x => x.Id == match.PlayerAId || x.Id == match.PlayerBId).ToDictionaryAsync(x => x.Id, x => x.Username);
    var rounds = await db.CombatRounds.AsNoTracking().Where(x => x.MatchId == matchId).OrderBy(x => x.Turn).ToListAsync();
    return Results.Ok(new
    {
        match = HistoryEntry(match, player.Id, names),
        rounds = rounds.Select(r =>
        {
            var mine = isA ? (r.RuneA, r.FormA, r.StateA, r.HpA) : (r.RuneB, r.FormB, r.StateB, r.HpB);
            var theirs = isA ? (r.RuneB, r.FormB, r.StateB, r.HpB) : (r.RuneA, r.FormA, r.StateA, r.HpA);
            var clash = mine.Item3 == theirs.Item3 ? "even" : ShadeBot.Counter(theirs.Item3) == mine.Item3 ? "won" : "lost";
            return new
            {
                r.Turn,
                me = new { rune = mine.Item1, symbol = CombatEngine.GetRuneSymbol(mine.Item1), form = mine.Item2, state = mine.Item3, committed = CombatEngine.GetFormState(mine.Item2) == mine.Item3 },
                them = new { rune = theirs.Item1, symbol = CombatEngine.GetRuneSymbol(theirs.Item1), form = theirs.Item2, state = theirs.Item3, committed = CombatEngine.GetFormState(theirs.Item2) == theirs.Item3 },
                clash,
                myHp = mine.Item4,
                opponentHp = theirs.Item4
            };
        })
    });
}).RequireAuthorization();

// Finishing the guided first duel. The reward is paid once.
app.MapPost("/api/v1/tutorial/complete", async (ClaimsPrincipal user, RuneDbContext db, GameRules r, ServerSettings server, CompleteTutorialRequest? request) =>
{
    var player = await CurrentPlayer(user, db);
    if (player is null) return ApiError("not_found", "Player not found.", 404);
    var reward = 0;
    if (!player.TutorialDone)
    {
        player.TutorialDone = true;
        if (request?.Finished == true)
        {
            reward = server.TutorialCoins;
            player.Coins += reward;
        }
        await db.SaveChangesAsync();
    }
    return Results.Ok(new { reward, player = PlayerDto(player, r) });
}).RequireAuthorization();

app.MapGet("/api/v1/combat/active", async (ClaimsPrincipal user, RuneDbContext db, ServerSettings server) =>
{
    var player = await CurrentPlayer(user, db);
    if (player is null) return ApiError("not_found", "Player not found.", 404);
    var match = await ActiveMatch(db, player.Id, server);
    if (match is null) return Results.Ok(new { active = false });
    var isA = match.PlayerAId == player.Id;
    var opponentName = await OpponentName(db, match, player.Id);
    return Results.Ok(new
    {
        active = true,
        matchId = match.Id,
        practice = match.ShadeDifficulty,
        mode = match.Mode,
        opponent = opponentName,
        opponentClass = isA ? match.ClassB : match.ClassA,
        myClass = isA ? match.ClassA : match.ClassB,
        turn = match.Turn,
        myHp = isA ? match.HpA : match.HpB,
        opponentHp = isA ? match.HpB : match.HpA,
        waitingForYou = (isA ? match.PendingA : match.PendingB) is null && (isA ? match.PendingB : match.PendingA) is not null
    });
}).RequireAuthorization();

app.MapGet("/api/v1/combat/{matchId:guid}", async (Guid matchId, ClaimsPrincipal user, RuneDbContext db, GameRules rules, ServerSettings server) =>
{
    var player = await CurrentPlayer(user, db);
    var match = await db.CombatMatches.FindAsync(matchId);
    if (player is null || match is null || (match.PlayerAId != player.Id && match.PlayerBId != player.Id))
        return ApiError("not_found", "Combat match not found.", 404);
    if (match.CompletedAtUtc is null)
    {
        EnsureDice(match);
        MarkSeen(match, player.Id);
        await db.SaveChangesAsync();
    }
    return Results.Ok(CombatDto(match, player.Id, player.Username, await OpponentName(db, match, player.Id), rules, server));
}).RequireAuthorization();

app.MapPost("/api/v1/combat/{matchId:guid}/action", async (CombatActionRequest request, Guid matchId, ClaimsPrincipal user, RuneDbContext db, GameRules rules, ServerSettings server) =>
{
    var player = await CurrentPlayer(user, db);
    var match = await db.CombatMatches.FindAsync(matchId);
    if (player is null || match is null || (match.PlayerAId != player.Id && match.PlayerBId != player.Id))
        return ApiError("not_found", "Combat match not found.", 404);
    if (match.CompletedAtUtc is not null) return ApiError("match_complete", "This combat match is already complete.", 409);
    if (match.AcceptDeadlineUtc is not null) return ApiError("match_not_ready", "Both players must accept the match first.", 409);
    if (!new[] { "Break", "Flow", "Bind" }.Contains(request.State)) return ApiError("invalid_state", "Choose Break, Flow, or Bind.", 400);
    MarkSeen(match, player.Id);
    EnsureDice(match);
    var isA = match.PlayerAId == player.Id;
    // Declaring a state locks you in. The dice were already rolled and shaped.
    if ((isA ? match.PendingA : match.PendingB) is not null) return ApiError("action_already_submitted", "Your state is already locked in.", 409);
    if (isA) match.PendingA = request.State; else match.PendingB = request.State;

    if (match.ShadeDifficulty is not null && match.PendingB is null)
        match.PendingB = ShadeBot.Declare(match.ShadeDifficulty, match.FormB!, match.FormA!, History(match.StatesA), Random.Shared);

    if (match.PendingA is not null && match.PendingB is not null)
    {
        var a = new CombatAction(match.RuneA!, match.FormA!, match.PendingA, match.PendingD8AdjustmentA);
        var b = new CombatAction(match.RuneB!, match.FormB!, match.PendingB, match.PendingD8AdjustmentB);
        if (!CombatEngine.IsValidAction(match.ClassA, a) || !CombatEngine.IsValidAction(match.ClassB, b))
            return ApiError("invalid_roll", "The server generated an invalid combat roll.", 500);
        var result = CombatEngine.Resolve(match.ClassA, a, new CombatPlayerState(match.HpA, match.ShieldA, match.ManaA), match.ClassB, b, new CombatPlayerState(match.HpB, match.ShieldB, match.ManaB),
            Equipment.ToBonuses(Equipment.Decode(match.BonusesA)), Equipment.ToBonuses(Equipment.Decode(match.BonusesB)));
        match.HpA = result.PlayerA.Hp; match.ShieldA = result.PlayerA.Shield; match.ManaA = result.PlayerA.Mana;
        match.HpB = result.PlayerB.Hp; match.ShieldB = result.PlayerB.Shield; match.ManaB = result.PlayerB.Mana;
        match.UsedRunesA = AddUsedRune(match.UsedRunesA, a.Rune);
        match.UsedRunesB = AddUsedRune(match.UsedRunesB, b.Rune);
        match.LastRollA = JsonSerializer.Serialize(new { a.Rune, Symbol = CombatEngine.GetRuneSymbol(a.Rune), a.Form, a.State, a.D8Adjustment, Picked = match.PendingFormA is not null });
        match.LastRollB = JsonSerializer.Serialize(new { b.Rune, Symbol = CombatEngine.GetRuneSymbol(b.Rune), b.Form, b.State, b.D8Adjustment, Picked = match.PendingFormB is not null });
        db.CombatRounds.Add(new CombatRound
        {
            Id = Guid.NewGuid(), MatchId = match.Id, Turn = match.Turn,
            RuneA = a.Rune, FormA = a.Form, StateA = a.State,
            RuneB = b.Rune, FormB = b.Form, StateB = b.State,
            HpA = result.PlayerA.Hp, HpB = result.PlayerB.Hp, Log = result.Log
        });
        match.StatesA = AddHistory(match.StatesA, a.State);
        match.StatesB = AddHistory(match.StatesB, b.State);
        match.LastLog = result.Log; match.Turn++;
        match.PendingA = null; match.PendingB = null;
        if (result.Winner is not null || result.IsDraw)
        {
            match.WinnerId = result.Winner is null ? null : result.Winner == "A" ? match.PlayerAId : match.PlayerBId;
            match.IsDraw = result.IsDraw;
            match.CompletedAtUtc = DateTime.UtcNow;
            await ApplyResult(db, match, match.WinnerId, rules, server);
        }
        else
        {
            // The next turn's dice hit the table straight away.
            RollTurnDice(match);
        }
    }
    await db.SaveChangesAsync();
    return Results.Ok(CombatDto(match, player.Id, player.Username, await OpponentName(db, match, player.Id), rules, server));
}).RequireAuthorization();

// Shape your dice before declaring. Everything here is visible to the opponent.
app.MapPost("/api/v1/combat/{matchId:guid}/shape", async (ShapeDiceRequest request, Guid matchId, ClaimsPrincipal user, RuneDbContext db, GameRules rules, ServerSettings server) =>
{
    var player = await CurrentPlayer(user, db);
    var match = await db.CombatMatches.FindAsync(matchId);
    if (player is null || match is null || (match.PlayerAId != player.Id && match.PlayerBId != player.Id))
        return ApiError("not_found", "Combat match not found.", 404);
    if (match.CompletedAtUtc is not null) return ApiError("match_complete", "This combat match is already complete.", 409);
    if (match.AcceptDeadlineUtc is not null) return ApiError("match_not_ready", "Both players must accept the match first.", 409);
    MarkSeen(match, player.Id);
    EnsureDice(match);
    var isA = match.PlayerAId == player.Id;
    if ((isA ? match.PendingA : match.PendingB) is not null) return ApiError("already_locked", "You have already declared this turn.", 409);
    var className = isA ? match.ClassA : match.ClassB;
    var mana = isA ? match.ManaA : match.ManaB;
    var form = (isA ? match.FormA : match.FormB)!;

    switch (request.Kind)
    {
        case "reroll":
        {
            if (isA ? match.PendingRerollA : match.PendingRerollB) return ApiError("already_rerolled", "You can reroll your rune once per turn.", 409);
            var hp = isA ? match.HpA : match.HpB;
            var (manaCost, hpCost) = RerollCost(match, isA);
            // Flamecallers pay with blood instead of mana.
            if (hpCost > 0)
            {
                if (hp <= hpCost) return ApiError("not_enough_hp", "You need more HP to reroll.", 400);
                if (isA) match.HpA -= hpCost; else match.HpB -= hpCost;
            }
            if (manaCost > 0)
            {
                if (mana < manaCost) return ApiError("not_enough_mana", $"Rerolling your rune costs {manaCost} mana.", 400);
                if (isA) match.ManaA -= manaCost; else match.ManaB -= manaCost;
            }
            // A reroll always lands on a different rune.
            var current = (isA ? match.RuneA : match.RuneB)!;
            var used = ParseRunes(isA ? match.UsedRunesA : match.UsedRunesB).Append(current).ToList();
            var rune = CombatEngine.RollRune(className, used, Random.Shared);
            if (rune == current) rune = CombatEngine.RollRune(className, [current], Random.Shared);
            if (isA) { match.RuneA = rune; match.PendingRerollA = true; } else { match.RuneB = rune; match.PendingRerollB = true; }
            break;
        }
        case "pick":
        {
            if (request.Form is null || !CombatEngine.GetForms().Contains(request.Form)) return ApiError("invalid_form", "That form does not exist on the D8.", 400);
            if ((isA ? match.PendingFormA : match.PendingFormB) is not null) return ApiError("already_picked", "You have already chosen your D8 this turn.", 409);
            if (mana < CombatEngine.FormPickCost) return ApiError("not_enough_mana", $"Choosing a form costs {CombatEngine.FormPickCost} mana.", 400);
            if (isA) { match.ManaA -= CombatEngine.FormPickCost; match.FormA = request.Form; match.PendingFormA = request.Form; }
            else { match.ManaB -= CombatEngine.FormPickCost; match.FormB = request.Form; match.PendingFormB = request.Form; }
            break;
        }
        case "shift":
        {
            if (className != "Fateweaver") return ApiError("invalid_shift", "Only Fateweavers can shift the D8.", 400);
            if (request.Direction is not (1 or -1)) return ApiError("invalid_shift", "Shift the D8 up (+1) or down (-1).", 400);
            if ((isA ? match.PendingD8AdjustmentA : match.PendingD8AdjustmentB) != 0) return ApiError("already_shifted", "You can shift the D8 once per turn.", 409);
            if ((isA ? match.PendingFormA : match.PendingFormB) is not null) return ApiError("already_picked", "You chose this D8 face, so it cannot be shifted.", 409);
            var shifted = CombatEngine.ShiftForm(form, request.Direction);
            if (shifted == form) return ApiError("invalid_shift", "The D8 cannot move further that way.", 400);
            if (isA) { match.FormA = shifted; match.PendingD8AdjustmentA = request.Direction; } else { match.FormB = shifted; match.PendingD8AdjustmentB = request.Direction; }
            break;
        }
        default:
            return ApiError("invalid_shape", "Choose reroll, pick or shift.", 400);
    }
    await db.SaveChangesAsync();
    return Results.Ok(CombatDto(match, player.Id, player.Username, await OpponentName(db, match, player.Id), rules, server));
}).RequireAuthorization();

app.MapPost("/api/v1/combat/{matchId:guid}/give-up", async (Guid matchId, ClaimsPrincipal user, RuneDbContext db, GameRules rules, ServerSettings server) =>
{
    var player = await CurrentPlayer(user, db);
    var match = await db.CombatMatches.FindAsync(matchId);
    if (player is null || match is null || (match.PlayerAId != player.Id && match.PlayerBId != player.Id))
        return ApiError("not_found", "Combat match not found.", 404);
    if (match.CompletedAtUtc is not null)
        return ApiError("match_complete", "This combat match is already complete.", 409);

    await FinishByForfeit(db, match, player.Id, rules, server, $"{player.Username} forfeited the duel.");
    return Results.Ok(CombatDto(match, player.Id, player.Username, await OpponentName(db, match, player.Id), rules, server));
}).RequireAuthorization();

// When the opponent has left the duel for long enough, the player who stayed can take the win.
app.MapPost("/api/v1/combat/{matchId:guid}/claim-win", async (Guid matchId, ClaimsPrincipal user, RuneDbContext db, GameRules rules, ServerSettings server) =>
{
    var player = await CurrentPlayer(user, db);
    var match = await db.CombatMatches.FindAsync(matchId);
    if (player is null || match is null || (match.PlayerAId != player.Id && match.PlayerBId != player.Id))
        return ApiError("not_found", "Combat match not found.", 404);
    if (match.CompletedAtUtc is not null)
        return ApiError("match_complete", "This combat match is already complete.", 409);
    var away = OpponentAwaySeconds(match, player.Id);
    if (away < server.AwayClaimSeconds)
        return ApiError("opponent_present", $"Your opponent is still in the duel. You can claim the win after {server.AwayClaimSeconds} seconds away.", 409);

    var opponentId = match.PlayerAId == player.Id ? match.PlayerBId : match.PlayerAId;
    var opponentName = await OpponentName(db, match, player.Id);
    MarkSeen(match, player.Id);
    await FinishByForfeit(db, match, opponentId, rules, server, $"{opponentName} left the duel and forfeited.");
    return Results.Ok(CombatDto(match, player.Id, player.Username, opponentName, rules, server));
}).RequireAuthorization();

app.MapPost("/api/v1/test/results", async (TestResultRequest request, ClaimsPrincipal user, RuneDbContext db, GameRules r, IWebHostEnvironment env) =>
{
    if (env.IsProduction()) return ApiError("disabled", "Test endpoints are disabled in production.", 404);
    var player = await CurrentPlayer(user, db);
    if (player is null || request.Win is null) return ApiError("validation_error", "Provide win: true or false.", 400);
    var next = LeagueEngine.RecordResult(new LeagueState(player.League, player.Points, player.SeasonWins), request.Win.Value, r);
    player.League = next.League; player.Points = next.Points; player.SeasonWins = next.SeasonWins; player.SeasonMatches++;
    await db.SaveChangesAsync();
    return Results.Ok(PlayerDto(player, r));
}).RequireAuthorization();

app.MapPost("/api/v1/test/wheel", async (RuneDbContext db, GameRules r, IWebHostEnvironment env) =>
{
    if (env.IsProduction()) return ApiError("disabled", "Test endpoints are disabled in production.", 404);
    await WheelWorker.Turn(db, r, LeagueEngine.SeasonKey(DateTime.UtcNow));
    return Results.Ok(new { status = "completed" });
}).RequireAuthorization();

// ---- Friend challenges ----
// The host creates a code and shares it. Whoever enters it first starts a duel with the host straight away.
app.MapPost("/api/v1/challenges", async (ClaimsPrincipal user, RuneDbContext db, ServerSettings server) =>
{
    var player = await CurrentPlayer(user, db);
    if (player is null || player.ClassName is null) return ApiError("class_required", "Choose a class before you challenge anyone.", 400);
    if (await ActiveMatch(db, player.Id, server) is not null)
        return ApiError("match_in_progress", "You still have a duel in progress. Continue it or forfeit it first.", 409);
    var open = await OpenChallenge(db, player.Id);
    if (open is null)
    {
        string code;
        do code = ChallengeCode(); while (await db.FriendChallenges.AnyAsync(x => x.Code == code));
        open = new FriendChallenge { Id = Guid.NewGuid(), Code = code, HostId = player.Id, ExpiresAtUtc = DateTime.UtcNow.AddMinutes(server.ChallengeMinutes) };
        db.FriendChallenges.Add(open);
        await db.SaveChangesAsync();
    }
    return Results.Ok(ChallengeDto(open));
}).RequireAuthorization();

app.MapGet("/api/v1/challenges/mine", async (ClaimsPrincipal user, RuneDbContext db) =>
{
    var player = await CurrentPlayer(user, db);
    if (player is null) return ApiError("not_found", "Player not found.", 404);
    // A challenge someone has just accepted is reported once so the host can enter the duel.
    var accepted = await db.FriendChallenges
        .Where(x => x.HostId == player.Id && x.CombatMatchId != null)
        .OrderByDescending(x => x.CreatedAtUtc).FirstOrDefaultAsync();
    if (accepted is not null)
    {
        var match = await db.CombatMatches.FindAsync(accepted.CombatMatchId!.Value);
        db.FriendChallenges.Remove(accepted);
        await db.SaveChangesAsync();
        if (match is not null && match.CompletedAtUtc is null)
            return Results.Ok(new { status = "accepted", matchId = (Guid?)match.Id, code = accepted.Code, expiresInSeconds = 0 });
    }
    var open = await OpenChallenge(db, player.Id);
    return open is null
        ? Results.Ok(new { status = "none", matchId = (Guid?)null, code = (string?)null, expiresInSeconds = 0 })
        : Results.Ok(ChallengeDto(open));
}).RequireAuthorization();

app.MapDelete("/api/v1/challenges/mine", async (ClaimsPrincipal user, RuneDbContext db) =>
{
    var player = await CurrentPlayer(user, db);
    if (player is not null)
        await db.FriendChallenges.Where(x => x.HostId == player.Id && x.CombatMatchId == null).ExecuteDeleteAsync();
    return Results.NoContent();
}).RequireAuthorization();

app.MapPost("/api/v1/challenges/join", async (JoinChallengeRequest request, ClaimsPrincipal user, RuneDbContext db, ServerSettings server) =>
{
    var player = await CurrentPlayer(user, db);
    if (player is null || player.ClassName is null) return ApiError("class_required", "Choose a class before you duel.", 400);
    var code = (request.Code ?? "").Trim().ToUpperInvariant();
    var challenge = await db.FriendChallenges.FirstOrDefaultAsync(x => x.Code == code && x.CombatMatchId == null);
    if (challenge is null || challenge.ExpiresAtUtc < DateTime.UtcNow)
        return ApiError("unknown_code", "That code is not open. Ask your friend for a new one.", 404);
    if (challenge.HostId == player.Id)
        return ApiError("own_code", "That is your own code. Send it to a friend.", 400);
    if (await ActiveMatch(db, player.Id, server) is not null)
        return ApiError("match_in_progress", "You still have a duel in progress. Continue it or forfeit it first.", 409);
    var host = await db.Players.FindAsync(challenge.HostId);
    if (host is null || host.ClassName is null)
        return ApiError("unknown_code", "That code is not open. Ask your friend for a new one.", 404);
    if (await ActiveMatch(db, host.Id, server) is not null)
        return ApiError("host_busy", $"{host.Username} is in another duel right now. Try again when it ends.", 409);

    var match = await StartDuelNow(db, host, player, DuelMode.Friend);
    challenge.CombatMatchId = match.Id;
    // Neither player should stay in a queue or keep another open code.
    await db.SaveChangesAsync();
    await db.MatchQueue.Where(x => (x.PlayerId == host.Id || x.PlayerId == player.Id) && !x.Matched).ExecuteDeleteAsync();
    await db.FriendChallenges.Where(x => x.HostId == player.Id && x.CombatMatchId == null).ExecuteDeleteAsync();
    return Results.Ok(new { matchId = match.Id, opponent = host.Username });
}).RequireAuthorization();

// ---- Rematch ----
// After a casual or friend duel either player can ask for another. When both have asked, a new duel starts.
app.MapPost("/api/v1/combat/{matchId:guid}/rematch", async (Guid matchId, ClaimsPrincipal user, RuneDbContext db, GameRules rules, ServerSettings server) =>
{
    var player = await CurrentPlayer(user, db);
    var match = await db.CombatMatches.FindAsync(matchId);
    if (player is null || match is null || (match.PlayerAId != player.Id && match.PlayerBId != player.Id))
        return ApiError("not_found", "Combat match not found.", 404);
    var isA = match.PlayerAId == player.Id;
    var state = RematchDto(match, isA, server);
    if (match.RematchMatchId is null)
    {
        if (!state.Available)
            return ApiError("no_rematch", match.Mode == DuelMode.Ranked ? "Ranked duels have no rematch. Find a new opponent." : "The rematch window has closed.", 409);
        if (isA) match.RematchA = true; else match.RematchB = true;
        if (match.RematchA && match.RematchB)
        {
            var me = player;
            var them = await db.Players.FindAsync(isA ? match.PlayerBId : match.PlayerAId);
            if (them is null || them.ClassName is null || me.ClassName is null)
                return ApiError("no_rematch", "Your opponent is no longer available.", 409);
            if (await ActiveMatch(db, me.Id, server) is not null || await ActiveMatch(db, them.Id, server) is not null)
                return ApiError("match_in_progress", "One of you is already in another duel.", 409);
            // Swap sides so the other player is listed first.
            var next = await StartDuelNow(db, them, me, match.Mode);
            match.RematchMatchId = next.Id;
        }
        await db.SaveChangesAsync();
    }
    return Results.Ok(CombatDto(match, player.Id, player.Username, await OpponentName(db, match, player.Id), rules, server));
}).RequireAuthorization();

app.Run();

// A duel between two players with their gear fixed at this moment, so changing equipment mid-duel has no effect.
static async Task<CombatMatch> NewDuel(RuneDbContext db, Player a, Player b, string mode)
{
    var (_, gearA) = await Equipment.Totals(db, a.Id);
    var (_, gearB) = await Equipment.Totals(db, b.Id);
    return new CombatMatch
    {
        Id = Guid.NewGuid(), PlayerAId = a.Id, PlayerBId = b.Id,
        ClassA = a.ClassName!, ClassB = b.ClassName!,
        BonusesA = Equipment.Encode(gearA), BonusesB = Equipment.Encode(gearB),
        ManaA = Math.Min(CombatEngine.ManaCap, CombatEngine.StartMana + gearA[3]),
        ManaB = Math.Min(CombatEngine.ManaCap, CombatEngine.StartMana + gearB[3]),
        Mode = mode
    };
}

// Friend duels and rematches: both players already agreed, so there is no Enter Match step.
static async Task<CombatMatch> StartDuelNow(RuneDbContext db, Player a, Player b, string mode)
{
    var match = await NewDuel(db, a, b, mode);
    var now = DateTime.UtcNow;
    match.AcceptedA = true; match.AcceptedB = true;
    match.LastSeenA = now; match.LastSeenB = now; match.CreatedAtUtc = now;
    RollTurnDice(match);
    db.CombatMatches.Add(match);
    return match;
}

static Task<FriendChallenge?> OpenChallenge(RuneDbContext db, Guid hostId)
{
    var now = DateTime.UtcNow;
    return db.FriendChallenges.Where(x => x.HostId == hostId && x.CombatMatchId == null && x.ExpiresAtUtc > now)
        .OrderByDescending(x => x.CreatedAtUtc).FirstOrDefaultAsync();
}

// Six characters without look-alikes (no 0/O, 1/I/L).
static string ChallengeCode()
{
    const string alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
    return new string(Enumerable.Range(0, 6).Select(_ => alphabet[Random.Shared.Next(alphabet.Length)]).ToArray());
}

static object ChallengeDto(FriendChallenge c) => new
{
    status = "open", matchId = (Guid?)null, code = c.Code,
    expiresInSeconds = (int)Math.Max(0, (c.ExpiresAtUtc - DateTime.UtcNow).TotalSeconds)
};

static RematchState RematchDto(CombatMatch match, bool isA, ServerSettings server)
{
    var open = match.CompletedAtUtc is DateTime done && match.ShadeDifficulty is null && match.Mode != DuelMode.Ranked
        && (DateTime.UtcNow - done).TotalSeconds <= server.RematchSeconds;
    return new RematchState(
        open && match.RematchMatchId is null,
        isA ? match.RematchA : match.RematchB,
        isA ? match.RematchB : match.RematchA,
        match.RematchMatchId,
        match.CompletedAtUtc is DateTime end ? (int)Math.Max(0, server.RematchSeconds - (DateTime.UtcNow - end).TotalSeconds) : 0);
}

static IResult ApiError(string code, string message, int status) => Results.Json(new { error = new { code, message } }, statusCode: status);
static async Task<Player?> CurrentPlayer(ClaimsPrincipal user, RuneDbContext db) => Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? await db.Players.FindAsync(id) : null;
static object PlayerDto(Player p, GameRules r) => new { p.Username, p.Email, p.ClassName, p.League, leagueName = r.Leagues.FirstOrDefault(x => x.Number == p.League)?.Name ?? $"League {p.League}", p.Points, pointsPerLeague = r.PointsPerLeague, p.SeasonMatches, p.SeasonWins, p.Coins, p.Gems, p.TutorialDone };

// Records a finished duel once: league points for a win or loss, a match for both players, and new skill ratings.
static async Task ApplyResult(RuneDbContext db, CombatMatch match, Guid? winnerId, GameRules rules, ServerSettings server)
{
    // Practice duels against the Shade are never recorded.
    if (match.ShadeDifficulty is not null) match.ResultsApplied = true;
    // Casual and friend duels: no league points, rating or season matches. Casual pays a few coins; friend duels pay nothing,
    // so two accounts cannot farm each other.
    if (!match.ResultsApplied && match.Mode != DuelMode.Ranked)
    {
        if (match.Mode == DuelMode.Casual)
        {
            var playerA = await db.Players.FindAsync(match.PlayerAId);
            var playerB = await db.Players.FindAsync(match.PlayerBId);
            match.CoinsA = winnerId == match.PlayerAId ? server.CasualWinCoins : server.CasualLossCoins;
            match.CoinsB = winnerId == match.PlayerBId ? server.CasualWinCoins : server.CasualLossCoins;
            if (playerA is not null) playerA.Coins += match.CoinsA;
            if (playerB is not null) playerB.Coins += match.CoinsB;
        }
        match.ResultsApplied = true;
    }
    if (!match.ResultsApplied)
    {
        var playerA = await db.Players.FindAsync(match.PlayerAId);
        var playerB = await db.Players.FindAsync(match.PlayerBId);
        if (playerA is not null && playerB is not null)
        {
            var beforeA = (playerA.League, playerA.Points, playerA.SkillRating);
            var beforeB = (playerB.League, playerB.Points, playerB.SkillRating);
            if (winnerId is not null)
            {
                var nextA = LeagueEngine.RecordResult(new LeagueState(playerA.League, playerA.Points, playerA.SeasonWins), winnerId == match.PlayerAId, rules);
                var nextB = LeagueEngine.RecordResult(new LeagueState(playerB.League, playerB.Points, playerB.SeasonWins), winnerId == match.PlayerBId, rules);
                playerA.League = nextA.League; playerA.Points = nextA.Points; playerA.SeasonWins = nextA.SeasonWins;
                playerB.League = nextB.League; playerB.Points = nextB.Points; playerB.SeasonWins = nextB.SeasonWins;
            }
            playerA.SeasonMatches++;
            playerB.SeasonMatches++;
            var score = winnerId is null ? 0.5 : winnerId == match.PlayerAId ? 1.0 : 0.0;
            (playerA.SkillRating, playerB.SkillRating) = LeagueEngine.UpdateRatings(playerA.SkillRating, playerB.SkillRating, score);

            // Coins for playing ranked: more for a win, something for every finished duel.
            match.CoinsA = winnerId == match.PlayerAId ? server.WinCoins : server.LossCoins;
            match.CoinsB = winnerId == match.PlayerBId ? server.WinCoins : server.LossCoins;
            playerA.Coins += match.CoinsA;
            playerB.Coins += match.CoinsB;

            // Kept for the results screen and match history. A promotion counts as a full league of points.
            match.LeagueChangeA = beforeA.League - playerA.League;
            match.LeagueChangeB = beforeB.League - playerB.League;
            match.PointsDeltaA = match.LeagueChangeA * rules.PointsPerLeague + playerA.Points - beforeA.Points;
            match.PointsDeltaB = match.LeagueChangeB * rules.PointsPerLeague + playerB.Points - beforeB.Points;
            match.RatingDeltaA = playerA.SkillRating - beforeA.SkillRating;
            match.RatingDeltaB = playerB.SkillRating - beforeB.SkillRating;
            match.ResultsApplied = true;
        }
    }
    await db.MatchQueue.Where(x => x.CombatMatchId == match.Id).ExecuteDeleteAsync();
}

// A duel the player can continue: both players entered it and it has not finished.
static async Task<CombatMatch?> ActiveMatch(RuneDbContext db, Guid playerId, ServerSettings server)
{
    await CloseDeadMatches(db, server);
    return await db.CombatMatches
        .Where(x => (x.PlayerAId == playerId || x.PlayerBId == playerId) && x.CompletedAtUtc == null && x.AcceptDeadlineUtc == null && x.AcceptedA && x.AcceptedB)
        .OrderByDescending(x => x.CreatedAtUtc)
        .FirstOrDefaultAsync();
}

// Removes unfinished duels that can never be played:
// - ones from before the Enter Match step, which neither player accepted;
// - ones that neither player has looked at for AbandonedMatchMinutes.
// They are deleted with no league result, and their queue rows go with them so both players can search again.
static async Task CloseDeadMatches(RuneDbContext db, ServerSettings server)
{
    var cutoff = DateTime.UtcNow.AddMinutes(-server.AbandonedMatchMinutes);
    var dead = await db.CombatMatches
        .Where(x => x.CompletedAtUtc == null && x.AcceptDeadlineUtc == null &&
            (!(x.AcceptedA && x.AcceptedB) || ((x.LastSeenA ?? x.CreatedAtUtc) < cutoff && (x.LastSeenB ?? x.CreatedAtUtc) < cutoff)))
        .ToListAsync();
    if (dead.Count == 0) return;
    var ids = dead.Select(x => x.Id).ToList();
    await db.MatchQueue.Where(x => x.CombatMatchId != null && ids.Contains(x.CombatMatchId.Value)).ExecuteDeleteAsync();
    db.CombatMatches.RemoveRange(dead);
    await db.SaveChangesAsync();
}

// Rolls both players' dice for a new turn and clears last turn's shaping.
static void RollTurnDice(CombatMatch match)
{
    match.RuneA = CombatEngine.RollRune(match.ClassA, ParseRunes(match.UsedRunesA), Random.Shared);
    match.FormA = CombatEngine.RollForm(Random.Shared);
    match.RuneB = CombatEngine.RollRune(match.ClassB, ParseRunes(match.UsedRunesB), Random.Shared);
    match.FormB = CombatEngine.RollForm(Random.Shared);
    match.PendingRerollA = false; match.PendingRerollB = false;
    match.PendingD8AdjustmentA = 0; match.PendingD8AdjustmentB = 0;
    match.PendingFormA = null; match.PendingFormB = null;
    if (match.ShadeDifficulty is not null)
    {
        // The Shade shapes its dice in the open, before the player declares.
        var shaping = ShadeBot.Shape(match.ShadeDifficulty, match.FormB, match.ManaB, match.HpB, match.HpA);
        if (shaping.PickForm is not null)
        {
            match.FormB = shaping.PickForm;
            match.PendingFormB = shaping.PickForm;
            match.ManaB -= CombatEngine.FormPickCost;
        }
    }
}

// Duels that began before dice were rolled up front get their first roll here.
static void EnsureDice(CombatMatch match)
{
    if (match.CompletedAtUtc is null && match.AcceptDeadlineUtc is null && (match.RuneA is null || match.FormA is null || match.RuneB is null || match.FormB is null))
        RollTurnDice(match);
}

static List<string> History(string? csv) => (csv ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();

static string AddHistory(string? csv, string state) => string.Join(",", History(csv).Append(state).TakeLast(3));

// The engine writes "A: ... B: ..."; show the players' names instead.
static string NameLog(string log, string nameA, string nameB) =>
    (log.StartsWith("A: ") ? $"{nameA}: " + log[3..].Replace(". B: ", $". {nameB}: ") : log)
        .Replace("Mage A committed", $"{nameA} committed").Replace("Mage B committed", $"{nameB} committed");

// Insight from gear makes rerolls cheaper. Flamecallers pay HP instead of mana.
static (int Mana, int Hp) RerollCost(CombatMatch match, bool isA)
{
    var insight = Equipment.Decode(isA ? match.BonusesA : match.BonusesB)[Equipment.Insight];
    var flamecaller = (isA ? match.ClassA : match.ClassB) == "Flamecaller";
    return flamecaller ? (0, Math.Max(0, 1 - insight)) : (Math.Max(0, CombatEngine.RerollManaCost - insight), 0);
}

static object DiceDto(string className, string? rune, string? form, bool rerolled, string? picked, int shift) => new
{
    rune,
    symbol = rune is null ? null : CombatEngine.GetRuneSymbol(rune),
    form,
    formState = form is null ? null : CombatEngine.GetFormState(form),
    rerolled,
    picked = picked is not null,
    shifted = shift != 0
};

static object HistoryEntry(CombatMatch x, Guid playerId, Dictionary<Guid, string> names)
{
    var isA = x.PlayerAId == playerId;
    var opponentId = isA ? x.PlayerBId : x.PlayerAId;
    return new
    {
        matchId = x.Id,
        completedAtUtc = x.CompletedAtUtc,
        practice = x.ShadeDifficulty,
        mode = x.Mode,
        opponent = x.ShadeDifficulty is not null ? "The Shade" : names.GetValueOrDefault(opponentId, "Opponent"),
        myClass = isA ? x.ClassA : x.ClassB,
        opponentClass = isA ? x.ClassB : x.ClassA,
        result = x.IsDraw ? "draw" : x.WinnerId == playerId ? "win" : "loss",
        turns = Math.Max(1, x.Turn - 1),
        pointsDelta = isA ? x.PointsDeltaA : x.PointsDeltaB,
        leagueChange = isA ? x.LeagueChangeA : x.LeagueChangeB,
        ratingDelta = isA ? x.RatingDeltaA : x.RatingDeltaB,
        coins = isA ? x.CoinsA : x.CoinsB,
        log = x.LastLog
    };
}

static async Task<string> OpponentName(RuneDbContext db, CombatMatch match, Guid playerId)
{
    if (match.ShadeDifficulty is not null) return match.ShadeDifficulty switch
    {
        ShadeBot.Hard => "The Shade (Hard)",
        ShadeBot.Tutorial => "The Shade (Training)",
        _ => "The Shade (Easy)"
    };
    var opponent = await db.Players.FindAsync(match.PlayerAId == playerId ? match.PlayerBId : match.PlayerAId);
    return opponent?.Username ?? "Opponent";
}

static string? ReadState(string? rollJson)
{
    if (string.IsNullOrWhiteSpace(rollJson)) return null;
    try
    {
        using var doc = JsonDocument.Parse(rollJson);
        return doc.RootElement.TryGetProperty("State", out var state) ? state.GetString() : null;
    }
    catch (JsonException)
    {
        return null;
    }
}

static void MarkSeen(CombatMatch match, Guid playerId)
{
    // The Shade never leaves, so a practice duel only goes stale if the player does.
    if (match.ShadeDifficulty is not null) { match.LastSeenA = DateTime.UtcNow; match.LastSeenB = DateTime.UtcNow; return; }
    if (match.PlayerAId == playerId) match.LastSeenA = DateTime.UtcNow; else match.LastSeenB = DateTime.UtcNow;
}

static int OpponentAwaySeconds(CombatMatch match, Guid playerId)
{
    if (match.ShadeDifficulty is not null) return 0;
    var seen = match.PlayerAId == playerId ? match.LastSeenB : match.LastSeenA;
    var since = seen ?? match.CreatedAtUtc;
    return (int)Math.Max(0, (DateTime.UtcNow - since).TotalSeconds);
}

static async Task FinishByForfeit(RuneDbContext db, CombatMatch match, Guid loserId, GameRules rules, ServerSettings server, string log)
{
    var winnerId = match.PlayerAId == loserId ? match.PlayerBId : match.PlayerAId;
    match.WinnerId = winnerId;
    match.LastLog = log;
    match.CompletedAtUtc = DateTime.UtcNow;
    match.PendingA = null;
    match.PendingB = null;
    await ApplyResult(db, match, winnerId, rules, server);
    await db.SaveChangesAsync();
}
static void EnsureSqliteSchema(RuneDbContext db)
{
    // EnsureCreated never alters an existing database, so add any column the models gained later.
    // New tables are created from the EF model; IF NOT EXISTS leaves the existing ones alone.
    var script = db.Database.GenerateCreateScript()
        .Replace("CREATE TABLE ", "CREATE TABLE IF NOT EXISTS ")
        .Replace("CREATE UNIQUE INDEX ", "CREATE UNIQUE INDEX IF NOT EXISTS ")
        .Replace("CREATE INDEX ", "CREATE INDEX IF NOT EXISTS ");
    using var connection = db.Database.GetDbConnection();
    connection.Open();
    AddMissingColumns(connection, "Players", new Dictionary<string, string>
    {
        ["Coins"] = "INTEGER NOT NULL DEFAULT 0",
        ["Gems"] = "INTEGER NOT NULL DEFAULT 0",
        ["StoryChapter"] = "INTEGER NOT NULL DEFAULT 0",
        ["RealmInitializedAtUtc"] = "TEXT NULL"
    });
    foreach (var statement in script.Split(";", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    {
        if (!statement.StartsWith("CREATE", StringComparison.OrdinalIgnoreCase)) continue;
        using var create = connection.CreateCommand();
        create.CommandText = statement;
        create.ExecuteNonQuery();
    }
    AddMissingColumns(connection, "Players", new Dictionary<string, string> { ["TutorialDone"] = "INTEGER NOT NULL DEFAULT 0" });
    AddMissingColumns(connection, "Items", new Dictionary<string, string>
    {
        ["Edge"] = "INTEGER NOT NULL DEFAULT 0",
        ["Pierce"] = "INTEGER NOT NULL DEFAULT 0",
        ["Insight"] = "INTEGER NOT NULL DEFAULT 0",
        ["Resolve"] = "INTEGER NOT NULL DEFAULT 0",
        ["Rarity"] = "TEXT NOT NULL DEFAULT ''",
        ["Image"] = "TEXT NOT NULL DEFAULT ''",
        ["Power"] = "INTEGER NOT NULL DEFAULT 0",
        ["Guard"] = "INTEGER NOT NULL DEFAULT 0",
        ["Vigor"] = "INTEGER NOT NULL DEFAULT 0",
        ["Focus"] = "INTEGER NOT NULL DEFAULT 0",
        ["Grit"] = "INTEGER NOT NULL DEFAULT 0"
    });
    AddMissingColumns(connection, "CombatMatches", new Dictionary<string, string>
    {
        ["ShadeDifficulty"] = "TEXT NULL",
        ["RuneA"] = "TEXT NULL",
        ["FormA"] = "TEXT NULL",
        ["RuneB"] = "TEXT NULL",
        ["FormB"] = "TEXT NULL",
        ["PointsDeltaA"] = "INTEGER NOT NULL DEFAULT 0",
        ["PointsDeltaB"] = "INTEGER NOT NULL DEFAULT 0",
        ["LeagueChangeA"] = "INTEGER NOT NULL DEFAULT 0",
        ["LeagueChangeB"] = "INTEGER NOT NULL DEFAULT 0",
        ["RatingDeltaA"] = "INTEGER NOT NULL DEFAULT 0",
        ["RatingDeltaB"] = "INTEGER NOT NULL DEFAULT 0",
        ["CoinsA"] = "INTEGER NOT NULL DEFAULT 0",
        ["CoinsB"] = "INTEGER NOT NULL DEFAULT 0",
        ["StatesA"] = "TEXT NOT NULL DEFAULT ''",
        ["StatesB"] = "TEXT NOT NULL DEFAULT ''",
        ["BonusesA"] = "TEXT NOT NULL DEFAULT ''",
        ["BonusesB"] = "TEXT NOT NULL DEFAULT ''",
        ["LastSeenA"] = "TEXT NULL",
        ["LastSeenB"] = "TEXT NULL",
        ["IsDraw"] = "INTEGER NOT NULL DEFAULT 0",
        ["PendingD8AdjustmentA"] = "INTEGER NOT NULL DEFAULT 0",
        ["PendingD8AdjustmentB"] = "INTEGER NOT NULL DEFAULT 0",
        ["UsedRunesA"] = "TEXT NOT NULL DEFAULT ''",
        ["UsedRunesB"] = "TEXT NOT NULL DEFAULT ''",
        ["PendingRerollA"] = "INTEGER NOT NULL DEFAULT 0",
        ["PendingRerollB"] = "INTEGER NOT NULL DEFAULT 0",
        ["PendingFormA"] = "TEXT NULL",
        ["PendingFormB"] = "TEXT NULL",
        ["AcceptedA"] = "INTEGER NOT NULL DEFAULT 0",
        ["AcceptedB"] = "INTEGER NOT NULL DEFAULT 0",
        ["AcceptDeadlineUtc"] = "TEXT NULL",
        ["Mode"] = "TEXT NOT NULL DEFAULT 'ranked'",
        ["RematchA"] = "INTEGER NOT NULL DEFAULT 0",
        ["RematchB"] = "INTEGER NOT NULL DEFAULT 0",
        ["RematchMatchId"] = "TEXT NULL"
    });
    AddMissingColumns(connection, "MatchQueue", new Dictionary<string, string>
    {
        ["LastHeartbeatUtc"] = "TEXT NULL",
        ["Mode"] = "TEXT NOT NULL DEFAULT 'ranked'"
    });
}

static void AddMissingColumns(System.Data.Common.DbConnection connection, string table, Dictionary<string, string> columns)
{
    var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    using (var info = connection.CreateCommand())
    {
        info.CommandText = $"PRAGMA table_info('{table}')";
        using var reader = info.ExecuteReader();
        while (reader.Read())
            existing.Add(reader.GetString(1));
    }

    foreach (var (name, definition) in columns)
    {
        if (existing.Contains(name)) continue;
        using var alter = connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {name} {definition}";
        alter.ExecuteNonQuery();
    }
}

static async Task ResolveExpiredPendingMatches(RuneDbContext db, ServerSettings server)
{
    var cutoff = DateTime.UtcNow.AddSeconds(-server.AcceptGraceSeconds);
    var expired = await db.CombatMatches
        .Where(x => x.AcceptDeadlineUtc != null && x.AcceptDeadlineUtc < cutoff && x.CompletedAtUtc == null)
        .ToListAsync();
    if (expired.Count == 0) return;
    foreach (var match in expired)
    {
        var anyoneAccepted = match.AcceptedA || match.AcceptedB;
        var entries = await db.MatchQueue.Where(x => x.CombatMatchId == match.Id).ToListAsync();
        foreach (var entry in entries)
        {
            var accepted = entry.PlayerId == match.PlayerAId ? match.AcceptedA : match.AcceptedB;
            // Players who pressed Enter Match (or everyone, if nobody did) go back to searching; the one who missed it leaves the queue.
            if (accepted || !anyoneAccepted)
            {
                entry.Matched = false; entry.OpponentId = null; entry.CombatMatchId = null;
                entry.LastHeartbeatUtc = DateTime.UtcNow;
            }
            else db.MatchQueue.Remove(entry);
        }
        db.CombatMatches.Remove(match);
    }
    await db.SaveChangesAsync();
}

static async Task RemoveCompletedMatchQueueEntries(RuneDbContext db)
{
    var matchedEntries = await db.MatchQueue.Where(x => x.Matched && x.CombatMatchId != null).ToListAsync();
    if (matchedEntries.Count == 0) return;

    var combatIds = matchedEntries.Select(x => x.CombatMatchId!.Value).Distinct().ToList();
    var completedCombatIds = await db.CombatMatches
        .Where(x => combatIds.Contains(x.Id) && x.CompletedAtUtc != null)
        .Select(x => x.Id)
        .ToListAsync();
    if (completedCombatIds.Count > 0)
        await db.MatchQueue.Where(x => x.CombatMatchId != null && completedCombatIds.Contains(x.CombatMatchId.Value)).ExecuteDeleteAsync();
}

static async Task RemoveExpiredQueueEntries(RuneDbContext db, GameRules rules)
{
    await RemoveCompletedMatchQueueEntries(db);
    var cutoff = DateTime.UtcNow.AddSeconds(-rules.QueueTimeoutSeconds);
    var expiredCombatIds = await db.MatchQueue
        .Where(x => x.Matched && x.CombatMatchId != null && (x.LastHeartbeatUtc ?? x.JoinedAtUtc) < cutoff)
        .Select(x => x.CombatMatchId!.Value)
        .Distinct()
        .ToListAsync();
    if (expiredCombatIds.Count > 0)
        await db.MatchQueue.Where(x => x.CombatMatchId != null && expiredCombatIds.Contains(x.CombatMatchId.Value)).ExecuteDeleteAsync();

    await db.MatchQueue
        .Where(x => !x.Matched && (x.LastHeartbeatUtc ?? x.JoinedAtUtc) < cutoff)
        .ExecuteDeleteAsync();
}

static object CombatDto(CombatMatch match, Guid playerId, string playerName, string opponentName, GameRules rules, ServerSettings server)
{
    var isA = match.PlayerAId == playerId;
    var awaySeconds = match.CompletedAtUtc is null ? OpponentAwaySeconds(match, playerId) : 0;
    var myClass = isA ? match.ClassA : match.ClassB;
    var opponentClass = isA ? match.ClassB : match.ClassA;
    return new
    {
        matchId = match.Id, turn = match.Turn, playerName, opponent = opponentName,
        myClass, myCoreRule = rules.Classes.FirstOrDefault(x => x.Name == myClass)?.CoreRule,
        opponentClass, opponentCoreRule = rules.Classes.FirstOrDefault(x => x.Name == opponentClass)?.CoreRule,
        status = match.CompletedAtUtc is null ? "active" : "finished",
        myHp = isA ? match.HpA : match.HpB, myShield = isA ? match.ShieldA : match.ShieldB, myMana = isA ? match.ManaA : match.ManaB,
        opponentHp = isA ? match.HpB : match.HpA, opponentShield = isA ? match.ShieldB : match.ShieldA, opponentMana = isA ? match.ManaB : match.ManaA,
        waitingForOpponent = isA ? match.PendingA is not null : match.PendingB is not null,
        winner = match.WinnerId is null ? null : match.WinnerId == playerId ? "me" : "opponent",
        draw = match.IsDraw,
        completedAtUtc = match.CompletedAtUtc,
        log = NameLog(match.LastLog, isA ? playerName : opponentName, isA ? opponentName : playerName), lastRoll = JsonSerializer.Deserialize<object>(isA ? match.LastRollA ?? "null" : match.LastRollB ?? "null"),
        opponentRoll = JsonSerializer.Deserialize<object>(isA ? match.LastRollB ?? "null" : match.LastRollA ?? "null"),
        usedRunes = ParseRunes(isA ? match.UsedRunesA : match.UsedRunesB).ToArray(),
        states = new[] { "Break", "Flow", "Bind" },
        forms = CombatEngine.GetForms(),
        formPickCost = CombatEngine.FormPickCost,
        manaCap = CombatEngine.ManaCap,
        myGear = Equipment.StatsObject(Equipment.Decode(isA ? match.BonusesA : match.BonusesB)),
        opponentGear = Equipment.StatsObject(Equipment.Decode(isA ? match.BonusesB : match.BonusesA)),
        practice = match.ShadeDifficulty,
        mode = match.Mode,
        rematch = RematchDto(match, isA, server),
        myDice = DiceDto(myClass, isA ? match.RuneA : match.RuneB, isA ? match.FormA : match.FormB, isA ? match.PendingRerollA : match.PendingRerollB, isA ? match.PendingFormA : match.PendingFormB, isA ? match.PendingD8AdjustmentA : match.PendingD8AdjustmentB),
        opponentDice = DiceDto(opponentClass, isA ? match.RuneB : match.RuneA, isA ? match.FormB : match.FormA, isA ? match.PendingRerollB : match.PendingRerollA, isA ? match.PendingFormB : match.PendingFormA, isA ? match.PendingD8AdjustmentB : match.PendingD8AdjustmentA),
        opponentLocked = (isA ? match.PendingB : match.PendingA) is not null,
        myHistory = History(isA ? match.StatesA : match.StatesB),
        opponentHistory = History(isA ? match.StatesB : match.StatesA),
        rerollManaCost = RerollCost(match, isA).Mana,
        rerollHpCost = RerollCost(match, isA).Hp,
        result = match.CompletedAtUtc is null ? null : new
        {
            pointsDelta = isA ? match.PointsDeltaA : match.PointsDeltaB,
            leagueChange = isA ? match.LeagueChangeA : match.LeagueChangeB,
            ratingDelta = isA ? match.RatingDeltaA : match.RatingDeltaB,
            coins = isA ? match.CoinsA : match.CoinsB,
            turns = Math.Max(1, match.Turn - 1)
        },
        opponentAway = match.CompletedAtUtc is null && awaySeconds >= server.AwayNoticeSeconds,
        opponentAwaySeconds = awaySeconds,
        claimWinInSeconds = Math.Max(0, server.AwayClaimSeconds - awaySeconds)
    };
}

static IReadOnlyCollection<string> ParseRunes(string value)
{
    if (string.IsNullOrWhiteSpace(value)) return [];
    return value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

static string AddUsedRune(string value, string rune)
{
    var used = ParseRunes(value).ToHashSet(StringComparer.Ordinal);
    used.Add(rune);
    var runes = used.ToArray();
    return runes.Length >= 6 ? "" : string.Join(",", runes);
}
static string CreateToken(Player player, string key)
{
    var token = new JwtSecurityToken(claims: [new Claim(ClaimTypes.NameIdentifier, player.Id.ToString()), new Claim(ClaimTypes.Name, player.Username)], expires: DateTime.UtcNow.AddHours(12), signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)), SecurityAlgorithms.HmacSha256));
    return new JwtSecurityTokenHandler().WriteToken(token);
}

static async Task TryMatch(RuneDbContext db, GameRules rules, ServerSettings server)
{
    var cutoff = DateTime.UtcNow.AddSeconds(-rules.QueueTimeoutSeconds);
    var waiting = await db.MatchQueue
        .Where(x => !x.Matched && (x.LastHeartbeatUtc ?? x.JoinedAtUtc) >= cutoff)
        .OrderBy(x => x.JoinedAtUtc)
        .ToListAsync();
    foreach (var first in waiting)
    {
        var p1 = await db.Players.FindAsync(first.PlayerId);
        if (p1 is null) continue;
        var candidates = new List<(MatchQueueEntry Entry, Player Player, int RatingGap)>();
        foreach (var candidateEntry in waiting.Where(x => x.Id != first.Id && x.Mode == first.Mode))
        {
            var candidate = await db.Players.FindAsync(candidateEntry.PlayerId);
            if (candidate is null || candidate.ClassName is null) continue;

            var firstProtected = p1.SeasonMatches < rules.ProtectedBracketMatches;
            var candidateProtected = candidate.SeasonMatches < rules.ProtectedBracketMatches;
            var oldestWait = Math.Max(
                (DateTime.UtcNow - first.JoinedAtUtc).TotalSeconds,
                (DateTime.UtcNow - candidateEntry.JoinedAtUtc).TotalSeconds);
            var protectedBracketAllowed = first.Mode != DuelMode.Ranked || firstProtected == candidateProtected
                || (!firstProtected && !candidateProtected)
                || oldestWait >= rules.ProtectedBracketMaxWaitSeconds;
            if (!protectedBracketAllowed) continue;

            var waitSeconds = (int)Math.Max(0, oldestWait);
            var gap = Math.Min(
                rules.MaximumMatchmakingGap,
                rules.InitialMatchmakingGap + waitSeconds / rules.MatchmakingGapExpansionSeconds * rules.InitialMatchmakingGap);
            var ratingGap = Math.Abs(p1.SkillRating - candidate.SkillRating);
            if (ratingGap <= gap)
                candidates.Add((candidateEntry, candidate, ratingGap));
        }

        var best = candidates
            .OrderBy(x => x.RatingGap)
            .ThenBy(x => x.Entry.JoinedAtUtc)
            .FirstOrDefault();
        if (best.Player is not null)
        {
            first.Matched = true; first.OpponentId = best.Player.Id;
            best.Entry.Matched = true; best.Entry.OpponentId = p1.Id;
            var combat = await NewDuel(db, p1, best.Player, first.Mode);
            combat.AcceptDeadlineUtc = DateTime.UtcNow.AddSeconds(server.AcceptWindowSeconds);
            db.CombatMatches.Add(combat);
            first.CombatMatchId = combat.Id;
            best.Entry.CombatMatchId = combat.Id;
            await db.SaveChangesAsync();
            return;
        }
    }
}
