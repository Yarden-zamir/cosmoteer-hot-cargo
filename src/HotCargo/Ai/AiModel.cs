namespace HotCargo.Ai;

/// <summary>Flee state of one ship. Rolled is true after the chance roll for the current drop below the flee health.</summary>
public readonly record struct FleeState(bool Fleeing, bool Rolled);

public enum CallStep { Wait, Start, Continue, Complete, Blocked }

/// <summary>Pure decisions of the enemy AI behaviors (spec 193-201) on plain numbers. No game types.</summary>
public static class AiModel
{
    /// <summary>
    /// Spec 193-194. A ship without offensive weapons always flees. A ship that drops below the flee health rolls once
    /// against the flee chance; it rolls again only after its health is back at or above the flee health.
    /// A fleeing ship stops only when canStop (vanilla AI) and it has weapons and health at or above the recover health.
    /// The roll function runs only when a roll is due, so the random stream advances the same way on every client.
    /// </summary>
    public static FleeState NextFlee(FleeState s, bool hasWeapons, float health, float fleeHealth, float recoverHealth,
        float chance, bool canStop, Func<float> roll)
    {
        if (!hasWeapons) return s with { Fleeing = true };
        if (s.Fleeing) return canStop && health >= recoverHealth ? s with { Fleeing = false } : s;
        if (health >= fleeHealth) return s with { Rolled = false };
        if (s.Rolled) return s;
        return new FleeState(Fleeing: roll() < chance, Rolled: true);
    }

    /// <summary>Spec 196. A ship that lost at least this health fraction since its last check took damage.</summary>
    public static bool TookDamage(float lastHealth, float health, float threshold) => lastHealth - health >= threshold;

    /// <summary>
    /// Spec 196-199. Next step of a call for help. An idle ship starts a call when it is triggered, someone can answer,
    /// it is not cooling down, no ally near it calls already, and it is not in a storm. A storm stops a running call.
    /// </summary>
    public static CallStep NextCall(bool calling, double elapsedSeconds, double callSeconds, bool triggered, bool hasListeners,
        bool inStorm, bool coolingDown, bool allyCalling)
    {
        if (calling)
        {
            if (inStorm) return CallStep.Blocked;
            return elapsedSeconds >= callSeconds ? CallStep.Complete : CallStep.Continue;
        }
        if (!triggered || !hasListeners || coolingDown || allyCalling) return CallStep.Wait;
        return inStorm ? CallStep.Blocked : CallStep.Start;
    }

    /// <summary>Progress of a running call, 0 to 1. Drives the visible pulse.</summary>
    public static float CallProgress(double elapsedSeconds, double callSeconds) =>
        callSeconds <= 0 ? 1f : (float)Math.Clamp(elapsedSeconds / callSeconds, 0, 1);

    /// <summary>
    /// Spec 201. A ship weaker than its target holds back near its allies instead of charging alone.
    /// It regroups while fewer than minGroup ships (itself included) are close, some allies are still far, and its time did not run out.
    /// </summary>
    public static bool ShouldRegroup(float ownFirepower, float targetFirepower, float firepowerRatio, int alliesNear, int alliesTotal,
        int minGroup, bool timedOut, bool coolingDown) =>
        !timedOut && !coolingDown
        && alliesTotal > alliesNear
        && alliesNear + 1 < Math.Min(minGroup, alliesTotal + 1)
        && ownFirepower < targetFirepower * firepowerRatio;

    /// <summary>Spec 200. Attack value factor for a target: weakened targets count more. Health is clamped to 0..1.</summary>
    public static float FocusFactor(float targetHealth, float weakFactor) => 1f + weakFactor * (1f - Math.Clamp(targetHealth, 0f, 1f));
}
