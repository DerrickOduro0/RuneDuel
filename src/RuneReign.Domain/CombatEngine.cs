namespace RuneReign.Domain;

public sealed record CombatAction(string Rune, string Form, string State, int D8Adjustment = 0);
public sealed record CombatRoll(string Rune, string Form, string State);
public sealed record CombatPlayerState(int Hp = 30, int Shield = 0, int Mana = 0, bool Stunned = false);
// Equipment bonuses, already capped by the server. Focus only changes starting mana, so it is not here.
// Edge: +damage when you win the clash. Pierce: ignores that much enemy shield. Resolve: -damage taken when you lose the clash.
public sealed record CombatBonuses(int Power = 0, int Guard = 0, int Vigor = 0, int Grit = 0, int Edge = 0, int Pierce = 0, int Resolve = 0)
{
    public static readonly CombatBonuses None = new();
}
public sealed record CombatResult(
    CombatPlayerState PlayerA,
    CombatPlayerState PlayerB,
    string Log,
    string? Winner,
    string? Loser,
    bool IsDraw);

public static class CombatEngine
{
    public const int StartMana = 1;
    public const int ManaCap = 5;
    public const int FormPickCost = 2;
    private static readonly string[] Forms = ["Bolt", "Wave", "Ward", "Drain", "Curse", "Surge", "Echo", "Nova"];
    private static readonly Dictionary<string, string> FormTypes = new()
    {
        ["Bolt"] = "Break", ["Nova"] = "Break", ["Wave"] = "Flow", ["Surge"] = "Flow",
        ["Echo"] = "Flow", ["Ward"] = "Bind", ["Drain"] = "Bind", ["Curse"] = "Bind"
    };
    private static readonly Dictionary<string, string> Beats = new() { ["Break"] = "Flow", ["Flow"] = "Bind", ["Bind"] = "Break" };
    private static readonly Dictionary<string, string[]> Runes = new()
    {
        ["Flamecaller"] = ["Kenaz", "Sowilo", "Nauthiz", "Thurisaz", "Hagalaz", "Dagaz"],
        ["Frostwarden"] = ["Isa", "Algiz", "Uruz", "Othala", "Berkano", "Laguz"],
        ["Fateweaver"] = ["Perthro", "Ansuz", "Tiwaz", "Mannaz", "Gebo", "Fehu"],
        ["Gravebound"] = ["Eihwaz", "Jera", "Ingwaz", "Raidho", "Ehwaz", "Wunjo"]
    };
    private static readonly Dictionary<string, string> RuneSymbols = new()
    {
        ["Fehu"] = "ᚠ", ["Uruz"] = "ᚢ", ["Thurisaz"] = "ᚦ", ["Ansuz"] = "ᚨ",
        ["Raidho"] = "ᚱ", ["Kenaz"] = "ᚲ", ["Gebo"] = "ᚷ", ["Wunjo"] = "ᚹ",
        ["Hagalaz"] = "ᚺ", ["Nauthiz"] = "ᚾ", ["Isa"] = "ᛁ", ["Jera"] = "ᛃ",
        ["Eihwaz"] = "ᛇ", ["Perthro"] = "ᛈ", ["Algiz"] = "ᛉ", ["Sowilo"] = "ᛊ",
        ["Tiwaz"] = "ᛏ", ["Berkano"] = "ᛒ", ["Ehwaz"] = "ᛖ", ["Mannaz"] = "ᛗ",
        ["Laguz"] = "ᛚ", ["Ingwaz"] = "ᛜ", ["Dagaz"] = "ᛞ", ["Othala"] = "ᛟ"
    };

