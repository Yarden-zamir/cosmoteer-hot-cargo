"""Hot Cargo economy model (BALANCE.md).

Reads the vanilla numbers from the game data (tier tables, economy levels, tech costs) and the measured numbers from the
balance autotests (tests/autotest/balance-*.txt logs), then models one run sector by sector for the old and the new tunables.
Writes tools/balance/model.json, which docs/balance.html embeds.

Usage: uv run --no-project tools/balance/model.py <game Data folder> <census log before> <hunters log> <census log after>
"""

import json
import re
import statistics
import sys
from pathlib import Path

DATA = Path(sys.argv[1])
CENSUS = Path(sys.argv[2]).read_text()
HUNTERS = Path(sys.argv[3]).read_text()
CENSUS_AFTER = Path(sys.argv[4]).read_text()
OUT = Path(__file__).with_name("model.json")
# Heat stages of the current defaults, read from the C# tunables, so the charts follow the stage list.
TUNABLES_CS = (Path(__file__).parents[2] / "src/HotCargo/Core/Tunables.cs").read_text()
STAGES = [(float(h), n) for n, h in re.findall(r'new\(\) \{ Name = "(\w+)", Heat = ([\d.]+)f \}', TUNABLES_CS)]
if not STAGES: raise SystemExit("no heat stages found in Tunables.cs")

# ---- Vanilla data -------------------------------------------------------------------------------------------------------
tier_tables = (DATA / "modes/career/tier_tables.rules").read_text()
tvm_block = tier_tables.split("TierValueMaximums")[1].split("]")[0]
TVM = [int(x) for x in re.findall(r"^\s*(\d+),", tvm_block, re.M)]
assert len(TVM) == 20, TVM


def tvm(tier: int) -> int:
    return TVM[min(max(tier, 1), 20) - 1]


def std_money(tier: int) -> float:
    """Middle of StandardMoneyPerTier before reward factors (tier_tables.rules: 0.05 x the value maximums)."""
    lo = tvm(tier) * 2 / 3 if tier == 1 else tvm(tier - 1)
    return 0.05 * (lo + tvm(tier)) / 2


MAX_FAME_MULT = 10
VALUE_MAX_FAME = 10000 / tvm(18) / MAX_FAME_MULT


def max_fame(tier: int) -> int:
    offset = 0 if tier >= 18 else -160 / MAX_FAME_MULT
    return round(tvm(tier) * VALUE_MAX_FAME + offset) * MAX_FAME_MULT


def std_fame(tier: int) -> float:
    return tvm(tier) * max_fame(1) / tvm(1) / 8


career = (DATA / "modes/career/career.rules").read_text()
fast = career.split('ID = "fast"')[1].split("}")[0]
FAST_MONEY = float(re.search(r"MoneyRewardFactor = (\d+)%", fast).group(1)) / 100
FAST_FAME = float(re.search(r"FameRewardFactor = (\d+)%", fast).group(1)) / 100
start_money_expr = re.search(r"StartingMoney = ([\d+]+)", fast).group(1)
FAST_START_MONEY = sum(int(x) for x in start_money_expr.split("+"))

techs = (DATA / "modes/career/techs.rules").read_text()
TECHS = [(m.group(1), int(m.group(2))) for m in re.finditer(r"ID = (\S+)[\s\S]*?Cost = (\d+)", techs)]
TECH_BASE = sum(c for i, c in TECHS if "overclock" not in i)
TECH_OVERCLOCK = sum(c for i, c in TECHS if "overclock" in i)

# ---- Measured: mission boards (balance-census) -------------------------------------------------------------------------
offers = re.findall(r"BALANCE (s\d) offer type=(\S+) cat=(\S+) money=(\S+) fame=(\S+) fee=(\S+) flatHeat=(\S+) trail=(\S+) informant=(\S+)", CENSUS)
systems = {m[0]: dict(sector=int(m[1]), tier=int(m[2]), type=m[3]) for m in
           re.findall(r"BALANCE (s\d) system sector=(\d+) tier=(\d+) type=(\S+)", CENSUS)}
