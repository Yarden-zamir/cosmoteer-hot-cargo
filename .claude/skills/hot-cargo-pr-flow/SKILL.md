---
name: hot-cargo-pr-flow
description: Take a Hot Cargo change from its branch to a merged PR and an installed build. Owner merge with auto-merge, required checks, tests outside CI, failures that look unrelated, PR text. Use when you open, update or merge a Hot Cargo PR, and point every agent brief that ends in a PR to it.
---

# From branch to merged PR

The rules are in CLAUDE.md (Git, Spec, Agents) and CONTRIBUTING.md (Git). This skill gives the order.

1. Branch from `origin/main` in your own worktree: `git --git-dir ../.bare worktree add ../<name> -b <type>/<name> origin/main`.
2. In the same change, make the spec lines true that the change touches. For a visible change, take screenshots (`hot-cargo-screenshots`). Before you pick a new spec section or requirement number, check the open PRs (`gh pr diff <n>`) for numbers in use.
3. Commit with conventional commits (`feat:`, `fix:`, `docs:`, `chore:`). Never credit an AI, a model or a tool.
4. Before each push, run `scripts/check-private.sh` and the game tests that the change needs (`scripts/autotest-sim.sh <names>`, `scripts/autotest-affected.sh`). PR runs run no game tests.
5. Push and open the PR. Start the PR text with 🤖 and keep it short. Add images of visible changes (`gh-pr-images` skill). The merge queue runs the PR's game test set: by default the affected tests and the baseline. Change it in the `### Game tests` block of the PR text (`+ <test>`, `- <test>`, `all`, `none: <reason>`; CONTRIBUTING.md). The PR run's summary shows the set; an unknown test fails it. Usually fewer tests are better.
6. Tests outside CI: run `LIST_ONLY=1 RUNNER=pc scripts/autotest-affected.sh`. Run only the listed tests that the merge depends on, on the PC (`hot-cargo-testing`). Tick the box of the PR template.
7. Turn on auto-merge: `gh pr merge --auto --squash`. When the checks pass, the PR goes into the merge queue of `main`. The queue tests it on top of the newest `main` and the PRs ahead of it, then merges it. Watch the PR. Do not poll it.
8. The PR does not have to be up to date with `main`. When `main` moves, read what the new commits intend (`git log`, `gh pr view`). Only if they conflict with the change or change what it does, merge `origin/main` into the branch, adapt, and push.
9. If a required check fails, read it: `gh pr checks`, `gh run view <run id> --log-failed`, and the job summary of the `game-tests` shard. A failure in the merge queue removes the PR from the queue. Then follow the next section.
10. After the merge, install the build into the player's game: `scripts/install-player.sh` (`hot-cargo-install`).

## A failure that looks unrelated

1. Check the same test on `main` first: the full suite runs there every 3 hours (`gh run list --workflow check --event schedule --limit 5`), and queue runs show other PRs (`--event merge_group`). Read the job summary of a recent run.
2. If the test passes on `main`, the failure belongs to the PR. Fix it. A PR once merged with 4 real regressions that its owner called unrelated.
3. If the test is flaky (it passes on its retry or on a rerun) or the failure is an infrastructure failure, it counts as passing. On the PR's own run, rerun the failed jobs: `gh run rerun <run id> --failed`. In the merge queue, add the PR again: `gh pr merge --auto --squash`. Note it in the PR in one line.
4. If the test also fails on `main`, `main` is broken. Do not merge over it. Tell the coordinator.

Infrastructure failures and the log words of a game failure: `hot-cargo-testing`, "Infrastructure failures".