    public static IReadOnlyList<string> GetRunes(string className) => Runes.TryGetValue(className, out var runes) ? runes : [];
    public static IReadOnlyList<string> GetForms() => Forms;
    public static string GetFormState(string form) => FormTypes.TryGetValue(form, out var state) ? state : "Break";
    public static string GetRuneSymbol(string rune) => RuneSymbols.TryGetValue(rune, out var symbol) ? symbol : "ᛟ";
    public static CombatAction Roll(string className, string state, IReadOnlyCollection<string>? excludedRunes = null, int d8Adjustment = 0, string? chosenForm = null)
    {
        var excluded = excludedRunes ?? [];
        var runes = GetRunes(className).Where(rune => !excluded.Contains(rune)).ToArray();
        if (runes.Length == 0) runes = GetRunes(className).ToArray();
        var formIndex = Random.Shared.Next(Forms.Length);
        // Spending mana lets the player pick the D8 face, so only the rune die is rolled.
        var picked = chosenForm is not null ? Array.IndexOf(Forms, chosenForm) : -1;
        if (picked >= 0)
            return new CombatAction(runes[Random.Shared.Next(runes.Length)], Forms[picked], state, 0);
        if (className == "Fateweaver")
            formIndex = Math.Clamp(formIndex + Math.Clamp(d8Adjustment, -1, 1), 0, Forms.Length - 1);
        return new CombatAction(runes[Random.Shared.Next(runes.Length)], Forms[formIndex], state, className == "Fateweaver" ? Math.Clamp(d8Adjustment, -1, 1) : 0);
    }
    // Roll first, then decide: the dice are rolled at the start of each turn, in the open.
    public const int RerollManaCost = 1;

    public static string RollRune(string className, IReadOnlyCollection<string>? excludedRunes, Random random)
    {
        var excluded = excludedRunes ?? [];
        var runes = GetRunes(className).Where(rune => !excluded.Contains(rune)).ToArray();
        if (runes.Length == 0) runes = GetRunes(className).ToArray();
        return runes[random.Next(runes.Length)];
    }

    public static string RollForm(Random random) => Forms[random.Next(Forms.Length)];

    // Fateweaver: move the D8 one face up or down.
    public static string ShiftForm(string form, int direction)
    {
        var index = Array.IndexOf(Forms, form);
        if (index < 0) return form;
        return Forms[Math.Clamp(index + Math.Sign(direction), 0, Forms.Length - 1)];
    }

    public static bool IsValidAction(string className, CombatAction action) =>
        Runes.TryGetValue(className, out var runes) && runes.Contains(action.Rune) &&
        Forms.Contains(action.Form) && FormTypes.ContainsKey(action.Form) &&
        new[] { "Break", "Flow", "Bind" }.Contains(action.State) &&
        action.D8Adjustment is >= -1 and <= 1 &&
        (className == "Fateweaver" || action.D8Adjustment == 0);

    public static CombatResult Resolve(
        string classA, CombatAction actionA, CombatPlayerState stateA,
        string classB, CombatAction actionB, CombatPlayerState stateB,
        CombatBonuses? bonusA = null, CombatBonuses? bonusB = null)
    {
        var manaA = Math.Min(ManaCap, Math.Max(0, stateA.Mana) + 1);
        var manaB = Math.Min(ManaCap, Math.Max(0, stateB.Mana) + 1);
        var gearA = bonusA ?? CombatBonuses.None;
        var gearB = bonusB ?? CombatBonuses.None;
        var a = Calculate(classA, actionA, stateA.Hp, manaA, gearA);
        var b = Calculate(classB, actionB, stateB.Hp, manaB, gearB);
        var damage = new[] { a.Damage, b.Damage };
        var shield = new[] { a.Shield, b.Shield };
        var recoil = new[] { 0, 0 };
        var logs = new List<string> { $"A: {actionA.Rune} + {actionA.Form}. B: {actionB.Rune} + {actionB.Form}." };

        if (actionA.State != actionB.State)
        {
            var winner = Beats[actionA.State] == actionB.State ? 0 : 1;
            var loser = 1 - winner;
            if (FormTypes[winner == 0 ? actionA.Form : actionB.Form] == (winner == 0 ? actionA.State : actionB.State))
            {
                damage[winner] = Math.Min(12, damage[winner] + 2);
                shield[winner] = Math.Min(8, shield[winner] + 2);
                if ((winner == 0 ? actionA.State : actionB.State) == "Bind")
                    recoil[loser] += 2;
                logs.Add($"Mage {(winner == 0 ? "A" : "B")} committed and gained the triangle bonus.");
            }
            damage[loser] = Math.Max(1, damage[loser] - 1);
            // Gear: the winner's Edge sharpens the hit, the loser's Resolve softens it.
            var winnerGear = winner == 0 ? gearA : gearB;
            var loserGear = winner == 0 ? gearB : gearA;
            if (damage[winner] > 0)
                damage[winner] = Math.Clamp(damage[winner] + Math.Max(0, winnerGear.Edge) - Math.Max(0, loserGear.Resolve), 0, 12);
            logs.Add($"{(winner == 0 ? actionA.State : actionB.State)} beats {(loser == 0 ? actionA.State : actionB.State)}.");
        }
        else logs.Add("Same state declared, so there is no triangle effect.");

        var nextA = Apply(classA, stateA, damage[1], shield[0], a.Heal, a.SelfDamage + recoil[0], actionB.Form == "Wave", manaA - a.ManaSpent, gearB.Pierce);
        var nextB = Apply(classB, stateB, damage[0], shield[1], b.Heal, b.SelfDamage + recoil[1], actionA.Form == "Wave", manaB - b.ManaSpent, gearA.Pierce);
        var isDraw = nextA.Hp <= 0 && nextB.Hp <= 0;
        var winnerId = isDraw ? null : nextA.Hp <= 0 ? "B" : nextB.Hp <= 0 ? "A" : null;
        return new CombatResult(nextA, nextB, string.Join(" ", logs), winnerId, winnerId is null ? null : winnerId == "A" ? "B" : "A", isDraw);
    }