fuel_lines = {m[0]: [int(x) for x in m[1].split(",") if x] for m in re.findall(r"BALANCE (s\d) fuel stationStock=\[([-\d,]*)\]", CENSUS)}
distances = [float(d) for m in re.findall(r"mapDistances=\[([\d.,]*)\]", CENSUS) for d in m.split(",") if d]
GALAXY = {"VisitSystem", "Explore"}
board = {}
for s in systems:
    mine = [o for o in offers if o[0] == s and o[1] not in GALAXY]
    board[s] = dict(total=len(mine), hotcargo=sum(o[1].startswith("HotCargo") for o in mine),
                    chains=sum(o[1] == "HotCargoChain" for o in mine))
by_type = {}
for o in offers:
    if o[1] in GALAXY or float(o[3]) < 0:
        continue
    by_type.setdefault(o[1], []).append((float(o[3]), float(o[4]), float(o[5]), float(o[6]), float(o[8])))
MISSION_TYPES = {k: dict(n=len(v), money=round(statistics.mean(x[0] for x in v), 2), fame=round(statistics.mean(x[1] for x in v), 2),
                         fee=round(statistics.mean(x[2] for x in v), 2), flatHeat=round(statistics.mean(x[3] for x in v), 2))
                 for k, v in sorted(by_type.items())}
# A typical pick pays like the board mean (deliveries left out: they pay little and give no fame).
picks = [x for k, v in by_type.items() if k != "ResourceDelivery" for x in v]
PICK_MONEY = statistics.mean(x[0] for x in picks)
MISSION_FAME = statistics.median(x[1] for x in picks)
MISSION_FLAT = statistics.mean(x[3] for x in picks)
MISSION_INFORMANT = statistics.mean(x[4] for x in picks)

offers_after = re.findall(r"BALANCE (s\d) offer type=(\S+) ", CENSUS_AFTER)
board_after = {}
for s_, t_ in offers_after:
    if t_ in GALAXY:
        continue
    b = board_after.setdefault(s_, dict(total=0, hotcargo=0, chains=0))
    b["total"] += 1
    b["hotcargo"] += t_.startswith("HotCargo")
    b["chains"] += t_ == "HotCargoChain"
fuel_after = {m[0]: [int(x) for x in m[1].split(",") if x] for m in re.findall(r"BALANCE (s\d) fuel stationStock=\[([-\d,]*)\]", CENSUS_AFTER)}

# ---- Measured: hunters (balance-hunters) -------------------------------------------------------------------------------
probes = re.findall(r"probe tier=(\d+) ship=.*? mass=(\d+) value=(\d+) fuel=(\d+)", HUNTERS)
salvage = {int(t): int(v) for t, v in re.findall(r"salvage t(\d+) nuggetValue=(\d+)", HUNTERS)}
HUNTER = {}
for t in sorted({int(p[0]) for p in probes}):
    rows = [p for p in probes if int(p[0]) == t]
    value = sum(int(p[2]) for p in rows)
    HUNTER[t] = dict(mass=round(statistics.mean(int(p[1]) for p in rows)), value=round(value / len(rows)),
                     salvageFraction=round(salvage[t] / value, 3))

# ---- Run model ---------------------------------------------------------------------------------------------------------
ROUTE = [1, 3, 5, 7, 9, 11, 12, 12]  # main route low tier per sector (galaxy.rules: +2 per jump up to FinalSectorTier)
MISSIONS_PER_SECTOR = 6
MINUTES_PER_SECTOR = 17.5
KILLS_PER_SECTOR = 1.5  # hunters destroyed per sector before the players jump at Hunt (Warning: 1 early, 2 late), salvage only
JUMP_LY = statistics.mean(distances) if distances else 7.5

