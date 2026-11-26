using RuneReign.Domain;
using Xunit;

namespace RuneReign.Tests;

public class LeagueEngineTests
{
    private static readonly GameRules Rules = new([], [], 20, -10, 3, 20, 1000, 30, 100);

    [Fact]
    public void First_three_wins_are_double_and_promote()
    {
        var state = new LeagueState(10, 0, 0);
        state = LeagueEngine.RecordResult(state, true, Rules);
        state = LeagueEngine.RecordResult(state, true, Rules);
        state = LeagueEngine.RecordResult(state, true, Rules);
        Assert.Equal(9, state.League);
        Assert.Equal(20, state.Points);
    }

    [Fact]
    public void Loss_relegates_but_never_below_league_ten()
    {
        var state = LeagueEngine.RecordResult(new LeagueState(9, 0, 0), false, Rules);
        Assert.Equal(10, state.League);
        Assert.Equal(90, state.Points);
        Assert.Equal(10, LeagueEngine.RecordResult(new LeagueState(10, 0, 0), false, Rules).League);
    }

    [Fact]
    public void Wheel_crowns_eligible_leader_and_resets_points()
    {
        var result = LeagueEngine.TurnWheel([
            new("a", "A", 2, 80, 30),
            new("b", "B", 1, 10, 2)
        ], "Season 1", Rules);
        Assert.Equal("A", result.Champion!.Username);
        Assert.Equal(5, result.Players["a"].League);
        Assert.Equal(0, result.Players["a"].Points);
    }

    [Fact]
    public void Combat_resolves_triangle_bonus_and_damage()
    {
        var result = CombatEngine.Resolve(
            "Flamecaller",
            new CombatAction("Hagalaz", "Bolt", "Break"),
            new CombatPlayerState(),
            "Frostwarden",
            new CombatAction("Isa", "Wave", "Flow"),
            new CombatPlayerState());

        Assert.True(result.PlayerA.Hp < 30);
        Assert.True(result.PlayerB.Hp < 30);
        Assert.Contains("committed", result.Log);
    }

    [Fact]
    public void Combat_rejects_runes_from_another_class()
    {
        Assert.False(CombatEngine.IsValidAction("Flamecaller", new CombatAction("Isa", "Bolt", "Break")));
        Assert.True(CombatEngine.IsValidAction("Flamecaller", new CombatAction("Kenaz", "Bolt", "Break")));
    }

    [Fact]
    public void Combat_finishes_as_draw_when_both_players_reach_zero_hp()
    {
        var result = CombatEngine.Resolve(
            "Flamecaller",
            new CombatAction("Dagaz", "Nova", "Break"),
            new CombatPlayerState(3),
            "Flamecaller",
            new CombatAction("Dagaz", "Nova", "Break"),
            new CombatPlayerState(3));

        Assert.True(result.IsDraw);
        Assert.Null(result.Winner);
        Assert.Equal(0, result.PlayerA.Hp);
        Assert.Equal(0, result.PlayerB.Hp);
    }

    [Fact]
    public void Mana_grows_by_one_each_turn_and_caps_at_five()
    {
        var result = CombatEngine.Resolve(
            "Flamecaller",
            new CombatAction("Kenaz", "Bolt", "Break"),
            new CombatPlayerState(Mana: 5),
            "Frostwarden",
            new CombatAction("Isa", "Wave", "Flow"),
            new CombatPlayerState(Mana: 0));

        Assert.Equal(CombatEngine.ManaCap, result.PlayerA.Mana);
        Assert.Equal(1, result.PlayerB.Mana);
    }

    [Fact]
    public void Frostwarden_shield_carries_between_turns_but_is_reset_for_new_match()
    {
        var first = CombatEngine.Resolve(
            "Frostwarden",
            new CombatAction("Algiz", "Ward", "Bind"),
            new CombatPlayerState(Shield: 0),
            "Flamecaller",
            new CombatAction("Kenaz", "Bolt", "Break"),
            new CombatPlayerState());
        Assert.True(first.PlayerA.Shield > 0);

        var second = CombatEngine.Resolve(
            "Frostwarden",
            new CombatAction("Isa", "Bolt", "Flow"),
            new CombatPlayerState(Shield: first.PlayerA.Shield),
            "Flamecaller",
            new CombatAction("Kenaz", "Ward", "Flow"),
            new CombatPlayerState(Shield: 4));

        Assert.True(second.PlayerA.Shield > 0);
        Assert.Equal(0, second.PlayerB.Shield);
    }

    [Fact]
    public void Fateweaver_d8_adjustment_is_clamped_and_applied_to_roll()
    {
        var roll = CombatEngine.Roll("Fateweaver", "Break", d8Adjustment: 1);
        Assert.InRange(roll.D8Adjustment, 0, 1);
        Assert.True(CombatEngine.IsValidAction("Fateweaver", roll));
        Assert.False(CombatEngine.IsValidAction("Flamecaller", roll));
    }

    [Fact]
    public void Gravebound_below_half_hp_deals_bonus_damage()
    {
        var result = CombatEngine.Resolve(
            "Gravebound",
            new CombatAction("Eihwaz", "Bolt", "Break"),
            new CombatPlayerState(Hp: 15, Mana: 10),
            "Flamecaller",
            new CombatAction("Kenaz", "Bolt", "Break"),
            new CombatPlayerState(Hp: 30));

        Assert.True(result.PlayerB.Hp < 30);
    }

}
