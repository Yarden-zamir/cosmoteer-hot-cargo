namespace HotCargo.Stash;

/// <summary>What the players find at a stash (spec 972). The integer values are saved.</summary>
public enum StashState
{
    /// <summary>The players have not reached the stash yet.</summary>
    Waiting = 0,
    /// <summary>The stash was there: the objective completed with its loot.</summary>
    Looted = 1,
    /// <summary>The corporation found it first and took everything.</summary>
    Empty = 2,
    /// <summary>The corporation found it first and waits there. The objective completes when the ambushers are gone, without loot.</summary>
    Ambush = 3,
}

/// <summary>Pure math of the timed stash (spec 971, 972). No game types.</summary>
public static class StashModel
{
    /// <summary>Spec 971: the chance that the corporation found the stash first, after this many game seconds since the offer.</summary>
    public static float FoundFirstChance(double secondsSinceOffer, float baseChance, float perMinute, float max) =>
        Math.Clamp(baseChance + perMinute * (float)(Math.Max(0, secondsSinceOffer) / 60.0), 0f, Math.Clamp(max, 0f, 1f));

    /// <summary>Spec 971: the chance as a whole percent, as the offer and the mission log show it.</summary>
    public static int Percent(float chance) => (int)MathF.Round(chance * 100f);

    /// <summary>Spec 972: the outcome for two rolls in [0, 1).</summary>
    public static StashState Outcome(float foundRoll, float foundChance, float emptyRoll, float emptyShare) =>
        foundRoll >= foundChance ? StashState.Looted : emptyRoll < emptyShare ? StashState.Empty : StashState.Ambush;
}
