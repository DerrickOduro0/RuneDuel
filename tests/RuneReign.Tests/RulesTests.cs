using RuneReign.Domain;
using Xunit;

namespace RuneReign.Tests;

public class CombatRulesTests
{
    private static CombatResult Duel(string classA, CombatAction a, string classB, CombatAction b, CombatPlayerState? stateA = null, CombatPlayerState? stateB = null) =>
        CombatEngine.Resolve(classA, a, stateA ?? new CombatPlayerState(), classB, b, stateB ?? new CombatPlayerState());

    [Fact]
    public void Ward_shield_blocks_damage_in_the_same_turn_for_every_class()
    {
        // Same state on both sides, so no clash bonus. Kenaz + Ward raises a 4 shield; Isa + Bolt deals 5.
        var result = Duel("Flamecaller", new("Kenaz", "Ward", "Flow"), "Frostwarden", new("Isa", "Bolt", "Flow"));
        Assert.Equal(29, result.PlayerA.Hp);
        Assert.Equal(0, result.PlayerA.Shield);
        Assert.Equal(30, result.PlayerB.Hp);
    }

    [Fact]
    public void Wave_ignores_shields()
    {
        var result = Duel("Flamecaller", new("Kenaz", "Ward", "Flow"), "Frostwarden", new("Isa", "Wave", "Flow"));
        Assert.Equal(28, result.PlayerA.Hp);
    }

    [Fact]
    public void Frostwarden_keeps_unused_shield()
    {
        // Algiz + Ward = 7 shield. Kenaz + Curse deals 5, leaving 2.
        var result = Duel("Frostwarden", new("Algiz", "Ward", "Bind"), "Flamecaller", new("Kenaz", "Curse", "Bind"));
        Assert.Equal(30, result.PlayerA.Hp);
        Assert.Equal(2, result.PlayerA.Shield);
    }

    [Fact]
    public void Shield_never_goes_above_eight()
    {
        var result = Duel("Frostwarden", new("Algiz", "Ward", "Bind"), "Flamecaller", new("Kenaz", "Ward", "Bind"), new CombatPlayerState(Shield: 7));
        Assert.Equal(8, result.PlayerA.Shield);
    }

    [Fact]
    public void A_spell_never_deals_more_than_twelve()
    {
        // Dagaz 8 + Nova 4 = 12, plus the +2 clash bonus, is still capped at 12.
        var result = Duel("Flamecaller", new("Dagaz", "Nova", "Break"), "Frostwarden", new("Isa", "Surge", "Flow"));
        Assert.Equal(18, result.PlayerB.Hp);
    }

    [Fact]
    public void Committed_clash_winner_gets_plus_two_and_loser_minus_one()
    {
        // Break beats Flow and Bolt is a Break form: 4 + 2 + 2 = 8. Isa + Surge = 3, minus 1 = 2,
        // which the winner's +2 clash shield then blocks.
        var result = Duel("Flamecaller", new("Kenaz", "Bolt", "Break"), "Frostwarden", new("Isa", "Surge", "Flow"));
        Assert.Equal(22, result.PlayerB.Hp);
        Assert.Equal(30, result.PlayerA.Hp);
    }

    [Fact]
    public void Uncommitted_clash_winner_gets_no_bonus_but_loser_still_loses_one()
    {
        // Break beats Flow, but Surge is a Flow form, so A gets no bonus: 4. B: 3 - 1 = 2.
        var result = Duel("Flamecaller", new("Kenaz", "Surge", "Break"), "Frostwarden", new("Isa", "Surge", "Flow"));
        Assert.Equal(26, result.PlayerB.Hp);
        Assert.Equal(28, result.PlayerA.Hp);
    }

    [Fact]
    public void Gravebound_hits_harder_below_half_health()
    {
        var healthy = Duel("Gravebound", new("Wunjo", "Surge", "Flow"), "Frostwarden", new("Isa", "Surge", "Flow"), new CombatPlayerState(Hp: 20));
        var wounded = Duel("Gravebound", new("Wunjo", "Surge", "Flow"), "Frostwarden", new("Isa", "Surge", "Flow"), new CombatPlayerState(Hp: 15));
        Assert.Equal(26, healthy.PlayerB.Hp);
        Assert.Equal(24, wounded.PlayerB.Hp);
    }

