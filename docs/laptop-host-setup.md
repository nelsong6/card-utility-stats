# SpireLens laptop host setup

This is the setup guide for the Windows gaming laptop that runs SpireLens's
live verify loop. The laptop hosts the warm Slay the Spire 2 (STS2) install and
the SpireLensMcp bridge, so the load-bearing work (Claude invocation, build,
deploy, scenario prep, verification) executes here even though each run is
orchestrated by `romaine-life/glimmung` from the cluster.

There is **no** GitHub Actions self-hosted runner on this host. The previous
GitHub Actions self-hosted-runner model has been retired end to end. Runs
now reach the laptop over SSH-on-Tailscale, dispatched by Glimmung's
`k8s_job` native phases. See
[docs/glimmung-workflow.md](./glimmung-workflow.md) for the registered phase
shape and `romaine-life/glimmung/docs/remote-host-execution.md` for the
orchestrator-side protocol.

## How a run reaches the laptop

1. Glimmung schedules the workflow's phase pods in the cluster.
2. `scripts/glimmung-native/env-prep.sh` (running in a phase pod) mints a
   per-run SSH user certificate (signed by Glimmung's CA, ~10-minute TTL) and an
   ephemeral, pre-authorized Tailscale auth key, then brings up `tailscaled` in
   userspace-networking mode and joins the tailnet.
3. It resolves this laptop by its Tailscale tag `tag:spirelens-host` and opens
   an SSH session as the local Windows account (`nelsonlaptopuser`).
4. The cluster-side `.sh` scripts invoke this repo's `.github/scripts/*.ps1`
   over that SSH session, against the persistent checkout at `D:\repos\SpireLens`.

The pwsh contract is Glimmung-shaped — `GLIMMUNG_RUN_ID`,
`GLIMMUNG_ATTEMPT_INDEX`, `GLIMMUNG_PROJECT_REPO`, `GLIMMUNG_WORKING_DIR`,
`GLIMMUNG_REPO_ROOT`. There are no GitHub Actions env vars in the live path.

## One-time host prerequisites

- **OpenSSH Server** running and reachable on the tailnet. Configure
  `sshd_config` to trust Glimmung's SSH CA via `TrustedUserCAKeys` (the CA is
  issued by `auth.romaine.life`; see
  `romaine-life/glimmung/docs/remote-host-execution.md` for the current CA public
  key and the exact `sshd_config` stanza). Per-run user certificates are signed
  by that CA, so no static authorized key is stored on the host.
- **Tailscale** installed and signed in, with this device tagged
  `tag:spirelens-host` in the tailnet ACL. The orchestrator's ephemeral node is
  tagged `tag:spirelens-orchestrator`; the ACL pins which tags may dial this
  host.
- **Steam + Slay the Spire 2** installed. The default game-dir candidates the
  scripts probe include `D:\SteamLibrary\steamapps\common\Slay the Spire 2` and
  `D:\Programs\SteamLibrary\steamapps\common\Slay the Spire 2`. If STS2 lives
  elsewhere, set `STS2_GAME_DIR` in `.mcp.json` (read by `prepare-host.ps1`).
- **Claude Code CLI** installed. `prepare-host.ps1` searches the documented
  default locations (e.g. `D:\automation\claude-code\...\claude.exe`); set
  `CONFIGURED_CLAUDE_CLI_PATH` if it lives elsewhere.
- **.NET SDK** (`dotnet`) and **uv** on `PATH`, for building the loader/core and
  running the SpireLensMcp Python helpers.
- **Repo checkouts** under `D:\repos`:
  - `D:\repos\SpireLens` — this repo. The checkout is **auto-synced per run**:
    `env-prep` hard-resets it to the run's commit before any `.ps1` runs (see
    "How the checkout stays current" below). If the checkout is absent, the
    sync creates it from the canonical upstream.
  - `D:\repos\spire-lens-mcp` — the bridge repo. `prepare-host.ps1` clones it on
    first run and `git pull --ff-only origin main` on subsequent runs.

## Mods policy

`env-prep`'s `probe-mod-set` step fails closed if the STS2 `mods/` directory
contains anything outside `{BaseLib, SpireLens, SpireLensMcp}`. Keep the mods
folder limited to those three (`SpireLensMcp` is deployed per-run by
`prepare-host.ps1 -InstallMcp`). A clean run shows `Loaded 3 mods` in the game
log; `Loaded 2 mods` means the bridge is not installed yet.