OLD = dict(name="before", RewardScale=2.0, HeatPerGameMinute=2.0, HeatPerStandardMission=15.0, FameCapLeak=True,
           EscortHeatChange=-15, TechCostScale=1.0, FuelPrice=50, FuelStack=20, StationFuelTyped=0.85, StationFuelUntyped=0.05,
           JumpCostScale=1.0, HunterFuel=40, ChainsPerSystem=None, StationFuelPerMillion=0)
NEW = dict(name="after", RewardScale=1.0, HeatPerGameMinute=1.14, HeatPerStandardMission=1.425, FameCapLeak=False,
           EscortHeatChange=-8, TechCostScale=2.5, FuelPrice=150, FuelStack=10, StationFuelTyped=0.325, StationFuelUntyped=0.01,
           JumpCostScale=2.0, HunterFuel=60, ChainsPerSystem=2, StationFuelPerMillion=100)
MULT = 1.2  # heat multiplier with 8 tech units
CARRY = 0.25
CONTRACT_HEAT = MISSION_FLAT + MISSION_INFORMANT * 0.75 * 15  # flat heat and expected informant heat per accepted contract


def tech_value(tier, hours, notoriety):
    return tvm(tier) * (0.25 + 0.10 * hours + 0.05 * notoriety / 100)


