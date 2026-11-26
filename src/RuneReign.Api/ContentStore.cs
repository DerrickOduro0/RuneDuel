using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RuneReign.Domain;

namespace RuneReign.Api;

// Server-side switches that are not part of the league rules record. Loaded from GameSettings at start-up.
public sealed record ServerSettings(
    int AcceptWindowSeconds,
    int AcceptGraceSeconds,
    int AwayNoticeSeconds,
    int AwayClaimSeconds,
    string SeasonNumber,
    string SeasonTitle,
    int StartingCoins,
    int StartingGems,
    string WheelSummary,
    int AbandonedMatchMinutes,
    int WinCoins,
    int LossCoins,
    int TutorialCoins,
    int CasualWinCoins,
    int CasualLossCoins,
    int ChallengeMinutes,
    int RematchSeconds);

// Owns the game content tables: seeds them on first start and reads them back for the API.
public static class ContentStore
{
    private const string CasualTag = "PvP · No points";
    private const string CasualText = "Duel a real mage with nothing at stake. No league points or rating change, but you still earn coins and quest progress.";
    private const string FriendTag = "Private · No points";
    private const string FriendText = "Create a code and send it to a rival, or enter theirs. No points, rating or coins change hands.";

    public static GameRules ReadSeedRules(string path) =>
        JsonSerializer.Deserialize<GameRules>(File.ReadAllText(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
        ?? throw new InvalidOperationException("GameRules.json is invalid.");

    // Fills every content table that is still empty. Rows that already exist are never overwritten,
    // so anything edited directly in the database survives a restart.
    public static void Seed(RuneDbContext db, GameRules seed)
    {
        var settings = new (string Key, string Value, string Description)[]
        {
            ("WinPoints", seed.WinPoints.ToString(), "League points for a ranked win."),
            ("LossPoints", seed.LossPoints.ToString(), "League points for a ranked loss (negative)."),
            ("DoubleWinCount", seed.DoubleWinCount.ToString(), "The first N wins of a season earn double points."),
            ("ProtectedBracketMatches", seed.ProtectedBracketMatches.ToString(), "New players are matched together for this many matches."),
            ("DefaultSkillRating", seed.DefaultSkillRating.ToString(), "Starting hidden skill rating."),
            ("MinimumChampionMatches", seed.MinimumChampionMatches.ToString(), "Matches needed to be crowned champion when the Wheel turns."),
            ("PointsPerLeague", seed.PointsPerLeague.ToString(), "Points needed to climb one league."),
            ("InitialMatchmakingGap", seed.InitialMatchmakingGap.ToString(), "Starting skill gap allowed between opponents."),
            ("MaximumMatchmakingGap", seed.MaximumMatchmakingGap.ToString(), "Largest skill gap allowed between opponents."),
            ("MatchmakingGapExpansionSeconds", seed.MatchmakingGapExpansionSeconds.ToString(), "Seconds of waiting before the skill gap widens."),
            ("QueueTimeoutSeconds", seed.QueueTimeoutSeconds.ToString(), "A searching player with no heartbeat for this long leaves the queue."),
            ("ProtectedBracketMaxWaitSeconds", seed.ProtectedBracketMaxWaitSeconds.ToString(), "After this wait a new player may meet anyone."),
            ("AcceptWindowSeconds", "10", "Seconds both players have to press Enter Match."),
            ("AcceptGraceSeconds", "2", "Extra seconds the server allows for slow connections."),
            ("AwayNoticeSeconds", "15", "Seconds without contact before a player is shown as away."),
            ("AwayClaimSeconds", "60", "Seconds an opponent must be away before the other player can claim the win."),
            ("WinCoins", "60", "Coins for winning a ranked duel."),
            ("LossCoins", "20", "Coins for finishing a ranked duel you lost."),
            ("TutorialCoins", "150", "Coins for finishing the tutorial duel."),
            ("CasualWinCoins", "30", "Coins for winning a casual duel."),
            ("CasualLossCoins", "10", "Coins for finishing a casual duel you lost."),
            ("ChallengeMinutes", "10", "How long a friend challenge code stays open."),
            ("RematchSeconds", "120", "How long after a casual or friend duel either player can ask for a rematch."),
            ("AbandonedMatchMinutes", "20", "A duel neither player has looked at for this many minutes is closed with no result."),
            ("SeasonNumber", "I", "Season number shown in the header."),
            ("SeasonTitle", "The Awakening", "Season title shown in the header."),
            ("StartingCoins", "800", "Coins given to a new player."),
            ("StartingGems", "10", "Gems given to a new player."),
            ("WheelSummary", "The Wheel turns on the 1st of each month. The top 3 leagues fall 3 tiers; every other league falls 1.", "Shown on the Ranks screen.")
        };
        var existingKeys = db.GameSettings.Select(x => x.Key).ToHashSet();
        foreach (var (key, value, description) in settings.Where(x => !existingKeys.Contains(x.Key)))
            db.GameSettings.Add(new GameSetting { Key = key, Value = value, Description = description });

        if (!db.GameClasses.Any())
        {
            var order = 0;
            foreach (var item in seed.Classes)
            {
                db.GameClasses.Add(new GameClass { Name = item.Name, Playstyle = item.Playstyle, CoreRule = item.CoreRule, SortOrder = order++ });
                var face = 0;
                foreach (var rune in item.Runes)
                    db.ClassRunes.Add(new ClassRune { ClassName = item.Name, Name = rune.Name, Meaning = rune.Meaning, Symbol = rune.Symbol, SortOrder = face++ });
            }
        }

        if (!db.LeagueTiers.Any())
            foreach (var league in seed.Leagues)
                db.LeagueTiers.Add(new LeagueTier { Number = league.Number, Name = league.Name });

        if (!db.SpellForms.Any())
        {
            var blurbs = new Dictionary<string, string>
            {
                ["Bolt"] = "+2 damage",
                ["Wave"] = "−1 damage, ignores shields",
                ["Ward"] = "turns the spell into a shield",
                ["Drain"] = "−1 damage, heal what you deal",
                ["Curse"] = "+1 damage",
                ["Surge"] = "a plain strike, no modifier",
                ["Echo"] = "a plain strike, no modifier",
                ["Nova"] = "+4 damage, 3 recoil"
            };
            var face = 0;
            foreach (var form in CombatEngine.GetForms())
                db.SpellForms.Add(new SpellForm { Name = form, Face = face++, State = CombatEngine.GetFormState(form), Blurb = blurbs.GetValueOrDefault(form, "") });
        }

        if (!db.CombatStates.Any())
        {
            db.CombatStates.AddRange(
                new CombatStateInfo { Name = "Break", Beats = "Flow", LosesTo = "Bind", SortOrder = 0 },
                new CombatStateInfo { Name = "Flow", Beats = "Bind", LosesTo = "Break", SortOrder = 1 },
                new CombatStateInfo { Name = "Bind", Beats = "Break", LosesTo = "Flow", SortOrder = 2 });
        }

        if (!db.GameTips.Any())
        {
            var tips = new[]
            {
                "Break beats Flow, Flow beats Bind, Bind beats Break. Declare your state in secret.",
                "Spend {FormPickCost} mana before a roll to choose the D8 yourself. Then only the rune die is rolled.",
                "Mana grows by 1 each turn, up to {ManaCap}. Save it for the turn that matters.",
                "Win the state clash with a matching form and you gain +2 damage; the losing side loses 1.",
                "Gravebound mages grow stronger below half health. Do not count them out.",
                "Frostwardens keep unspent shield between turns, up to 8."
            };
            for (var i = 0; i < tips.Length; i++)
                db.GameTips.Add(new GameTip { Text = tips[i], SortOrder = i });
        }

        if (!db.ArenaModes.Any())
        {
            db.ArenaModes.AddRange(
                new ArenaMode { Key = "ranked", Title = "Ranked Duel", Tag = "PvP · League points", Icon = "swords", IsLive = true, SortOrder = 0, Description = "Face a mage of similar skill. Win +{WinPoints} points, lose −{LossPointsAbs}. The first {DoubleWinCount} wins of a season count double." },
                new ArenaMode { Key = "casual", Title = "Casual Duel", Tag = CasualTag, Icon = "helmet", IsLive = true, SortOrder = 1, Description = CasualText },
                new ArenaMode { Key = "shade", Title = "Duel the Shade", Tag = "Practice · No points", Icon = "book", IsLive = true, SortOrder = 2, Description = "Practise against a computer mage. Easy plays at random; Hard reads your habits and spends its mana. Nothing in your league or quests changes." },
                new ArenaMode { Key = "friend", Title = "Friend Challenge", Tag = FriendTag, Icon = "friends", IsLive = true, SortOrder = 3, Description = FriendText });
        }

        // Attributes are added by key, so new ones reach databases seeded before they existed.
        var attributes = new[]
        {
            new AttributeDefinition { Key = "power", Name = "Power", Icon = "⚔", Effect = "+1 damage on every attack", Cap = 2, SortOrder = 0 },
            new AttributeDefinition { Key = "guard", Name = "Guard", Icon = "⛨", Effect = "+1 shield whenever you raise one", Cap = 2, SortOrder = 1 },
            new AttributeDefinition { Key = "vigor", Name = "Vigor", Icon = "✚", Effect = "+1 healing whenever you heal", Cap = 2, SortOrder = 2 },
            new AttributeDefinition { Key = "focus", Name = "Focus", Icon = "◈", Effect = "+1 mana at the start of a duel", Cap = 1, SortOrder = 3 },
            new AttributeDefinition { Key = "grit", Name = "Grit", Icon = "▲", Effect = "−1 recoil damage from your own spells", Cap = 2, SortOrder = 4 },
            new AttributeDefinition { Key = "edge", Name = "Edge", Icon = "✦", Effect = "+1 damage when you win the clash", Cap = 2, SortOrder = 5 },
            new AttributeDefinition { Key = "pierce", Name = "Pierce", Icon = "➶", Effect = "Your attacks ignore 1 enemy shield", Cap = 2, SortOrder = 6 },
            new AttributeDefinition { Key = "insight", Name = "Insight", Icon = "◉", Effect = "Rerolling your rune costs 1 less", Cap = 1, SortOrder = 7 },
            new AttributeDefinition { Key = "resolve", Name = "Resolve", Icon = "❂", Effect = "−1 damage taken when you lose the clash", Cap = 1, SortOrder = 8 }
        };
        var existingAttributes = db.Attributes.Select(x => x.Key).ToHashSet();
        foreach (var attribute in attributes.Where(x => !existingAttributes.Contains(x.Key)))
            db.Attributes.Add(attribute);

        if (!db.EquipmentSlots.Any())
        {
            db.EquipmentSlots.AddRange(
                new EquipmentSlot { Key = "helmet", Name = "Helmet", SortOrder = 0 },
                new EquipmentSlot { Key = "armor", Name = "Armor", SortOrder = 1 },
                new EquipmentSlot { Key = "mantle", Name = "Cloak", SortOrder = 2 },
                new EquipmentSlot { Key = "sigil", Name = "Sigil", SortOrder = 3 },
                new EquipmentSlot { Key = "ring", Name = "Ring", SortOrder = 4 },
                new EquipmentSlot { Key = "charm", Name = "Charm", SortOrder = 5 },
                new EquipmentSlot { Key = "frame", Name = "Dice frame", SortOrder = 6 });
        }

        // Casual and Friend Challenge used to be placeholders; open them in older databases.
        foreach (var (key, tag, text) in new[] { ("casual", CasualTag, CasualText), ("friend", FriendTag, FriendText) })
        {
            var mode = db.ArenaModes.Find(key);
            if (mode is not null && !mode.IsLive && mode.Tag == "Coming soon")
            {
                mode.IsLive = true; mode.Tag = tag; mode.Description = text;
            }
        }

        // Duel the Shade used to be a placeholder; open it in databases seeded before it existed.
        var shadeMode = db.ArenaModes.Find("shade");
        if (shadeMode is not null && !shadeMode.IsLive && shadeMode.Tag == "Coming soon")
        {
            shadeMode.IsLive = true;
            shadeMode.Tag = "Practice · No points";
            shadeMode.Description = "Practise against a computer mage. Easy plays at random; Hard reads your habits and spends its mana. Nothing in your league or quests changes.";
        }

        // Items are added by key, so new gear appears in existing databases without touching edited rows.
        var items = new[]
        {
            new ItemDefinition { Key = "runebound-mantle", Name = "Runebound mantle", Kind = "mantle", Icon = "✦", Description = "The plain mantle every apprentice is given.", IsStarter = true, SortOrder = 0 },
            new ItemDefinition { Key = "frost-sigil", Name = "Frost sigil", Kind = "sigil", Icon = "❄", Description = "A cold seal worn at the collar.", IsStarter = true, SortOrder = 1 },
            new ItemDefinition { Key = "apprentice-charm", Name = "Apprentice's charm", Kind = "charm", Icon = "✧", Description = "A small token for luck in your first duels.", IsStarter = true, SortOrder = 2 },
            new ItemDefinition { Key = "apprentice-hood", Name = "Apprentice hood", Kind = "helmet", Icon = "♜", Description = "A simple cloth hood.", IsStarter = true, SortOrder = 3 },
            new ItemDefinition { Key = "padded-robe", Name = "Padded robe", Kind = "armor", Icon = "▣", Description = "Quilted cloth that stops very little.", IsStarter = true, SortOrder = 4 },
            new ItemDefinition { Key = "iron-circlet", Name = "Iron circlet", Kind = "helmet", Icon = "♜", Description = "A plain band of iron that steadies your wards.", PriceCoins = 400, InShop = true, Guard = 1, SortOrder = 10 },
            new ItemDefinition { Key = "seers-hood", Name = "Seer's hood", Kind = "helmet", Icon = "♜", Description = "Woven with threads that hum with mana.", PriceCoins = 900, InShop = true, Focus = 1, Rarity = "rare", SortOrder = 11 },
            new ItemDefinition { Key = "leather-jerkin", Name = "Leather jerkin", Kind = "armor", Icon = "▣", Description = "Thick hide that takes the sting out of recoil.", PriceCoins = 350, InShop = true, Grit = 1, SortOrder = 12 },
            new ItemDefinition { Key = "runeplate-vest", Name = "Runeplate vest", Kind = "armor", Icon = "▣", Description = "Plates etched with Algiz.", PriceCoins = 1100, InShop = true, Guard = 1, Grit = 1, Rarity = "rare", SortOrder = 13 },
            new ItemDefinition { Key = "ember-ring", Name = "Ring of embers", Kind = "ring", Icon = "◎", Description = "Warm to the touch. Your spells bite harder.", PriceCoins = 600, InShop = true, Power = 1, SortOrder = 14 },
            new ItemDefinition { Key = "duelist-ring", Name = "Duelist's ring", Kind = "ring", Icon = "◎", Description = "Worn by arena veterans.", PriceCoins = 1500, InShop = true, Power = 1, Vigor = 1, Rarity = "rare", SortOrder = 15 },
            new ItemDefinition { Key = "bloodstone-sigil", Name = "Bloodstone sigil", Kind = "sigil", Icon = "❖", Description = "A red stone that quickens healing.", PriceCoins = 500, InShop = true, Vigor = 1, SortOrder = 16 },
            new ItemDefinition { Key = "stormglass-sigil", Name = "Stormglass sigil", Kind = "sigil", Icon = "❖", Description = "Lightning trapped in glass.", PriceCoins = 1300, InShop = true, Power = 1, Rarity = "rare", SortOrder = 17 },
            new ItemDefinition { Key = "keen-circlet", Name = "Keen-edged circlet", Kind = "helmet", Icon = "♜", Description = "Sharpens the moment you read your rival right.", PriceCoins = 900, InShop = true, Edge = 1, Rarity = "rare", SortOrder = 30 },
            new ItemDefinition { Key = "crown-of-duels", Name = "Crown of Duels", Kind = "helmet", Icon = "♛", Description = "Worn by champions of the Wheel.", PriceCoins = 3200, InShop = true, Edge = 1, Focus = 1, Guard = 1, Rarity = "epic", SortOrder = 31 },
            new ItemDefinition { Key = "shadowmail", Name = "Shadowmail", Kind = "armor", Icon = "▣", Description = "Dark links that turn a lost clash into a glancing blow.", PriceCoins = 2400, InShop = true, Resolve = 1, Guard = 1, Rarity = "epic", SortOrder = 32 },
            new ItemDefinition { Key = "wanderers-cloak", Name = "Wanderer's cloak", Kind = "mantle", Icon = "☾", Description = "Patched many times, never torn through.", PriceCoins = 900, InShop = true, Resolve = 1, Rarity = "rare", SortOrder = 33 },
            new ItemDefinition { Key = "stormcaller-mantle", Name = "Stormcaller mantle", Kind = "mantle", Icon = "☾", Description = "Crackles when you raise your staff.", PriceCoins = 2800, InShop = true, Pierce = 1, Power = 1, Rarity = "epic", SortOrder = 34 },
            new ItemDefinition { Key = "piercing-sigil", Name = "Piercing sigil", Kind = "sigil", Icon = "❖", Description = "A needle-thin rune that finds the gap in any ward.", PriceCoins = 1000, InShop = true, Pierce = 1, Rarity = "rare", SortOrder = 35 },
            new ItemDefinition { Key = "seer-ring", Name = "Ring of the Seer", Kind = "ring", Icon = "◎", Description = "Lets you see a better rune before you need it.", PriceCoins = 1100, InShop = true, Insight = 1, Rarity = "rare", SortOrder = 36 },
            new ItemDefinition { Key = "bladeweaver-ring", Name = "Bladeweaver ring", Kind = "ring", Icon = "◎", Description = "Two thin bands that cut as one.", PriceCoins = 2600, InShop = true, Edge = 1, Power = 1, Rarity = "epic", SortOrder = 37 },
            new ItemDefinition { Key = "thornspike-charm", Name = "Thornspike charm", Kind = "charm", Icon = "✧", Description = "Barbed, and so are your spells.", PriceCoins = 1200, InShop = true, Pierce = 1, Grit = 1, Rarity = "rare", SortOrder = 38 },
            new ItemDefinition { Key = "warding-charm", Name = "Warding charm", Kind = "charm", Icon = "✧", Description = "A knot charm against harm.", PriceCoins = 700, InShop = true, Guard = 1, SortOrder = 18 },
            new ItemDefinition { Key = "moonlit-cloak", Name = "Moonlit cloak", Kind = "mantle", Icon = "☾", Description = "A deep blue cloak that catches the light. Cosmetic.", PriceCoins = 750, InShop = true, Rarity = "rare", SortOrder = 20 },
            new ItemDefinition { Key = "aether-lantern", Name = "Aether lantern", Kind = "charm", Icon = "◈", Description = "A lantern that burns without oil. Cosmetic.", PriceCoins = 1200, InShop = true, Rarity = "rare", SortOrder = 21 },
            new ItemDefinition { Key = "ruby-rune-frame", Name = "Ruby rune frame", Kind = "frame", Icon = "◆", Description = "A red frame for your rune dice. Cosmetic.", PriceGems = 50, InShop = true, Rarity = "epic", SortOrder = 22 }
        };
        var existingItems = db.Items.Select(x => x.Key).ToHashSet();
        foreach (var item in items.Where(x => !existingItems.Contains(x.Key)))
            db.Items.Add(item);
        // Items stored before rarity existed get theirs from the list above.
        var rarityByKey = items.ToDictionary(x => x.Key, x => x.Rarity);
        foreach (var stored in db.Items.Where(x => x.Rarity == "" || x.Rarity == null).ToList())
            stored.Rarity = rarityByKey.GetValueOrDefault(stored.Key, "common");

        // Starter talismans. Behaviour lives in RuneReign.Domain.Talismans; this is the shop text and price.
        var talismans = new[]
        {
            new TalismanDefinition { Key = Talismans.LuckyKnot, Name = "Lucky Knot", Icon = "\u221e", Effect = "Your first reroll each duel is free.", Drawback = "", Budget = 2, PriceCoins = 400, SortOrder = 0 },
            new TalismanDefinition { Key = Talismans.MirrorShard, Name = "Mirror Shard", Icon = "\u25c8", Effect = "Once per duel, flip your D8 to its opposite face (Bolt \u2194 Nova, Wave \u2194 Echo, Ward \u2194 Surge, Drain \u2194 Curse).", Drawback = "Costs 1 mana.", Budget = 3, PriceCoins = 500, SortOrder = 1 },
            new TalismanDefinition { Key = Talismans.EmberCoal, Name = "Ember Coal", Icon = "\u2668", Effect = "Every time you lose a clash, gain 1 mana.", Drawback = "Start the duel with 1 less mana.", Budget = 3, PriceCoins = 450, SortOrder = 2 },
            new TalismanDefinition { Key = Talismans.IronSeal, Name = "Iron Seal", Icon = "\u26e8", Effect = "The first time you lose a clash, the winner gets no +2 bonus.", Drawback = "Start the duel at 28 HP.", Budget = 3, PriceCoins = 550, SortOrder = 3 },
            new TalismanDefinition { Key = Talismans.GamblersCoin, Name = "Gambler's Coin", Icon = "\u25ce", Effect = "Twice per duel, reroll your D6 and D8 together.", Drawback = "Costs 1 HP each time.", Budget = 3, PriceCoins = 500, SortOrder = 4 },
            new TalismanDefinition { Key = Talismans.PatientStone, Name = "Patient Stone", Icon = "\u25c9", Effect = "Twice per duel, gain 1 mana now.", Drawback = "No reroll, pick or shift that turn.", Budget = 3, PriceCoins = 400, SortOrder = 5 },
            new TalismanDefinition { Key = Talismans.SecondWind, Name = "Second Wind", Icon = "\u2740", Effect = "The first time your HP drops to 10 or less, gain 1 mana and your next reroll is free.", Drawback = "", Budget = 3, PriceCoins = 450, SortOrder = 6 },
            new TalismanDefinition { Key = Talismans.TwinMark, Name = "Twin Mark", Icon = "\u264a", Effect = "Once per duel, any state you declare this turn earns the +2 clash bonus.", Drawback = "Costs 2 mana.", Budget = 3, PriceCoins = 600, SortOrder = 7 }
        };
        var existingTalismans = db.Talismans.Select(x => x.Key).ToHashSet();
        foreach (var talisman in talismans.Where(x => !existingTalismans.Contains(x.Key)))
            db.Talismans.Add(talisman);

        if (!db.Objectives.Any())
        {
            db.Objectives.AddRange(
                new Objective { Key = "first-steps", Kind = "quest", Name = "First steps", Task = "Play 1 ranked match", Metric = "season_matches", Target = 1, Repeat = "season", RewardCoins = 50, SortOrder = 0 },
                new Objective { Key = "first-victory", Kind = "quest", Name = "First victory", Task = "Win 1 ranked match", Metric = "season_wins", Target = 1, Repeat = "season", RewardCoins = 75, SortOrder = 1 },
                new Objective { Key = "arena-regular", Kind = "quest", Name = "Arena regular", Task = "Complete 3 ranked matches", Metric = "season_matches", Target = 3, Repeat = "season", RewardCoins = 100, SortOrder = 2 },
                new Objective { Key = "rising-mage", Kind = "quest", Name = "Rising mage", Task = "Win 5 ranked matches", Metric = "season_wins", Target = 5, Repeat = "season", RewardCoins = 250, RewardGems = 5, SortOrder = 3 },
                new Objective { Key = "daily-duel", Kind = "mission", Name = "Daily duel", Task = "Finish 1 match today", Metric = "matches_today", Target = 1, Repeat = "daily", RewardCoins = 30, SortOrder = 10 },
                new Objective { Key = "daily-win", Kind = "mission", Name = "Daily victory", Task = "Win 1 match today", Metric = "wins_today", Target = 1, Repeat = "daily", RewardCoins = 60, SortOrder = 11 },
                new Objective { Key = "daily-trio", Kind = "mission", Name = "Three duels", Task = "Finish 3 matches today", Metric = "matches_today", Target = 3, Repeat = "daily", RewardCoins = 90, SortOrder = 12 },
                new Objective { Key = "daily-login", Kind = "reward", Name = "Daily login", Task = "Visit the realm today", Metric = "login", Target = 1, Repeat = "daily", RewardCoins = 25, SortOrder = 20 },
                new Objective { Key = "season-milestone", Kind = "reward", Name = "Season milestone", Task = "Complete 10 matches this season", Metric = "season_matches", Target = 10, Repeat = "season", RewardGems = 10, SortOrder = 21 },
                new Objective { Key = "league-promotion", Kind = "reward", Name = "League promotion", Task = "Climb out of the Apprentice league", Metric = "promotions", Target = 1, Repeat = "once", RewardCoins = 200, SortOrder = 22 });
        }

        if (!db.StoryChapters.Any())
        {
            db.StoryChapters.AddRange(
                new StoryChapterDefinition { Number = 1, Title = "The first rune", UnlockMatches = 0, Body = "The old gates have opened. Every mage who enters the arena carries six runes and a single die of forms. Win your first duels to learn what the runes remember." },
                new StoryChapterDefinition { Number = 2, Title = "Ash and ice", UnlockMatches = 5, Body = "The Flamecallers and the Frostwardens fought the first war of the runes. Neither side won, and the arena was built on the ashes so the quarrel could be settled one duel at a time." },
                new StoryChapterDefinition { Number = 3, Title = "The Wheel", UnlockMatches = 15, Body = "No crown lasts. Each month the Wheel turns and every mage falls back down the ladder. Only the Hall of Fame remembers who stood at the top." });
        }

        db.SaveChanges();
    }

    public static Dictionary<string, string> ReadSettings(RuneDbContext db) =>
        db.GameSettings.AsNoTracking().ToDictionary(x => x.Key, x => x.Value);

    public static GameRules LoadRules(RuneDbContext db, GameRules seed)
    {
        var s = ReadSettings(db);
        int Get(string key, int fallback) => s.TryGetValue(key, out var raw) && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : fallback;
        var classes = db.GameClasses.AsNoTracking().OrderBy(x => x.SortOrder).ToList();
        var runes = db.ClassRunes.AsNoTracking().OrderBy(x => x.SortOrder).ToList();
        var classDefinitions = classes
            .Select(c => new ClassDefinition(c.Name, c.Playstyle, c.CoreRule, runes.Where(r => r.ClassName == c.Name).Select(r => new RuneDefinition(r.Name, r.Meaning, r.Symbol)).ToList()))
            .ToList();
        var leagues = db.LeagueTiers.AsNoTracking().OrderBy(x => x.Number).Select(x => new LeagueDefinition(x.Number, x.Name)).ToList();
        return new GameRules(
            classDefinitions.Count > 0 ? classDefinitions : seed.Classes,
            leagues.Count > 0 ? leagues : seed.Leagues,
            Get("WinPoints", seed.WinPoints),
            Get("LossPoints", seed.LossPoints),
            Get("DoubleWinCount", seed.DoubleWinCount),
            Get("ProtectedBracketMatches", seed.ProtectedBracketMatches),
            Get("DefaultSkillRating", seed.DefaultSkillRating),
            Get("MinimumChampionMatches", seed.MinimumChampionMatches),
            Get("PointsPerLeague", seed.PointsPerLeague),
            Get("InitialMatchmakingGap", seed.InitialMatchmakingGap),
            Get("MaximumMatchmakingGap", seed.MaximumMatchmakingGap),
            Get("MatchmakingGapExpansionSeconds", seed.MatchmakingGapExpansionSeconds),
            Get("QueueTimeoutSeconds", seed.QueueTimeoutSeconds),
            Get("ProtectedBracketMaxWaitSeconds", seed.ProtectedBracketMaxWaitSeconds));
    }

    public static ServerSettings LoadServerSettings(RuneDbContext db)
    {
        var s = ReadSettings(db);
        int Get(string key, int fallback) => s.TryGetValue(key, out var raw) && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : fallback;
        string Text(string key, string fallback) => s.TryGetValue(key, out var raw) && !string.IsNullOrWhiteSpace(raw) ? raw : fallback;
        return new ServerSettings(
            Get("AcceptWindowSeconds", 10),
            Get("AcceptGraceSeconds", 2),
            Get("AwayNoticeSeconds", 15),
            Get("AwayClaimSeconds", 60),
            Text("SeasonNumber", "I"),
            Text("SeasonTitle", "The Awakening"),
            Get("StartingCoins", 800),
            Get("StartingGems", 10),
            Text("WheelSummary", ""),
            Get("AbandonedMatchMinutes", 20),
            Get("WinCoins", 60),
            Get("LossCoins", 20),
            Get("TutorialCoins", 150),
            Get("CasualWinCoins", 30),
            Get("CasualLossCoins", 10),
            Get("ChallengeMinutes", 10),
            Get("RematchSeconds", 120));
    }

    // Values that content text can refer to as {Name}.
    public static Dictionary<string, string> Tokens(GameRules rules) => new()
    {
        ["WinPoints"] = rules.WinPoints.ToString(),
        ["LossPointsAbs"] = Math.Abs(rules.LossPoints).ToString(),
        ["DoubleWinCount"] = rules.DoubleWinCount.ToString(),
        ["ProtectedBracketMatches"] = rules.ProtectedBracketMatches.ToString(),
        ["MinimumChampionMatches"] = rules.MinimumChampionMatches.ToString(),
        ["FormPickCost"] = CombatEngine.FormPickCost.ToString(),
        ["ManaCap"] = CombatEngine.ManaCap.ToString(),
        ["StartMana"] = CombatEngine.StartMana.ToString()
    };

    public static string Fill(string text, Dictionary<string, string> tokens)
    {
        foreach (var (key, value) in tokens)
            text = text.Replace("{" + key + "}", value);
        return text;
    }

    public static async Task<object> BuildContent(RuneDbContext db, GameRules rules, ServerSettings server)
    {
        var tokens = Tokens(rules);
        var forms = await db.SpellForms.AsNoTracking().OrderBy(x => x.Face).ToListAsync();
        var states = await db.CombatStates.AsNoTracking().OrderBy(x => x.SortOrder).ToListAsync();
        var tips = await db.GameTips.AsNoTracking().OrderBy(x => x.SortOrder).ToListAsync();
        var modes = await db.ArenaModes.AsNoTracking().OrderBy(x => x.SortOrder).ToListAsync();
        return new
        {
            season = new { number = server.SeasonNumber, title = server.SeasonTitle },
            classes = rules.Classes,
            leagues = rules.Leagues,
            forms = forms.Select(x => new { x.Name, x.Face, x.State, x.Blurb }),
            states = states.Select(x => new { x.Name, x.Beats, x.LosesTo }),
            tips = tips.Select(x => Fill(x.Text, tokens)),
            attributes = await db.Attributes.AsNoTracking().OrderBy(x => x.SortOrder).Select(x => new { x.Key, x.Name, x.Icon, x.Effect, x.Cap }).ToListAsync(),
            talismans = await db.Talismans.AsNoTracking().Where(x => x.Enabled).OrderBy(x => x.SortOrder).Select(x => new { x.Key, x.Name, x.Icon, x.Effect, x.Drawback, x.Rarity, x.PriceCoins, x.Ranked }).ToListAsync(),
            slots = await db.EquipmentSlots.AsNoTracking().OrderBy(x => x.SortOrder).Select(x => new { x.Key, x.Name }).ToListAsync(),
            arenaModes = modes.Select(x => new { x.Key, x.Title, x.Tag, description = Fill(x.Description, tokens), x.Icon, live = x.IsLive }),
            rules = new
            {
                rules.WinPoints,
                rules.LossPoints,
                rules.DoubleWinCount,
                rules.ProtectedBracketMatches,
                rules.MinimumChampionMatches,
                rules.PointsPerLeague,
                server.AcceptWindowSeconds,
                server.AwayNoticeSeconds,
                server.AwayClaimSeconds,
                wheelSummary = server.WheelSummary,
                maxHp = 30,
                CombatEngine.StartMana,
                CombatEngine.ManaCap,
                CombatEngine.FormPickCost
            },
            settingsDefaults = new PlayerSettingsDto(false, true, true, false)
        };
    }
}

public sealed record PlayerSettingsDto(bool ReduceMotion, bool DamageNumbers, bool DiceAnimation, bool CompactHud);
