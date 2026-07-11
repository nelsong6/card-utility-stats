# AGENTS

This repo is a hot-reloadable Slay the Spire 2 mod focused on per-card attribution: not just what a card says it should do, but what it actually caused in the run.

## User-Owned Validation

During interactive Codex development work, the user owns validation. Unless the
user explicitly requests validation in the current task, do **not** run tests,
build or deploy the mod, hot-reload SpireLens, inspect game logs for validation,
manipulate live game/MCP scenarios, or capture validation screenshots. Implement
the requested change, commit/push it when the active workflow calls for that,
and clearly report that validation was not run.

This preference overrides any repo skill or workflow default that would
otherwise automatically validate an implementation. Explicitly assigned
verification tasks (for example, a Glimmung verification phase) still count as
an explicit request and should follow their own validation contract.

## Mod Policy

The Slay the Spire 2 install (`D:\SteamLibrary\steamapps\common\Slay the Spire 2\mods\`) runs **only** the user's own mods plus their required prereqs. No third-party mods.

Allowed entries:

1. The user's own mods — currently **SpireLens**.
2. Required prereqs for (1) — currently **BaseLib** (Alchyr), which SpireLens depends on for Harmony patching and node factories.
3. Tooling prereqs required to validate (1) via the automated verify run — currently **`SpireLensMcp`** (in-house fork at [`romaine-life/spire-lens-mcp`](https://github.com/romaine-life/spire-lens-mcp), source at `D:\repos\spire-lens-mcp\`, vendored from `Gennadiyev/STS2MCP` under MIT). Listens on `localhost:15526` exposing `/api/v1/singleplayer` and `/api/v1/multiplayer`; the Python MCP server in the same repo's `mcp/` directory connects to that endpoint and exposes ~50 game-control tools to Claude. Without it the run's bridge-readiness probe fails before Claude launches.

When inspecting `mods/`, treat any non-(SpireLens|BaseLib|SpireLensMcp) entry as a removal candidate. Don't recommend installing third-party mods even for diagnostics — prefer adding the diagnostic to SpireLens itself. Orphaned appdata under `%APPDATA%\SlayTheSpire2\` from removed third-party mods is also fair game to clean up, after inspecting contents (some folders, e.g. save-game backups, may have user value). Expected `Loaded N mods` line in the game log: **3** when SpireLensMcp is installed (BaseLib + SpireLens + SpireLensMcp), **2** otherwise.

## Current Truths

- Runtime is split into a stable loader and a hot-reloaded core.
  - [Loader/LoaderMain.cs](D:/repos/SpireLens/Loader/LoaderMain.cs:14) owns the long-lived bootstrap and `F5` reload flow.
  - [Core/CoreMain.cs](D:/repos/SpireLens/Core/CoreMain.cs:8) owns Harmony patch install/uninstall and re-entry on each reload.
- Persistence is combat-boundary based.
  - [Core/RunTracker.cs](D:/repos/SpireLens/Core/RunTracker.cs:18) buffers live combat data in `_pendingCombat`.
  - Nothing is promoted to the permanent run file until combat ends — with one deliberate exception: a LOST run's end also promotes, because on a loss `RunManager.OnEnded` fires synchronously from the killing action and the fatal combat's `CombatEnded` only fires afterwards, too late for the buffer. Abandon-mid-combat still discards the buffer.
  - The game re-fires `RunStarted` with the same `_startTime` on every main-menu Continue; `OnRunStarted` resumes/adopts the existing run record (matched by `game_start_time`) instead of minting a new one.
  - Reload between combats / between floors is supported and expected.
  - Mid-combat restore is intentionally out of scope.
- The persisted shape evolves additively without an explicit version number.
  - [Core/RunData.cs](D:/repos/SpireLens/Core/RunData.cs:1) is the source of truth for the current shape.
  - [Core/RunStorage.cs](D:/repos/SpireLens/Core/RunStorage.cs:1) detects the historic pooled shape structurally; everything else is current per-instance.
  - [Tests/SpireLens.Core.Tests/SchemaLoadingTests.cs](D:/repos/SpireLens/Tests/SpireLens.Core.Tests/SchemaLoadingTests.cs:1) and the checked-in fixtures pin known shapes that must remain loadable.
- Card identity is per physical card when the card has stable deck identity.
  - Instance numbers never get reused within a run.
  - Combat-generated cards that do not meaningfully exist in the deck may use pooled summaries instead of fake deck-instance identities.
- Attribution prefers observed outcomes over listed card text whenever the game can diverge from the card face.
  - Examples already in tree: actual energy gained, Regent stars spent/gained, forge granted, observed cards drawn, blocked draw attempts/reasons, successful self-summons to hand, Artifact-blocked debuffs, and downstream poison damage.
- Tooltip style is intentionally quiet.
  - Hand view stays compact.
  - Rows should be self-describing without noisy section headers.
  - Inline keyword icons are preferred when they improve scanability without making the layout louder.
  - When the game already has a recognizable asset for the stat, prefer that in-game icon over a generic label.

## Start Here

- Read [README.md](D:/repos/SpireLens/README.md:1) for the product-level overview.
- Read [docs/architecture.md](D:/repos/SpireLens/docs/architecture.md:1) for subsystem layout and data flow.
- Read [docs/sts2-runtime-primer.md](docs/sts2-runtime-primer.md) before changing card/relic attribution hooks; it captures stable Slay the Spire 2 lifecycle, combat-history, async hook, pile, and attribution timing behavior.
- For tracking behavior, start in [Core/RunTracker.cs](D:/repos/SpireLens/Core/RunTracker.cs:18).
- For tooltip/UI behavior, start in:
  - [Core/Patches/ViewStatsInjectorPatch.cs](D:/repos/SpireLens/Core/Patches/ViewStatsInjectorPatch.cs:11)
  - [Core/Patches/CardHoverTooltipPatch.cs](D:/repos/SpireLens/Core/Patches/CardHoverTooltipPatch.cs:11)

## When Changing Behavior

- If you add persisted fields:
  - keep them additive so old run files still load via missing-field defaults
  - add a fixture file under [Fixtures/RunSchema](D:/repos/SpireLens/Fixtures/RunSchema/README.md:1) capturing the new shape
  - update [SchemaLoadingTests.cs](D:/repos/SpireLens/Tests/SpireLens.Core.Tests/SchemaLoadingTests.cs:1) to assert the new shape loads and any new fields land where expected
- If you change tooltip presentation:
  - preserve the compact-vs-full distinction
  - keep labels self-describing
  - avoid adding loud headers unless they clearly earn their space
- If you add new attribution:
  - read [docs/sts2-runtime-primer.md](docs/sts2-runtime-primer.md) first
  - prefer empirical results over intent text
  - be explicit when attribution is heuristic, pooled, contributor-ledger based, or case-specific

## Run execution model

Runs are dispatched by `romaine-life/glimmung` against this repo's
registered workflow. Each phase runs as a Glimmung-managed `k8s_job` in the
cluster, but the load-bearing work (Claude invocation, build, deploy, scenario
prep, verification) executes on this gaming laptop because the warm Slay the
Spire 2 install and the SpireLensMcp bridge only exist here.

The cluster-side phase pods open a per-run SSH session to this host over an
ephemeral Tailscale tailnet:

- The SSH user certificate is signed by Glimmung's CA per-run (10-minute TTL,
  `KeyId=glimmung-lease:<project>/<lease_id>`).
- The Tailscale auth key is minted per-run via Glimmung's federation flow
  against `auth.romaine.life`; the resulting tailnet node is `ephemeral` and
  dies when the orchestrator pod disconnects.

The harness is a single Go binary, `cmd/glimmung-spirelens`, built on the
`github.com/romaine-life/glimmung/harness` run-harness SDK. It has two faces:
the **pod face** (`glimmung-spirelens pod <slug>`) runs on the cluster-side k8s
job pods — `main()` builds a `harness/step.Registry` and calls `step.Main`,
which dispatches `GLIMMUNG_STEP_SLUG`; and the **host face**
(`glimmung-spirelens <subcmd>`) is the same binary cross-compiled for Windows
and run on this laptop over ssh by the pod (replacing the retired pwsh-over-ssh
here-docs). The pod reaches the laptop through the SDK's `harness/remotehost`
venue (mint ssh cert + tailscale authkey, userspace `tailscaled`, `tailscale nc`
ssh proxy). `git_ref` controls the harness end to end: the pod cross-compiles
the host `.exe` from its own checkout and scps it per run, and the `.mcp.json`
template is embedded in the binary. The retired `scripts/glimmung-native/*.sh`
and `.github/scripts/*.ps1` are gone; a Go reintroduction guard
(`internal/migrationguard`) fails if they return.

There is no GitHub Actions self-hosted runner on this host, and no GitHub
Actions workflow file drives these runs. Sign-in remains manual (no
AutoAdminLogon) — if
the laptop is asleep or pre-logon, the orchestrator's `env-prep` phase aborts
with `host_unavailable` and the run requeues until next manual sign-in.

See `romaine-life/glimmung/docs/remote-host-execution.md` for the orchestrator-side
protocol and `docs/glimmung-workflow.md` here for the registered phase shape.

In the verification phase, the unit-test gate is harness-owned and deterministic
(an observed-outcomes-over-claimed-intent application of the attribution
principle above). The host face's `run-phase` runs `dotnet test` on
`Tests/SpireLens.Core.Tests` with a `trx` logger before the verification agent
starts and reads the observed exit code + TRX via the SDK's
`harness/evidence.ObservedUnitTestResult`; `passed` is
`exit_code == 0 && failed == 0`. A failing observed result aborts with
`unit_tests_failed` (carrying the real failing test names) without invoking the
agent; a passing result is stamped into `verification.json` authoritatively. The
verification agent does live-MCP + screenshot evidence only — it does not run or
judge unit tests, and its narration cannot move the unit-test verdict.

## Useful Commands

- Build/tests:
  - `dotnet test D:\repos\SpireLens\Tests\SpireLens.Core.Tests\SpireLens.Core.Tests.csproj -c Debug`
- Focused schema tests:
  - `dotnet test D:\repos\SpireLens\Tests\SpireLens.Core.Tests\SpireLens.Core.Tests.csproj -c Debug --filter SchemaLoadingTests`
- Focused tooltip tests:
  - `dotnet test D:\repos\SpireLens\Tests\SpireLens.Core.Tests\SpireLens.Core.Tests.csproj -c Debug --filter PoisonTooltipTests`
