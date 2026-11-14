---
name: hot-cargo-pr-flow
description: Take a Hot Cargo change from its branch to a merged PR and an installed build. Owner merge with auto-merge, required checks, tests outside CI, failures that look unrelated, PR text. Use when you open, update or merge a Hot Cargo PR, and point every agent brief that ends in a PR to it.
---

# From branch to merged PR

The rules are in CLAUDE.md (Git, Spec, Agents) and CONTRIBUTING.md (Git). This skill gives the order.

1. Branch from `origin/main` in your own worktree: `git --git-dir ../.bare worktree add ../<name> -b <type>/<name> origin/main`.
2. In the same change, make the spec lines true that the change touches. For a visible change, take screenshots (`hot-cargo-screenshots`).
3. Commit with conventional commits (`feat:`, `fix:`, `docs:`, `chore:`). Never credit an AI, a model or a tool.
4. Before each push, run `scripts/check-private.sh`.
5. Push and open the PR. Start the PR text with 🤖 and keep it short. Add images of visible changes (`gh-pr-images` skill).
6. Tests outside CI: run `LIST_ONLY=1 RUNNER=pc scripts/autotest-affected.sh`. Run only the listed tests that the merge depends on, on the PC (`hot-cargo-testing`). Tick the box of the PR template.
7. Turn on auto-merge: `gh pr merge --auto --merge`. Watch the PR. Do not poll it.
8. If `main` moves and the PR is out of date:
   - If the new commits on `main` do not touch your area, run `gh pr update-branch`.
   - Otherwise merge `origin/main` into the branch, read what the new commits intend (`git log`, `gh pr view`), adapt, and push.
9. If a required check fails, read it: `gh pr checks`, `gh run view <run id> --log-failed`, and the job summary of the `game-tests` shard. Then follow the next section.
10. After the merge, install the build into the player's game: `scripts/install-player.sh` (`hot-cargo-install`).

## A failure that looks unrelated

1. Check the same test on `main` first: `gh run list --workflow check --branch main --limit 5`, then the job summary of a recent run.
2. If the test passes on `main`, the failure belongs to the PR. Fix it. A PR once merged with 4 real regressions that its owner called unrelated.
3. If the test is flaky (it passes on its retry or on a rerun) or the failure is an infrastructure failure, it counts as passing. Rerun the failed jobs: `gh run rerun <run id> --failed`. Note it in the PR in one line.
4. If the test also fails on `main`, `main` is broken. Do not merge over it. Tell the coordinator.

Infrastructure failures and the log words of a game failure: `hot-cargo-testing`, "Infrastructure failures".
