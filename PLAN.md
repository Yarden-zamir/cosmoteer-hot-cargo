# Plan: moved Tech Vault never finishes rebuilding

1. Read the vanilla move and construction code. Find why a moved vault stays unfinished.
2. Reproduce in `tests/autotest/tech-move.txt`: move a vault (empty and with tech) through the vanilla move flow, let crew rebuild it, check full construction and that it holds tech.
3. Fix the root cause with the smallest change. Keep co-op determinism.
4. Patch spec 39 or 07 in place if the behavior changes.
5. Run `scripts/autotest-affected.sh` and co-op tests `timer` and `trade`.
