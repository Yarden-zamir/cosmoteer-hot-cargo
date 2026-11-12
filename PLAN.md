# Plan: contract rivals and mission title markup

1. Rivals are one-way. `ContractBook.RivalOf` names only the first other offer of a group. Rules groups (`ore_claims`, `salvage_rights`, `magpies`) can hold 3 or more offers. Fix: name every offer that the accept dismisses.
2. Rivals that the players cannot see. Pairs form across stations (spec 103), but a comms tab shows only its own station list plus the system and galaxy lists. Fix: a rival in another station list says "at <station>".
3. Raw markup in titles. A chain stage name carries a colour tag. The offer details title (`MissionInfoPanel._name`) and the vanilla new, completed and failed mission banners print it without XML formatting. Fix: one helper `Ux/MissionTitle` (XML and plain text), used by these places and by telemetry.
4. Test: `tests/autotest/contracts-rivals.txt`.
