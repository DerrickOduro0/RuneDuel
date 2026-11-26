using RuneReign.Domain;
using Xunit;

namespace RuneReign.Tests;

public class TalismanRuleTests
{
    private static readonly CombatPlayerState Fresh = new(30, 0, 2);

    [Fact]
    public void Mirror_shard_flips_to_the_opposite_face()
    {
        Assert.Equal("Nova", Talismans.FlipForm("Bolt"));
        Assert.Equal("Echo", Talismans.FlipForm("Wave"));
        Assert.Equal("Surge", Talismans.FlipForm("Ward"));
        Assert.Equal("Curse", Talismans.FlipForm("Drain"));
        Assert.Equal("Bolt", Talismans.FlipForm(Talismans.FlipForm("Bolt")));
    }

    [Fact]
    public void Twin_mark_gives_the_bonus_to_any_winning_state()
    {
        // A rolled Bolt (a Break form) but declares Bind and beats Break.
        var a = new CombatAction("Kenaz", "Bolt", "Bind");
        var b = new CombatAction("Isa", "Wave", "Break");
        var without = CombatEngine.Resolve("Flamecaller", a, Fresh, "Frostwarden", b, Fresh);
        var with = CombatEngine.Resolve("Flamecaller", a, Fresh, "Frostwarden", b, Fresh, null, null, new TalismanRoundEffects(CommitAnyState: true), null);
        Assert.True(with.PlayerB.Hp < without.PlayerB.Hp);
    }

    [Fact]
    public void Iron_seal_cancels_the_winners_bonus_once()
    {
        // A commits Bolt with Break and beats B's Flow.
        var a = new CombatAction("Kenaz", "Bolt", "Break");
        var b = new CombatAction("Isa", "Wave", "Flow");
        var normal = CombatEngine.Resolve("Flamecaller", a, Fresh, "Frostwarden", b, Fresh);
        var sealedRound = CombatEngine.Resolve("Flamecaller", a, Fresh, "Frostwarden", b, Fresh, null, null, null, new TalismanRoundEffects(BlockWinnerBonusIfLost: true));
        Assert.True(sealedRound.PlayerB.Hp > normal.PlayerB.Hp);
        Assert.True(sealedRound.BonusBlockedByB);
        Assert.Equal(0, sealedRound.ClashWinner);

        var seal = new TalismanState(Talismans.IronSeal);
        Assert.True(Talismans.RoundEffects(seal).BlockWinnerBonusIfLost);
        var (after, _, fired) = Talismans.AfterRound(seal, -1, true, 25, 1);
        Assert.True(fired);
        Assert.False(Talismans.RoundEffects(after).BlockWinnerBonusIfLost);
    }

    [Fact]
    public void Ember_coal_adds_mana_on_a_lost_clash_only()
    {
        var coal = new TalismanState(Talismans.EmberCoal);
        Assert.Equal(3, Talismans.AfterRound(coal, -1, false, 20, 2).Mana);
        Assert.Equal(2, Talismans.AfterRound(coal, 1, false, 20, 2).Mana);
        Assert.Equal(CombatEngine.ManaCap, Talismans.AfterRound(coal, -1, false, 20, CombatEngine.ManaCap).Mana);
        Assert.Equal(-1, Talismans.StartManaChange(Talismans.EmberCoal));
    }

    [Fact]
    public void Lucky_knot_makes_one_reroll_free()
    {
        var knot = new TalismanState(Talismans.LuckyKnot);
        Assert.True(Talismans.RerollIsFree(knot));
        var (spent, fired) = Talismans.SpendReroll(knot);
        Assert.True(fired);
        Assert.False(Talismans.RerollIsFree(spent));
    }

    [Fact]
    public void Second_wind_fires_once_at_ten_hp()
    {
        var wind = new TalismanState(Talismans.SecondWind);
        var (notYet, _, firedEarly) = Talismans.AfterRound(wind, 0, false, 14, 1);
        Assert.False(firedEarly);
        var (after, mana, fired) = Talismans.AfterRound(notYet, 0, false, 9, 1);
        Assert.True(fired);
        Assert.Equal(2, mana);
        Assert.True(Talismans.RerollIsFree(after));
        var (_, _, again) = Talismans.AfterRound(Talismans.SpendReroll(after).State, 0, false, 5, 1);
        Assert.False(again);
    }

    [Fact]
    public void Patient_stone_and_twin_last_one_turn()
    {
        var t = new TalismanState(Talismans.PatientStone).With(Talismans.FlagStill).With(Talismans.FlagUsed);
        Assert.True(Talismans.ShapingLocked(t));
        var next = Talismans.NewTurn(t);
        Assert.False(Talismans.ShapingLocked(next));
        Assert.False(next.Has(Talismans.FlagUsed));
    }
}

// Plays many duels between two Hard Shades, one carrying a talisman, to check it does not swing the result too far.
public static class TalismanBalanceSim
{
    private static readonly string[] Classes = ["Flamecaller", "Frostwarden", "Fateweaver", "Gravebound"];
    private static readonly HashSet<string> WeakRunes = ["Fehu", "Perthro", "Jera", "Raidho", "Ehwaz"];
    private static readonly Dictionary<string, int> FormValue = new()
    {
        ["Nova"] = 5, ["Bolt"] = 4, ["Drain"] = 3, ["Curse"] = 3, ["Surge"] = 2, ["Ward"] = 2, ["Wave"] = 1, ["Echo"] = 1
    };

