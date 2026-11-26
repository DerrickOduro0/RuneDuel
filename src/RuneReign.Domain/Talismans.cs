namespace RuneReign.Domain;

// What a talisman changes inside one round's resolution.
public sealed record TalismanRoundEffects(bool CommitAnyState = false, bool BlockWinnerBonusIfLost = false)
{
    public static readonly TalismanRoundEffects None = new();
}

// A player's talisman during a duel: which one, how many charges are spent, and short-lived flags.
public sealed record TalismanState(string? Key, int Uses = 0, string Flags = "")
{
    public bool Has(string flag) => Flags.Split(',', StringSplitOptions.RemoveEmptyEntries).Contains(flag);
    public TalismanState With(string flag) => Has(flag) ? this : this with { Flags = string.Join(",", Flags.Split(',', StringSplitOptions.RemoveEmptyEntries).Append(flag)) };
    public TalismanState Without(string flag) => this with { Flags = string.Join(",", Flags.Split(',', StringSplitOptions.RemoveEmptyEntries).Where(x => x != flag)) };
}

// Talismans bend one rule each. They never add flat damage, health or shield.
// The definitions (names, prices, text) live in the database; the behaviour lives here, keyed by talisman key.
public static class Talismans
{
    public const string LuckyKnot = "lucky-knot";       // first reroll each duel is free
    public const string MirrorShard = "mirror-shard";   // active: flip the D8 to its opposite face (1 mana)
    public const string EmberCoal = "ember-coal";       // lose a clash: +1 mana. Start with 1 less mana
    public const string IronSeal = "iron-seal";         // once: when you lose a clash, the winner gets no +2. Start at 28 HP
    public const string GamblersCoin = "gamblers-coin"; // active x2: reroll D6 and D8 together (1 HP each)
    public const string PatientStone = "patient-stone"; // active x2: +1 mana now, but no other shaping this turn
    public const string SecondWind = "second-wind";     // once: first time HP drops to 10 or less, +1 mana and a free reroll
    public const string TwinMark = "twin-mark";         // active once: any state you declare this turn earns the +2 (2 mana)

    // Per-turn flags.
    public const string FlagStill = "still";       // Patient Stone used: no other shaping this turn
    public const string FlagTwin = "twin";         // Twin Mark active this turn
    public const string FlagFreeRoll = "freeroll"; // Second Wind: next reroll is free
    public const string FlagWindUsed = "wind";     // Second Wind already fired
    public const string FlagUsed = "used";         // an active talisman was used this turn

    public static readonly string[] All = [LuckyKnot, MirrorShard, EmberCoal, IronSeal, GamblersCoin, PatientStone, SecondWind, TwinMark];

    public static bool IsKnown(string? key) => key is not null && All.Contains(key);

    public static int Charges(string? key) => key switch
    {
        LuckyKnot or MirrorShard or IronSeal or SecondWind or TwinMark => 1,
        GamblersCoin or PatientStone => 2,
        _ => 0
    };

    // Active talismans are used with the Use button during shaping. The rest fire on their own.
    public static bool IsActive(string? key) => key is MirrorShard or GamblersCoin or PatientStone or TwinMark;

    public static int UseManaCost(string? key) => key switch { MirrorShard => 1, TwinMark => 2, _ => 0 };
    public static int UseHpCost(string? key) => key == GamblersCoin ? 1 : 0;

    public static int StartHp(string? key) => key == IronSeal ? 28 : 30;
    public static int StartManaChange(string? key) => key == EmberCoal ? -1 : 0;

    public static int ChargesLeft(TalismanState t) => Math.Max(0, Charges(t.Key) - t.Uses);

    // The opposite D8 face: Bolt <-> Nova, Wave <-> Echo, Ward <-> Surge, Drain <-> Curse.
    public static string FlipForm(string form)
    {
        var forms = CombatEngine.GetForms();
        var index = forms.ToList().IndexOf(form);
        return index < 0 ? form : forms[forms.Count - 1 - index];
    }

    // Shaping is blocked for the rest of the turn after Patient Stone.
    public static bool ShapingLocked(TalismanState t) => t.Has(FlagStill);

    // The next reroll is free from Lucky Knot (one per duel) or Second Wind.
    public static bool RerollIsFree(TalismanState t) =>
        (t.Key == LuckyKnot && t.Uses < 1) || t.Has(FlagFreeRoll);

    // Called when a reroll is made; spends the free reroll if there was one.
    public static (TalismanState State, bool Fired) SpendReroll(TalismanState t)
    {
        if (t.Has(FlagFreeRoll)) return (t.Without(FlagFreeRoll), true);
        if (t.Key == LuckyKnot && t.Uses < 1) return (t with { Uses = t.Uses + 1 }, true);
        return (t, false);
    }

    public static TalismanRoundEffects RoundEffects(TalismanState t) => new(
        CommitAnyState: t.Key == TwinMark && t.Has(FlagTwin),
        BlockWinnerBonusIfLost: t.Key == IronSeal && t.Uses < 1);

    // After the round: triggered talismans react. clash: 1 = this player won it, -1 = lost it, 0 = even.
    public static (TalismanState State, int Mana, bool Fired) AfterRound(TalismanState t, int clash, bool bonusBlocked, int hp, int mana)
    {
        var fired = false;
        if (t.Key == IronSeal && bonusBlocked) { t = t with { Uses = t.Uses + 1 }; fired = true; }
        if (t.Key == EmberCoal && clash < 0) { mana = Math.Min(CombatEngine.ManaCap, mana + 1); fired = true; }
        if (t.Key == SecondWind && !t.Has(FlagWindUsed) && hp > 0 && hp <= 10)
        {
            t = t.With(FlagWindUsed).With(FlagFreeRoll) with { Uses = t.Uses + 1 };
            mana = Math.Min(CombatEngine.ManaCap, mana + 1);
            fired = true;
        }
        return (t, mana, fired);
    }

    // Clears flags that last one turn.
    public static TalismanState NewTurn(TalismanState t) => t.Without(FlagStill).Without(FlagTwin).Without(FlagUsed);
}
