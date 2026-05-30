# SpireLens laptop host setup

This is the setup guide for the Windows gaming laptop that runs SpireLens's
live verify loop. The laptop hosts the warm Slay the Spire 2 (STS2) install and
the SpireLensMcp bridge, so the load-bearing work (Claude invocation, build,
deploy, scenario prep, verification) executes here even though each run is
orchestrated by `nelsong6/glimmung` from the cluster.

There is **no** GitHub Actions self-hosted runner on this host. The previous
self-hosted-runner / `issue-agent.yaml` model has been retired end to end. Runs
now reach the laptop over SSH-on-Tailscale, dispatched by Glimmung's
`k8s_job` native phases. See
[docs/glimmung-workflow.md](./glimmung-workflow.md) for the registered phase
shape and `nelsong6/glimmung/docs/remote-host-execution.md` for the
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
  `nelsong6/glimmung/docs/remote-host-execution.md` for the current CA public
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
  `ISSUE_AGENT_CLAUDE_CLI_PATH` if it lives elsewhere.
- **.NET SDK** (`dotnet`) and **uv** on `PATH`, for building the loader/core and
  running the SpireLensMcp Python helpers.
- **Repo checkouts** under `D:\repos`:
  - `D:\repos\SpireLens` — this repo (the persistent run checkout).
  - `D:\repos\spire-lens-mcp` — the bridge repo. `prepare-host.ps1` clones it on
    first run and `git pull --ff-only origin main` on subsequent runs.

## Mods policy

`env-prep`'s `probe-mod-set` step fails closed if the STS2 `mods/` directory
contains anything outside `{BaseLib, SpireLens, SpireLensMcp}`. Keep the mods
folder limited to those three (`SpireLensMcp` is deployed per-run by
`prepare-host.ps1 -InstallMcp`). A clean run shows `Loaded 3 mods` in the game
log; `Loaded 2 mods` means the bridge is not installed yet.

## Sign-in expectation

Sign-in is **manual** — there is no AutoAdminLogon. STS2 must run in the
logged-in Steam user's desktop session, so the laptop must be powered on and
signed in for a run to proceed. If the host is asleep or pre-logon, `env-prep`
aborts with `host_unavailable` and the run requeues until the next manual
sign-in.

## Keeping the checkout current (cutover step)

Because `D:\repos\SpireLens` is a **persistent, manually managed** checkout —
not a fresh clone per run — it does not update itself. After any change to the
`.github/scripts/*.ps1` phase scripts lands on `main`, pull it on the laptop
before the next run:

```powershell
git -C D:\repos\SpireLens checkout main
git -C D:\repos\SpireLens pull --ff-only origin main
```

The cluster-side `.sh` scripts are re-cloned fresh each run, so a rename or edit
to the pwsh scripts only takes effect once both sides are at the same commit.
Skipping the pull leaves the laptop invoking script names/paths that the
cluster side no longer expects.
