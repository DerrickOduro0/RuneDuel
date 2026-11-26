using Microsoft.EntityFrameworkCore;
using RuneReign.Domain;

namespace RuneReign.Api;

// Reads and writes a duel's talisman columns as a TalismanState.
public static class CombatMatchTalismans
{
    public static TalismanState Talisman(this CombatMatch match, bool isA) =>
        isA ? new TalismanState(match.TalismanA, match.TalismanUsesA, match.TalismanFlagsA ?? "")
            : new TalismanState(match.TalismanB, match.TalismanUsesB, match.TalismanFlagsB ?? "");

    public static void SetTalisman(this CombatMatch match, bool isA, TalismanState state)
    {
        if (isA) { match.TalismanUsesA = state.Uses; match.TalismanFlagsA = state.Flags; }
        else { match.TalismanUsesB = state.Uses; match.TalismanFlagsB = state.Flags; }
    }

    // Sets each side's talisman and applies its starting cost (Iron Seal: 28 HP, Ember Coal: 1 less mana).
    public static CombatMatch WithTalismans(this CombatMatch match, string? talismanA, string? talismanB)
    {
        match.TalismanA = talismanA;
        match.TalismanB = talismanB;
        match.HpA = Math.Min(match.HpA, Talismans.StartHp(talismanA));
        match.HpB = Math.Min(match.HpB, Talismans.StartHp(talismanB));
        match.ManaA = Math.Max(0, match.ManaA + Talismans.StartManaChange(talismanA));
        match.ManaB = Math.Max(0, match.ManaB + Talismans.StartManaChange(talismanB));
        return match;
    }
}

// Talisman names and text, read from the database at startup, for the duel screen.
public static class TalismanCatalog
{
    private static Dictionary<string, TalismanDefinition> byKey = new();

    public static void Load(RuneDbContext db) =>
        byKey = db.Talismans.AsNoTracking().ToList().ToDictionary(x => x.Key);

    public static object? Dto(TalismanState t, string? firedLastRound)
    {
        if (t.Key is null) return null;
        var def = byKey.GetValueOrDefault(t.Key);
        return new
        {
            key = t.Key,
            name = def?.Name ?? t.Key,
            icon = def?.Icon ?? "✧",
            effect = def?.Effect ?? "",
            drawback = def?.Drawback ?? "",
            active = Talismans.IsActive(t.Key),
            charges = Talismans.Charges(t.Key),
            chargesLeft = Talismans.ChargesLeft(t),
            manaCost = Talismans.UseManaCost(t.Key),
            hpCost = Talismans.UseHpCost(t.Key),
            usedThisTurn = t.Has(Talismans.FlagUsed),
            twinActive = t.Has(Talismans.FlagTwin),
            stillThisTurn = t.Has(Talismans.FlagStill),
            freeReroll = Talismans.RerollIsFree(t),
            firedLastRound = firedLastRound == t.Key
        };
    }
}
