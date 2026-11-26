namespace RuneReign.Domain;

public sealed record ShadeShaping(string? PickForm);

// The computer opponent for practice duels. Both players' dice are rolled in the open at the
// start of each turn, so the Shade reads the board the same way a player does.
public static class ShadeBot
{
    public const string Easy = "easy";
    public const string Hard = "hard";
    // The guided first duel. Plays like Easy.
    public const string Tutorial = "tutorial";
    private static readonly string[] States = ["Break", "Flow", "Bind"];

    public static bool IsDifficulty(string? value) => value is Easy or Hard or Tutorial;

    // The state that beats the given one: Break beats Flow, Flow beats Bind, Bind beats Break.
    public static string Counter(string state) => state switch
    {
        "Flow" => "Break",
        "Bind" => "Flow",
        _ => "Bind"
    };

    private static int Clash(string mine, string theirs) =>
        mine == theirs ? 0 : Counter(theirs) == mine ? 1 : -1;

    // Shaping happens at the start of the turn, before either side declares, so the player can see it.
    public static ShadeShaping Shape(string difficulty, string form, int mana, int hp, int opponentHp)
    {
        if (difficulty != Hard || mana < CombatEngine.FormPickCost) return new ShadeShaping(null);
        // Badly hurt: heal.
        if (hp <= 10 && form != "Drain") return new ShadeShaping("Drain");
        // Opponent is low and the Shade can take the recoil: go for the finish.
        if (opponentHp <= 12 && hp > 8 && form != "Nova") return new ShadeShaping("Nova");
        return new ShadeShaping(null);
    }

    // How likely the opponent is to declare each state, from what is on the table.
    public static Dictionary<string, double> Predict(string opponentForm, IReadOnlyList<string> opponentHistory)
    {
        var odds = States.ToDictionary(x => x, _ => 1.0);
        // Players like to commit their form for the +2.
        odds[CombatEngine.GetFormState(opponentForm)] += 1.5;
        // ...and they tend to repeat themselves.
        if (opponentHistory.Count > 0 && odds.ContainsKey(opponentHistory[^1])) odds[opponentHistory[^1]] += 0.8;
        if (opponentHistory.Count > 1 && opponentHistory[^1] == opponentHistory[^2]) odds[opponentHistory[^1]] += 0.6;
        var total = odds.Values.Sum();
        return odds.ToDictionary(x => x.Key, x => x.Value / total);
    }

    public static string Declare(string difficulty, string myForm, string opponentForm, IReadOnlyList<string> opponentHistory, Random random)
    {
        var myCommit = CombatEngine.GetFormState(myForm);
        if (difficulty != Hard)
            // Easy: commits to its own form half the time, otherwise picks at random.
            return random.NextDouble() < 0.5 ? myCommit : States[random.Next(States.Length)];

        // Hard: pick the state with the best expected clash, valuing its own commit bonus,
        // but stay a little unpredictable so it cannot be read perfectly in return.
        if (random.NextDouble() < 0.15) return States[random.Next(States.Length)];
        var odds = Predict(opponentForm, opponentHistory);
        var best = States
            .Select(state => (state, score: odds.Sum(o => o.Value * Clash(state, o.Key)) + (state == myCommit ? 0.35 : 0)))
            .OrderByDescending(x => x.score)
            .First();
        return best.state;
    }
}