    // Spells land at the same time, so a shield raised this turn blocks this turn's damage.
    // Unused shield fades at the end of the turn, except for Frostwardens, who keep it (up to 8).
    // pierce: how many points of this player's shield the attacker's gear ignores.
    private static CombatPlayerState Apply(string className, CombatPlayerState state, int incoming, int gainedShield, int heal, int selfDamage, bool ignoresShield, int mana, int pierce = 0)
    {
        var available = Math.Min(8, Math.Max(0, state.Shield) + gainedShield);
        var absorbed = ignoresShield ? 0 : Math.Min(incoming, Math.Max(0, available - Math.Max(0, pierce)));
        var hp = state.Hp - (incoming - absorbed) - selfDamage + heal;
        var leftover = available - absorbed;
        return state with
        {
            Hp = Math.Clamp(hp, 0, 30),
            Shield = className == "Frostwarden" ? leftover : 0,
            Mana = Math.Clamp(mana, 0, ManaCap)
        };
    }

    private static (int Damage, int Shield, int Heal, int SelfDamage, int ManaSpent) Calculate(string className, CombatAction action, int hp, int mana, CombatBonuses bonus)
    {
        var damage = 0;
        var shield = 0;
        var heal = 0;
        var selfDamage = 0;
        var manaSpent = 0;
        var rune = action.Rune;
        damage = rune switch
        {
            "Kenaz" => 4, "Sowilo" => 4, "Nauthiz" => 3, "Thurisaz" => 6, "Hagalaz" => 7, "Dagaz" => 8,
            "Isa" => 3, "Uruz" => 5, "Perthro" => 2, "Ansuz" => 3, "Tiwaz" => 4, "Gebo" => 3, "Fehu" => 2,
            "Eihwaz" => 4, "Jera" => 2, "Ingwaz" => 3, "Raidho" => 2, "Ehwaz" => 2, "Wunjo" => 4, _ => 0
        };
        if (rune == "Sowilo") heal = 2;
        if (rune == "Eihwaz") heal = 1;
        if (rune == "Thurisaz") selfDamage = 1;
        if (rune == "Hagalaz") selfDamage = 2;
        if (rune == "Dagaz") selfDamage = 2;
        if (rune == "Algiz") shield = 5;
        if (rune == "Othala") { shield = 3; heal = 2; }
        if (rune == "Berkano") heal = 4;
        if (action.Form == "Bolt") damage += 2;
        if (action.Form == "Wave") damage = Math.Max(1, damage - 1);
        if (action.Form == "Ward") { shield += Math.Max(damage, 2); damage = 0; }
        if (action.Form == "Drain") { damage = Math.Max(1, damage - 1); heal += damage; }
        if (action.Form == "Curse") damage += 1;
        if (action.Form == "Nova") { damage += 4; selfDamage += 3; }

        if (className == "Gravebound" && hp <= 15)
            damage += 2;

        // Equipment: Power adds to attacks, Guard to shields, Vigor to healing, Grit softens recoil.
        if (damage > 0) damage += Math.Max(0, bonus.Power);
        if (shield > 0) shield += Math.Max(0, bonus.Guard);
        if (heal > 0) heal += Math.Max(0, bonus.Vigor);
        selfDamage = Math.Max(0, selfDamage - Math.Max(0, bonus.Grit));

        return (Math.Min(12, damage), Math.Min(8, shield), heal, selfDamage, manaSpent);
    }
}
