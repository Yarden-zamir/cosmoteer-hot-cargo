# Fix: Off the Books does nothing

Bug (build 0.4.0-dev.22): at a trade station, "Sell a crate off the books" opens the page, but "Sell one crate" and "change my mind" do nothing.

1. Reproduce in headless tests (single player and co-op), clicking the real comms buttons.
2. Fix the root cause in the hail and choice path. A hail choice that cannot run shows why.
3. Run affected tests and co-op `hail`, `events`, `sharedchoice`, `diplomacy`.