    // Returns side A's win share (draws count half).
    public static double WinRate(string? talisman, int duels, int seed)
    {
        var random = new Random(seed);
        double wins = 0;
        for (var i = 0; i < duels; i++)
        {
            var result = Play(talisman, Classes[random.Next(4)], Classes[random.Next(4)], random);
            wins += result;
        }
        return wins / duels;
    }

    private static double Play(string? key, string classA, string classB, Random random)
    {
        var a = new CombatPlayerState(Talismans.StartHp(key), 0, Math.Max(0, CombatEngine.StartMana + Talismans.StartManaChange(key)));
        var b = new CombatPlayerState(30, 0, CombatEngine.StartMana);
        var tal = new TalismanState(key);
        var histA = new List<string>();
        var histB = new List<string>();
        for (var turn = 1; turn <= 40; turn++)
        {
            tal = Talismans.NewTurn(tal);
            var runeA = CombatEngine.RollRune(classA, [], random);
            var formA = CombatEngine.RollForm(random);
            var runeB = CombatEngine.RollRune(classB, [], random);
            var formB = CombatEngine.RollForm(random);
            // Both sides shape like the Hard Shade.
            var shapeB = ShadeBot.Shape(ShadeBot.Hard, formB, b.Mana, b.Hp, a.Hp);
            if (shapeB.PickForm is not null) { formB = shapeB.PickForm; b = b with { Mana = b.Mana - CombatEngine.FormPickCost }; }
            var manaA = a.Mana;
            var hpA = a.Hp;
            var left = Talismans.ChargesLeft(tal);
            // Talisman use, with a simple sensible policy.
            switch (key)
            {
                case Talismans.MirrorShard when left > 0 && manaA >= 1 && FormValue[Talismans.FlipForm(formA)] - FormValue[formA] >= 2:
                    formA = Talismans.FlipForm(formA); manaA -= 1; tal = tal with { Uses = tal.Uses + 1 }; break;
                case Talismans.GamblersCoin when left > 0 && hpA > 8 && WeakRunes.Contains(runeA) && FormValue[formA] <= 2:
                    runeA = CombatEngine.RollRune(classA, [runeA], random); formA = CombatEngine.RollForm(random); hpA -= 1; tal = tal with { Uses = tal.Uses + 1 }; break;
                case Talismans.PatientStone when left > 0 && turn <= 2:
                    manaA = Math.Min(CombatEngine.ManaCap, manaA + 1); tal = tal.With(Talismans.FlagStill) with { Uses = tal.Uses + 1 }; break;
                case Talismans.TwinMark when left > 0 && manaA >= 2 && b.Hp <= 14:
                    manaA -= 2; tal = tal.With(Talismans.FlagTwin) with { Uses = tal.Uses + 1 }; break;
            }
            if (!Talismans.ShapingLocked(tal))
            {
                if (Talismans.RerollIsFree(tal) && WeakRunes.Contains(runeA))
                {
                    runeA = CombatEngine.RollRune(classA, [runeA], random);
                    tal = Talismans.SpendReroll(tal).State;
                }
                var shapeA = ShadeBot.Shape(ShadeBot.Hard, formA, manaA, hpA, b.Hp);
                if (shapeA.PickForm is not null) { formA = shapeA.PickForm; manaA -= CombatEngine.FormPickCost; }
            }
            a = a with { Mana = manaA, Hp = hpA };
            // With Twin Mark active, A just plays the best counter.
            var stateA = tal.Has(Talismans.FlagTwin)
                ? ShadeBot.Counter(ShadeBot.Predict(formB, histB).OrderByDescending(x => x.Value).First().Key)
                : ShadeBot.Declare(ShadeBot.Hard, formA, formB, histB, random);
            var stateB = ShadeBot.Declare(ShadeBot.Hard, formB, formA, histA, random);
            var result = CombatEngine.Resolve(classA, new CombatAction(runeA, formA, stateA), a, classB, new CombatAction(runeB, formB, stateB), b,
                null, null, Talismans.RoundEffects(tal), null);
            var clash = result.ClashWinner is null ? 0 : result.ClashWinner == 0 ? 1 : -1;
            var (nextTal, mana, _) = Talismans.AfterRound(tal, clash, result.BonusBlockedByA, result.PlayerA.Hp, result.PlayerA.Mana);
            tal = nextTal;
            a = result.PlayerA with { Mana = mana };
            b = result.PlayerB;
            histA.Add(stateA); histB.Add(stateB);
            if (result.IsDraw) return 0.5;
            if (result.Winner == "A") return 1;
            if (result.Winner == "B") return 0;
        }
        return 0.5;
    }
}

public class TalismanBalanceTests
{
    [Fact]
    public void No_starter_talisman_swings_an_even_duel_by_more_than_five_points()
    {
        const int duels = 6000;
        var baseline = TalismanBalanceSim.WinRate(null, duels, 11);
        var report = new List<string> { $"baseline {baseline:P1}" };
        var worst = 0.0;
        foreach (var key in Talismans.All)
        {
            var rate = TalismanBalanceSim.WinRate(key, duels, 11);
            report.Add($"{key} {rate:P1} ({(rate - baseline) * 100:+0.0;-0.0} pts)");
            worst = Math.Max(worst, Math.Abs(rate - baseline));
        }
        Console.WriteLine(string.Join("\n", report));
        Assert.True(worst <= 0.05, string.Join("; ", report));
    }
}