`BaseLib` and `SpireLens` are **host-local persistent state** — they are not
re-installed per run (only `SpireLensMcp` is), and the per-run checkout sync
deliberately does not `git clean` the game's `mods/` directory. Whatever is
deployed under `mods\BaseLib\` stays until a human changes it.

### BaseLib version floor: **>= v3.1.8**

`BaseLib` must be **v3.1.8 or newer**. The current STS2 build renamed
`Creature.ShowsInfiniteHp` to `Creature.HpDisplay`. BaseLib < 3.1.8
hard-references the old getter, and its combat HP-display patch throws
`MissingMethodException: Creature.get_ShowsInfiniteHp()` on every HP-bar
refresh. On debug-loaded mid-combat saves this freezes the combat HUD at
placeholder values (e.g. `88/88` HP, `0/4` energy), so screenshots no longer
reflect true game state. BaseLib v3.1.8 ("main branch compatibility") added a
graceful `HpDisplay` reflection fallback that resolves the freeze.

`probe-mod-set` **enforces** this floor: after the name check it reads
`mods\BaseLib\BaseLib.json` over SSH and prints the checked state before it
decides: mods path/existence, allowed mods, observed mods, BaseLib manifest
path/existence, raw BaseLib version, normalized BaseLib version, and any JSON
parse error. It fails closed with
`baselib_too_old:found=<ver>:expected>=<floor>` below the floor (or
`baselib_missing_or_unversioned:found=<ver-or-empty>:manifest_exists=<bool>:expected>=<floor>`
if BaseLib is absent or its version is unparseable), and emits
`baselib_version` as a phase output on pass. The floor itself is the
`BASELIB_MIN_VERSION` constant near the top of
`scripts/glimmung-native/env-prep.sh`; the numeric comparison is
`native_semver_ge` in `scripts/glimmung-native/lib.sh`. **Bump
`BASELIB_MIN_VERSION`** when a future STS2 update requires a newer BaseLib. When
re-provisioning a host, fetch the current Alchyr/BaseLib-StS2 release and confirm
`mods\BaseLib\BaseLib.json` reports `"version": "v3.1.8"` or newer.

> Game updates land about every two weeks and routinely break this — see
> [docs/surviving-sts2-updates.md](./surviving-sts2-updates.md) for the full
> failure-mode runbook (build drift, BaseLib desync / frozen HUD, debug-load
> HUD desync) and the post-update checklist.

## Sign-in expectation

Sign-in is **manual** — there is no AutoAdminLogon. STS2 must run in the
logged-in Steam user's desktop session, so the laptop must be powered on and
signed in for a run to proceed. If the host is asleep or pre-logon, `env-prep`
aborts with `host_unavailable` and the run requeues until the next manual
sign-in.

## How the checkout stays current

The run **owns** `D:\repos\SpireLens`. There is no manual pull step. At the
start of `env-prep`'s `install-mcp-start-sts2`, the always-fresh cluster-side
script `scripts/glimmung-native/env-prep.sh` calls `native_sync_host_checkout`
(in `lib.sh`), which over SSH:

```powershell
git -C D:\repos\SpireLens remote set-url origin https://github.com/romaine-life/spirelens.git
git -C D:\repos\SpireLens fetch --prune origin '+refs/heads/*:refs/remotes/origin/*'
git -C D:\repos\SpireLens cat-file -e '<run-commit>^{commit}'
git -C D:\repos\SpireLens checkout --force --detach <run-commit>
git -C D:\repos\SpireLens reset --hard <run-commit>
```

`<run-commit>` is the exact SHA the orchestrator pod resolved `main` to when
the run started, read from the pod's own fresh checkout. Pinning to the SHA —
not a floating `git pull` — keeps the laptop on the same source the
cluster-side `.sh` scripts were cut from, even if `main` advances mid-run.
Because the sync lives in the re-cloned-every-run `.sh` layer and uses
**plain git** (no dependency on any `.ps1` path or name), a rename or move of a
phase script takes effect automatically on the next run — the cutover footgun
is gone.

The canonical upstream is enforced by the sync itself:
`https://github.com/romaine-life/spirelens.git`. The laptop's previous
`origin` value is not trusted. During sync, host-global Git config is ignored so
old URL rewrite rules cannot silently redirect the fetch to a retired repo. If
the pinned commit is not present after fetching from the canonical upstream, the
step fails before any host-side PowerShell script runs.

Two consequences worth knowing:

- **`git clean` is intentionally NOT run.** The sync hard-resets *tracked*
  files but leaves host-local *untracked* files (Steam/STS2 state, logs) in
  place.
- **Tracked files are restored to the run's commit.** `.mcp.json` is tracked,
  so the `reset --hard` overwrites it every run. A host-local edit to a tracked
  file (e.g. setting `STS2_GAME_DIR` directly in `.mcp.json`) will **not**
  survive. Configure host-specific overrides via environment / untracked means,
  or land the change on `main`.
