# Playtest: missions (spec section 11)

The autotests cover offers, sides of the AI ships, escort steering and arrival, the trap formula, the ambush and its reward, and save and load. Only a person can judge the points below.

## Setup

1. Start a new career with the mod on.
2. Open the station mission lists in the first systems. Look for "Convoy Duty", "Joint Strike" and "Discreet Bounty".
3. If a system has none, jump on. Spawn chances: escort 60 %, assault 40 %, shady 70 % (1 to 2 offers).

## Escort ("Convoy Duty")

- Does the offer text make clear that you must stay with the freighter?
- After you accept, fly to the freighter. Does it start to move when you come near, and stop when you leave it behind?
- Does the jump point marker show on the map after acceptance?
- Do the raiders near the jump point make a real fight? Does the freighter survive often enough, or die too fast?
- Does the freighter fight on the way, or does it only run? Does it behave strangely near suns or stations?
- Is the pay (1.5 × a bounty, plus raider kills) fair for the time it takes? Some freighters are slow: in one autotest a Gannet flew about 14 units/s, so a 6,000 unit trip takes about 7 minutes. If that is too long, lower the jump point distance in `mod/missions/sysgen_hotcargo.rules`.

## Fleet assault ("Joint Strike")

- Do the allied ships fly in and attack the stronghold when you come near? Do they wait too long, or arrive too early and die alone?
- Is it clear which ships are allies? Is it easy to hit them by accident (reputation loss)?
- Is the pay (75 % of a vanilla pirate base at the same tier) fair, given that the allies carry part of the fight?

## Shady offer ("Discreet Bounty")

- Does it look like a normal bounty at first sight? Are the tells (no faction, high pay, odd text) noticeable for a careful player, but not obvious?
- When it is a trap, does the ambush feel fair: enough warning, enough distance (1,300 to 1,800 from your ship), survivable at the sector tier + 1?
- Does the "Survive the ambush" line and its reward appear only after the trap springs?
- At high heat, are traps frequent enough to make the offer a real gamble (80 % at heat 100)?

## Heat

- Do the new missions push heat as much as vanilla missions of the same tier? (Fame from rewards and kills adds heat through Core.)
- Do the Hot Cargo offers crowd out the vanilla offers in the lists?