def run(p):
    rows, money, fame, heat_in, notoriety, hours = [], FAST_START_MONEY - 65350, 0, 0.0, 0.0, 0.0
    fleet = tvm(1)
    for i, tier in enumerate(ROUTE):
        sector = i + 1
        std = std_money(tier) * FAST_MONEY * p["RewardScale"]
        sfame = std_fame(tier) * FAST_FAME * p["RewardScale"]
        income = MISSIONS_PER_SECTOR * PICK_MONEY * std
        salvage_income = KILLS_PER_SECTOR * HUNTER.get(tier, HUNTER[max(HUNTER)])["salvageFraction"] * tvm(tier)
        # Fame: the cap of the highest mission tier in the system (missions use the high tier, about tier + 2).
        cap = max_fame(tier + 2)
        gained = min(MISSIONS_PER_SECTOR * MISSION_FAME * sfame, max(0, cap - fame))
        fame += gained
        # Heat. Fame heat counts uncapped fame unless the cap leak is in place (then only the fame below the cap).
        fame_units = (gained / sfame) if p["FameCapLeak"] else MISSIONS_PER_SECTOR * MISSION_FAME
        h_missions = MULT * p["HeatPerStandardMission"] * fame_units
        h_time = MULT * p["HeatPerGameMinute"] * MINUTES_PER_SECTOR
        h_contracts = MISSIONS_PER_SECTOR * CONTRACT_HEAT
        raw = heat_in + h_missions + h_time + h_contracts
        heat_out = min(100.0, raw)
        notoriety += heat_out - heat_in
        hours += MINUTES_PER_SECTOR / 60
        # Missions until the Strike stage (time heat included pro rata).
        per_mission = MULT * p["HeatPerStandardMission"] * MISSION_FAME + CONTRACT_HEAT + h_time / MISSIONS_PER_SECTOR
        missions_to_strike = (100 - heat_in) / per_mission
        target = tvm(min(ROUTE[min(i + 1, len(ROUTE) - 1)] + 1, 20))
        money += income + salvage_income
        spend = max(0.0, min(money, target - fleet))
        fleet += spend
        money -= spend
        jump_fuel = p["JumpCostScale"] * HUNTER.get(tier, HUNTER[max(HUNTER)])["mass"] * JUMP_LY / 1000
        # Relay jumps cost fuel (FtlManager checks the map jump cost for a gate jump of a human ship). Worst case of one jump:
        # a fleet of the next tier, 10 LY, 70 % drive efficiency. Spec 304 keeps this much fuel at every trade station.
        next_mass = HUNTER[min((k for k in HUNTER if k >= tier + 1), default=max(HUNTER))]["mass"]
        worst_jump = p["JumpCostScale"] * next_mass * 10 / 1000 / 0.7
        station_floor = max(10, -(-tvm(tier + 1) * p["StationFuelPerMillion"] // 1_000_000)) if p["StationFuelPerMillion"] else 0
        rows.append(dict(sector=sector, tier=tier, stdMoney=round(std), income=round(income), salvage=round(salvage_income),
                         incomeShipFraction=round((income + salvage_income) / tvm(tier), 2), fleetTarget=target,
                         fleet=round(fleet), bank=round(money), fame=round(fame), fameCap=cap, heatIn=round(heat_in, 1),
                         heatMissions=round(h_missions, 1), heatTime=round(h_time, 1), heatContracts=round(h_contracts, 1),
                         heatOut=round(heat_out, 1), heatRaw=round(raw, 1), missionsToStrike=round(missions_to_strike, 1),
                         techValue=round(tech_value(tier, hours, notoriety)), techValueShipFraction=round(tech_value(tier, hours, notoriety) / tvm(tier), 2),
                         techUnitInMissions=round(tech_value(tier, hours, notoriety) / (PICK_MONEY * std), 1),
                         jumpFuel=round(jump_fuel, 1), worstJumpFuel=round(worst_jump, 1), stationFuelFloor=int(station_floor), jumpFuelMoney=round(jump_fuel * p["FuelPrice"]),
                         jumpFuelInMissions=round(jump_fuel * p["FuelPrice"] / (PICK_MONEY * std), 3)))
        heat_in = heat_out * CARRY
    total_income = sum(r["income"] + r["salvage"] for r in rows)
    return dict(params=p, sectors=rows, totalIncome=round(total_income),
                techBase=round(TECH_BASE * p["TechCostScale"]), techOverclock=round(TECH_OVERCLOCK * p["TechCostScale"]),
                techBaseShareOfIncome=round(TECH_BASE * p["TechCostScale"] / total_income, 3),
                score=round(sum(r["bank"] for r in rows[-1:]) + 8 * 4 * rows[-1]["techValue"]))


result = dict(
    vanilla=dict(tvm=TVM[:15], stdMoney=[round(std_money(t)) for t in range(1, 16)], stdFame=[round(std_fame(t), 1) for t in range(1, 16)],
                 maxFame=[max_fame(t) for t in range(1, 16)], fastMoney=FAST_MONEY, fastFame=FAST_FAME, startMoneyAfterShip=FAST_START_MONEY - 65350,
                 techBase=TECH_BASE, techOverclock=TECH_OVERCLOCK, techCount=len(TECHS)),
    measured=dict(board=board, boardAfter=board_after, stationFuelAfter=fuel_after, missionTypes=MISSION_TYPES, pickMoney=round(PICK_MONEY, 2),
                  missionFame=round(MISSION_FAME, 2), contractHeat=round(CONTRACT_HEAT, 2), hunters=HUNTER,
                  stationFuel={s: v for s, v in fuel_lines.items()}, systems=systems, jumpLY=round(JUMP_LY, 1)),
    model=dict(missionsPerSector=MISSIONS_PER_SECTOR, minutesPerSector=MINUTES_PER_SECTOR, route=ROUTE, stages=STAGES),
    before=run(OLD), after=run(NEW),
)
OUT.write_text(json.dumps(result, indent=1))
for k in ("before", "after"):
    r = result[k]
    print(k, "income", r["totalIncome"], "tech base", r["techBase"], "share", r["techBaseShareOfIncome"], "score", r["score"])
    for s in r["sectors"]:
        print("  ", {x: s[x] for x in ("sector", "tier", "income", "incomeShipFraction", "fleet", "bank", "fame", "heatIn", "heatMissions",
                                         "heatTime", "heatContracts", "heatRaw", "missionsToStrike", "techValueShipFraction", "techUnitInMissions",
                                         "jumpFuel", "worstJumpFuel", "stationFuelFloor", "jumpFuelInMissions")})
print("board after", board_after, "fuel after", fuel_after)
print("pick money", PICK_MONEY, "mission fame", MISSION_FAME, "contract heat", CONTRACT_HEAT, "techs", TECH_BASE, TECH_OVERCLOCK)
print("hunters", HUNTER)
