# Plan: one hunter hand-over page

Coordinator session c1728dfe-7288-4494-882c-32a83021cb00. Branch `fix/handover-page`.

## Design
- The hunter demand page has three choices: Pay them off, Bluff, Fight. Demand surrender and accept-yield stay (general enemy talk).
- Pay them off: multiselect rows on the hunter page (multiselect serial = ship ID, shared draft). Rows: Tech (1..stock-1), Money (1..20 steps of `PayMoneyMaxUnits`), Crew to cryo jail (1..available, max `PayCrewMax`).
- Chance = min(`PayChanceCap`, crowd × (`PayTechChance` × tech ÷ stock + `PayMoneyChance` × money share + `PayCrewChancePer` × crew)).
  crowd = 1 − others ÷ (max + 1), 0 above max; others = hunters within `AreaRadius`; max = `PayMaxOtherHunters` × co-op enemy factor.
- Pay success: tech, money and crew go; wave leaves; heat −`HandOverHeatDrop`. Failure: the offered tech goes (they keep it), money and crew stay, that hunter attacks.
- Bluff: plain button, fixed amount = round(stock × `BluffShare`), 1..stock-1. Existing bluff chance. Win: wave leaves, heat drops. Fail: they keep it, attack, heat +`BluffCaughtHeat`.
- Fight: "Come and take it." No cost.
- Removed: punishment page and timer, hand over all (tech row stops at stock-1, so a pay-off never ends the run), deal, talk option, personal bribe (with the crowd factor on the whole offer it is a second money slider).

## Steps
- [ ] Code: model, quotes, deals, hail page, state, tick, tunables, autotest commands
- [ ] Strings
- [ ] Unit tests
- [ ] Spec 37, TUNABLES.md, TunableTiming
- [ ] Autotests diplomacy-hunters, diplomacy-friends (hand-over use), co-op diplomacy; screens script
- [ ] Run tests on the PC
- [ ] Screenshot
- [ ] Merge origin/main, check-private, delete PLAN.md, push