    [Fact]
    public void Hp_never_goes_above_thirty()
    {
        var result = Duel("Frostwarden", new("Berkano", "Drain", "Bind"), "Frostwarden", new("Isa", "Ward", "Bind"));
        Assert.Equal(30, result.PlayerA.Hp);
    }
}

public class LadderTests
{
    private static readonly GameRules Rules = new([], [], 20, -10, 3, 20, 1000, 30, 100);

    [Fact]
    public void Wheel_crowns_the_top_of_league_one_not_league_ten()
    {
        var result = LeagueEngine.TurnWheel([
            new("low", "Bottom", 10, 90, 40),
            new("top", "Top", 1, 10, 40),
            new("mid", "Middle", 5, 99, 40)
        ], "2026-11", Rules);
        Assert.Equal("Top", result.Champion!.Username);
    }

    [Fact]
    public void Wheel_skips_players_without_enough_matches()
    {
        var result = LeagueEngine.TurnWheel([
            new("top", "Top", 1, 50, 5),
            new("next", "Next", 2, 0, 30)
        ], "2026-11", Rules);
        Assert.Equal("Next", result.Champion!.Username);
    }

    [Fact]
    public void Wheel_drops_top_three_leagues_by_three_and_the_rest_by_one()
    {
        var result = LeagueEngine.TurnWheel([
            new("a", "A", 1, 0, 0), new("b", "B", 3, 0, 0), new("c", "C", 4, 0, 0), new("d", "D", 10, 0, 0)
        ], "2026-11", Rules);
        Assert.Equal(4, result.Players["a"].League);
        Assert.Equal(6, result.Players["b"].League);
        Assert.Equal(5, result.Players["c"].League);
        Assert.Equal(10, result.Players["d"].League);
    }

    [Fact]
    public void Wheel_is_due_only_when_the_month_changes()
    {
        Assert.False(LeagueEngine.IsWheelDue("2026-11", new DateTime(2026, 11, 30, 23, 0, 0, DateTimeKind.Utc)));
        Assert.True(LeagueEngine.IsWheelDue("2026-11", new DateTime(2026, 12, 1, 0, 5, 0, DateTimeKind.Utc)));
        Assert.False(LeagueEngine.IsWheelDue(null, DateTime.UtcNow));
    }

    [Fact]
    public void Skill_rating_moves_toward_the_result()
    {
        Assert.Equal((1016, 984), LeagueEngine.UpdateRatings(1000, 1000, 1));
        Assert.Equal((1000, 1000), LeagueEngine.UpdateRatings(1000, 1000, 0.5));
        var upset = LeagueEngine.UpdateRatings(900, 1300, 1);
        Assert.True(upset.A - 900 > 16);
        Assert.Equal(2200, upset.A + upset.B);
    }
}

public class EquipmentBonusTests
{
    private static CombatResult Duel(CombatAction a, CombatAction b, CombatBonuses? bonusA = null, CombatPlayerState? stateA = null) =>
        CombatEngine.Resolve("Flamecaller", a, stateA ?? new CombatPlayerState(), "Frostwarden", b, new CombatPlayerState(), bonusA, null);

    [Fact]
    public void Power_adds_damage_to_attacks()
    {
        // Kenaz + Surge = 4, +2 Power = 6. Same state, so no clash bonus.
        var result = Duel(new("Kenaz", "Surge", "Flow"), new("Isa", "Surge", "Flow"), new CombatBonuses(Power: 2));
        Assert.Equal(24, result.PlayerB.Hp);
    }

    [Fact]
    public void Power_does_not_push_a_spell_past_twelve()
    {
        var result = Duel(new("Dagaz", "Nova", "Flow"), new("Isa", "Surge", "Flow"), new CombatBonuses(Power: 2));
        Assert.Equal(18, result.PlayerB.Hp);
    }

