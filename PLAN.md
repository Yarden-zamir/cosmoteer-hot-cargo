# Gentle current (spec 221)

Playtest: the current cloud is too big, items get stuck in it, and things slide along it forever.

Decisions
- Size: band 18,000 x 4,500 -> 6,000 x 2,400 (length / 3, about 11 instead of 55 nebula triangles). Width 2,400 is the
  smallest width that stays one connected cloud on the 2,000 nebula grid in all 8 directions; 1,500 breaks into patches.
- Fixed numbers, not times the system Scale of PR #30, so the cloud does not shrink twice.
- Push: toward the drift velocity FlowSpeed x (flow + SideShare x outward). Outward = across the flow, toward the nearer
  side of the band (band axis through the centroid of the current triangles). Same lag rule on each part. SideShare tunable.
- Items: loose nuggets (resources, tech crates) in the current: their vanilla drag pulls them toward the drift velocity
  instead of to rest. Junk capsules are ships and get the ship push.
- Pure rule in WorldsModel with unit tests.

Steps
1. Model + unit tests
2. Push for ships and nuggets, band center cache, arrows share it
3. Rules size, tunable, docs (spec 221, WORLDS.md, TUNABLES.md)
4. worlds-current test: ship and crate drift out; Mac runs + co-op
