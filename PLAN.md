# Plan: co-op test sessions

The full plan and the test map are in the draft PR body. This file holds the steps, so a restarted agent can continue.

1. C#: `as <player> <command>`, `on <player> <command>`, `step`, `end`, `mprestart`, `follow`, join retry.
2. C#: MPHASH state lines (a hash per RunState field, plus vanilla money, ships and parts).
3. Scripts: session runner (`autotest-mp.sh <session>.txt`), `coop-report.sh`, autotest-all, affected, mp-all.
4. Sessions: `coop-core`, `coop-economy`, `coop-combat`. Delete the 21 test pairs.
5. Docs and spec: CONTRIBUTING.md, testing skill, spec/19.
6. Runs: each session passes twice in a row.