    [Fact]
    public void Power_does_nothing_for_a_ward()
    {
        var result = Duel(new("Kenaz", "Ward", "Flow"), new("Isa", "Surge", "Flow"), new CombatBonuses(Power: 2));
        Assert.Equal(30, result.PlayerB.Hp);
    }

    [Fact]
    public void Guard_adds_to_raised_shields()
    {
        // Kenaz + Ward = 4 shield, +2 Guard = 6. Isa + Bolt = 5, fully blocked.
        var result = Duel(new("Kenaz", "Ward", "Flow"), new("Isa", "Bolt", "Flow"), new CombatBonuses(Guard: 2));
        Assert.Equal(30, result.PlayerA.Hp);
    }

    [Fact]
    public void Vigor_adds_to_healing()
    {
        // Sowilo heals 2, +1 Vigor = 3. Isa + Surge deals 3.
        var result = Duel(new("Sowilo", "Surge", "Flow"), new("Isa", "Surge", "Flow"), new CombatBonuses(Vigor: 1), new CombatPlayerState(Hp: 20));
        Assert.Equal(20, result.PlayerA.Hp);
    }

    [Fact]
    public void Grit_reduces_recoil()
    {
        // Nova recoil 3 + Dagaz 2 = 5, Grit 2 leaves 3. Isa + Surge deals 3.
        var plain = Duel(new("Dagaz", "Nova", "Flow"), new("Isa", "Surge", "Flow"));
        var gritty = Duel(new("Dagaz", "Nova", "Flow"), new("Isa", "Surge", "Flow"), new CombatBonuses(Grit: 2));
        Assert.Equal(22, plain.PlayerA.Hp);
        Assert.Equal(24, gritty.PlayerA.Hp);
    }
}

public class ShadeBotTests
{
    [Fact]
    public void Hard_shade_counters_an_opponent_who_shows_a_commit_and_repeats_it()
    {
        // Opponent rolled Bolt (a Break form) and declared Break twice: the Shade should usually play Bind.
        var random = new Random(5);
        var binds = Enumerable.Range(0, 1000).Count(_ => ShadeBot.Declare(ShadeBot.Hard, "Surge", "Bolt", ["Break", "Break"], random) == "Bind");
        Assert.True(binds > 800, $"only {binds} of 1000");
    }

    [Fact]
    public void Prediction_weights_the_visible_form_and_habits()
    {
        var odds = ShadeBot.Predict("Wave", ["Bind", "Bind"]);
        Assert.True(odds["Flow"] > odds["Break"]);
        Assert.True(odds["Bind"] > odds["Break"]);
        Assert.Equal(1.0, odds.Values.Sum(), 6);
    }

    [Fact]
    public void Hard_shade_stays_somewhat_unpredictable()
    {
        var random = new Random(9);
        var states = Enumerable.Range(0, 1000).Select(_ => ShadeBot.Declare(ShadeBot.Hard, "Surge", "Bolt", ["Break", "Break"], random)).Distinct().Count();
        Assert.Equal(3, states);
    }

    [Fact]
    public void Easy_shade_never_shapes_its_dice()
    {
        Assert.Null(ShadeBot.Shape(ShadeBot.Easy, "Bolt", 5, 5, 5).PickForm);
    }

    [Fact]
    public void Hard_shade_heals_when_hurt_and_finishes_when_it_can()
    {
        Assert.Equal("Drain", ShadeBot.Shape(ShadeBot.Hard, "Bolt", 3, 8, 30).PickForm);
        Assert.Equal("Nova", ShadeBot.Shape(ShadeBot.Hard, "Wave", 2, 20, 9).PickForm);
        Assert.Null(ShadeBot.Shape(ShadeBot.Hard, "Wave", 1, 8, 9).PickForm);
    }

