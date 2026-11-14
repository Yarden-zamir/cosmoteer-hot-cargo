# Debt (spec 55, requirements 1400 to 1439)

Three PRs, one branch each, all from origin/main:
1. `feat/debt`: core debt, loan at stations, interest, repay, collectors, 2x/3x stages, wanted, score, heat panel line. Requirements 1400 to 1419.
2. `feat/debt-missions`: same-faction missions pay debt off, plus 2 or 3 debt missions. Requirements 1420 to 1429.
3. `feat/debt-paid-help`: the paid help event. Requirements 1430 to 1439.

## Decisions (PR 1)
- Money unit: the event money unit (spec 73). Loan = `LoanUnits` (5) units at the tier when the loan is taken.
- One debt per faction, a list in the run state. Paid help (PR 3) adds to a debt of another faction, so a single debt does not fit.
- Interest compounds: balance x 1.05 every 600 game seconds, rounded up.
- Stage by ratio = balance / principal: calm, firm (2x), hostile (3x).
- Hostile = vanilla wanted (`WantedManager._wantedShips`): enemies + police. No collectors while wanted. When the wanted state ends, the next collector comes within `ReturnSeconds`.
- A hostile-stage collector that leaves with the ratio still at 3x or more makes the lender want the players again.
- Collector: 1 combat ship of the lender, jumps in near the fleet, hails, waits, jumps out (`Removals.JumpOut`).
- Choices: shared choice domain `debt`, page serial = station or collector ship ID.
- Score = vanilla formula - 2 x debt (`Run.Hooks.ScoreDebt`).
- Display: one heat panel line per debt (new small hook `HeatPanel.ExtraLines`), run log lines (`RunLogKind.Debt`).

## Steps PR 1
- [ ] spec/55-debt.md (core part)
- [ ] DebtModel.cs + unit tests
- [ ] state, tunables, area, tick (interest, stage, collectors, wanted)
- [ ] hail provider + shared choice domain (loan, repay, refuse)
- [ ] score hook, heat panel line, run log
- [ ] autotest debt-core, co-op step debt.loan
- [ ] screenshots tests/screens/debt.txt

## PR 2 design (debt missions, 1420 to 1429)
- 1420 pay-off rewards: a mission of a lender's faction (`Mission.Faction`) pays debt instead of money with a chance seeded by the mission ID. Patch `MoneyReward.GiveReward` and `GetRewardText`. No save state: the roll is a pure function of the mission ID; the line shows while the debt exists.
- 1421 refinance: a station of another faction buys a debt. New debt: principal = balance, interest x `RefinanceRateFactor`. Old lender: reputation down (`SetFactionReputation`), its collectors leave. Station deal in the `debt` domain.
- 1422 debtor hunt ("collect for the lender"): a lender station offers a bounty-like mission on a fleeing debtor. Runtime spawn like `StashArea.Spawn`. Its money reward always pays debt (1420 rule by metatype).
- Not picked: escort the lender's convoy (Convoy Duty exists already); hot crate (needs the hand-over item flow; heat cost overlaps contracts).

## PR 3 design (paid help, 1430 to 1439)
- Damage share from `DamageEffectRules.DoEffect` (source ship, hit ship). Helpers: friendly faction military ships that join a fight.
- After the fight, if their share >= a minimum, they hail: pay (clear), refuse (price added to a debt with that faction), "you didn't ask": works when share < `ClaimMaxShare`, shown in the text; else reputation loss.
