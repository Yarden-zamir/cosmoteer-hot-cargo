# Plan: enemy AI round 3 (feat/ai3)

## Decisions
- Side profile: on the existing build profile refresh (`HcPersonality`, `ProfileRefreshSeconds`), measure the cover of each
  vulnerable part (part categories `command`, `reactor`, the tech vault) in 8 ship-local directions: cells between the part and
  the hull edge, armor cells weigh more. A direction is weak when its cover is below a tunable. The strong direction has the most
  cover (weapons facing it break ties). Saved with the ship and in the integrity hash.
- Moves that run from or near a threat (flee, fall back, regroup, bait, cover hide) use one guarded move helper: when plain
  travel would point a weak side at the threat, the move keeps the strong side toward the threat (MoveCommand with forced
  rotation relative to the threat).
- Build fit: Kite needs a guarded back (vanilla AttackCommand turns the ship to travel when it backs off far). Bait needs a
  guarded side. Fits stay in `PersonalityModel.Fit`.
- Tells removed: "IN COVER" label, personality mouseover line. Call ring and "CALLING HELP n%" stay. Debug log stays.
- Call: `CallSeconds` default 6 -> 15.

## Steps
1. PLAN.md, draft PR
2. Side profile in model + measure + tunables, unit tests
3. Guarded move helper; flee, fall back, regroup, bait, cover use it; kite and bait fit
4. Remove tells, strings, spec 368/366/49
5. Longer call, spec 197/16, TUNABLES.md
6. Autotests: ai-personality extend, ai-weakside new, ai-call check, co-op ai
7. Merge origin/main, delete PLAN.md, final push