    [Fact]
    public void Counter_follows_the_triangle()
    {
        Assert.Equal("Break", ShadeBot.Counter("Flow"));
        Assert.Equal("Flow", ShadeBot.Counter("Bind"));
        Assert.Equal("Bind", ShadeBot.Counter("Break"));
    }
}

public class OpenDiceTests
{
    [Fact]
    public void Fateweaver_shift_moves_one_face_and_stops_at_the_ends()
    {
        Assert.Equal("Wave", CombatEngine.ShiftForm("Bolt", 1));
        Assert.Equal("Bolt", CombatEngine.ShiftForm("Bolt", -1));
        Assert.Equal("Echo", CombatEngine.ShiftForm("Nova", -1));
    }

    [Fact]
    public void Rolled_runes_skip_used_ones_until_all_are_used()
    {
        var random = new Random(2);
        var all = CombatEngine.GetRunes("Flamecaller");
        var used = all.Take(5).ToList();
        Assert.All(Enumerable.Range(0, 50), _ => Assert.Equal(all[5], CombatEngine.RollRune("Flamecaller", used, random)));
        Assert.Contains(CombatEngine.RollRune("Flamecaller", all.ToList(), random), all);
    }
}

public class ExpandedGearTests
{
    private static CombatResult Duel(CombatAction a, CombatAction b, CombatBonuses? gearA = null, CombatBonuses? gearB = null) =>
        CombatEngine.Resolve("Flamecaller", a, new CombatPlayerState(), "Flamecaller", b, new CombatPlayerState(), gearA, gearB);

    [Fact]
    public void Edge_adds_damage_only_when_you_win_the_clash()
    {
        // Kenaz + Surge = 4. A declares Break against Flow and wins (uncommitted): 4 + Edge 2 = 6.
        // B's Sowilo heals B for 2, so 30 - 6 + 2 = 26.
        var won = Duel(new("Kenaz", "Surge", "Break"), new("Sowilo", "Surge", "Flow"), new CombatBonuses(Edge: 2));
        Assert.Equal(26, won.PlayerB.Hp);
        // Same state: no clash, Edge does nothing: 30 - 4 + 2 = 28.
        var even = Duel(new("Kenaz", "Surge", "Flow"), new("Sowilo", "Surge", "Flow"), new CombatBonuses(Edge: 2));
        Assert.Equal(28, even.PlayerB.Hp);
    }

    [Fact]
    public void Resolve_softens_the_hit_when_you_lose_the_clash()
    {
        // B wins with Break + Bolt (committed): 4 + 2 + 2 = 8, minus A's Resolve 1 = 7. A's Sowilo heals 2.
        var plain = Duel(new("Sowilo", "Surge", "Flow"), new("Kenaz", "Bolt", "Break"));
        var resolved = Duel(new("Sowilo", "Surge", "Flow"), new("Kenaz", "Bolt", "Break"), new CombatBonuses(Resolve: 1));
        Assert.Equal(24, plain.PlayerA.Hp);
        Assert.Equal(25, resolved.PlayerA.Hp);
    }

    [Fact]
    public void Pierce_ignores_part_of_the_enemy_shield()
    {
        // B raises a 4 shield (Kenaz + Ward). A hits for 4 (Kenaz + Surge). With Pierce 2 only 2 is blocked.
        var plain = Duel(new("Kenaz", "Surge", "Flow"), new("Kenaz", "Ward", "Flow"));
        var pierced = Duel(new("Kenaz", "Surge", "Flow"), new("Kenaz", "Ward", "Flow"), new CombatBonuses(Pierce: 2));
        Assert.Equal(30, plain.PlayerB.Hp);
        Assert.Equal(28, pierced.PlayerB.Hp);
    }

    [Fact]
    public void Edge_still_respects_the_damage_cap()
    {
        // Capped at 12, and B's Sowilo heals 2.
        var result = Duel(new("Dagaz", "Nova", "Break"), new("Sowilo", "Surge", "Flow"), new CombatBonuses(Power: 2, Edge: 2));
        Assert.Equal(20, result.PlayerB.Hp);
    }
}
