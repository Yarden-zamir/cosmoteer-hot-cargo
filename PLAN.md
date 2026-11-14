# Plan: retreat and repair (spec 53, requirements 1305 to 1313)

Stacked on feat/call-timing (PR #88) because both add to spec/53. Merge origin/main after #88 merges.

Steps:
1. Spec 1305-1313 (done).
2. RepairModel.cs: plan order, lacking amounts, donor share. Unit tests.
3. Resupply.cs: repair cost, donor search and transfer, station supply (queue repairs, add resources, crew).
4. FallBackStrategy: plan state machine (cover, donor, station, away), trip time, save and hash.
5. Autotests tactics-fallback-cover, -donor, -station. Co-op step check.
6. Screenshots of a ship that repairs at a station.
