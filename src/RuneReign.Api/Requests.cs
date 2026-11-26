namespace RuneReign.Api;

public sealed record RegisterRequest(string Username, string Email, string Password);
public sealed record LoginRequest(string Identifier, string Password);
public sealed record ChooseClassRequest(string ClassName);
public sealed record TestResultRequest(bool? Win);
public sealed record CombatActionRequest(string State, int D8Adjustment = 0, bool Reroll = false, string? ChosenForm = null);

public sealed record StartShadeRequest(string? Difficulty);

// The Shade is not a real player; practice duels use this id for its side.
public static class ShadeOpponent
{
    public static readonly Guid Id = Guid.Parse("00000000-0000-0000-0000-000000005ade");
}

// Shaping your dice before you declare. Kind: "reroll" (rune die), "pick" (choose the D8 face) or "shift" (Fateweaver: D8 up or down one).
public sealed record ShapeDiceRequest(string Kind, string? Form = null, int Direction = 0);

public sealed record CompleteTutorialRequest(bool Finished);

public sealed record QueueRequest(string? Mode);
public sealed record JoinChallengeRequest(string Code);

public sealed record RematchState(bool Available, bool Mine, bool Theirs, Guid? MatchId, int SecondsLeft);
