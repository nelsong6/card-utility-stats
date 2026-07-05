---
name: dev-work
description: SpireLens development workflow for code changes, stats work, Harmony patches, PR prep, and user-invoked "dev-work" sessions. Use when modifying this repo and needing the expected git, build, deploy, live-reload, validation, commit, push, PR, and merge flow.
---

# Dev Work

## Core Rule

Treat SpireLens work as branch-based, pushed, live-validated development. Do not leave the user paying manual validation tax that the repo tooling can handle.

## Start

1. Read `AGENTS.md`, then read the task-relevant docs named there.
2. Make sure local `main` matches remote `origin/main`.
   - Fetch first.
   - Switch to local `main`.
   - Pull fast-forward only.
   - If main cannot be updated cleanly, stop and report the blocker.
3. Branch from fresh `main`, using the `codex/` prefix unless the user asks otherwise.
   - Do this when starting a new dev-work session, or after the current feature branch has been PR'd/merged and work is continuing.
   - During an ongoing user session, expect the user may provide many related fixes in sequence. Keep stacking follow-up commits on the current live feature branch unless the user explicitly asks for a separate branch.
   - Do not switch back to `main` or create sibling branches mid-session just because the user gives another issue. If the current branch feels unrelated to the new request, say so and ask before splitting.
   - Prefer a worktree for substantial work or anything likely to overlap with existing dirty state.
   - If staying in the current worktree, inspect dirty files and do not disturb unrelated user changes.

## Implement

1. Do the requested work. Read code before changing it.
2. For new attribution or Harmony hooks, read `docs/sts2-runtime-primer.md`.
3. For persisted fields, add schema fixtures and schema-loading assertions.
4. Use focused commits on the feature branch. Do not be shy about commits.
5. Push the feature branch after committing. Do not be shy about pushing feature-branch updates.

## Validate After Each Pushed Iteration

After commit and push, validate the exact pushed code path as far as local tooling allows:

1. Run the relevant unit tests.
   - Default: `dotnet test D:\repos\SpireLens\Tests\SpireLens.Core.Tests\SpireLens.Core.Tests.csproj -c Debug`
   - Add focused filters when useful, but do not replace the broad suite for risky shared changes.
2. Build/deploy the mod.
   - Core-only changes may use the core project/test build if it copies `SpireLens.Core.dll`.
   - Loader or config-page changes need the root build/deploy.
   - If deploy fails because STS2 locks `SpireLens.dll`, report that distinction; core hot-reload may still be deployable.
3. Reload SpireLens through automation, not by asking the user to press F5.
   - Prefer MCP tool `reload_spirelens_core` when available.
   - Equivalent bridge call: `POST http://localhost:15526/api/v1/singleplayer` with `{"action":"dev_reload_spirelens_core"}`.
4. Inspect `%APPDATA%\SlayTheSpire2\logs\godot.log`.
   - Require `Core.Initialize complete`.
   - Treat `Core.Initialize threw` as a failed validation.
   - For Harmony changes, reflection-check target method names and parameter names before relying on reload.
5. For UI/tooltip changes, use SpireLensMcp live tools/screenshots when practical.

If validation fails, fix it, commit, push, and repeat the validation loop. Ask the user for help only when automated tooling is unavailable, the game state genuinely needs human setup, or the decision is product-level.

## PR And Merge

When the user asks to PR and merge:

1. Ensure the branch is committed and pushed.
2. Create the PR.
3. Merge using the repo's normal merge path.
4. Switch to `main` and pull latest `origin/main` after merge.
5. Do not spend user attention on branch deletion; merged feature branches are auto-deleted.

## Communication

Be explicit about which phase you are in: branch setup, implementation, commit/push, validation, iteration, PR/merge. If a required validation step is skipped, say exactly why.
