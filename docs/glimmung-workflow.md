# Glimmung workflow registration

This document captures the phase shape spirelens registers with Glimmung. The
live workflow shape is the Postgres-backed `spirelens.default` row in Glimmung;
dispatch does not read a workflow file from this repo. Treat the live
registration as the source of truth — this document is the human-readable map of
it.

## Phase shape

Mirrors `romaine-life/ambience`'s registered workflow, with the per-phase shell
scripts swapped for spirelens's SSH-over-Tailscale variants. spirelens is a
`stats-display` feature type: the world (the game) exists before the feature, so
generated test plans are honest and `llm-test-plan` always runs. That is the
spirelens-vs-ambience difference — ambience is `effect`/greenfield, where the
surface does not exist at plan time, so it when-skips the plan leg and sources a
standing case. The verification phase owns its own verdict and recycle policy;
there is no separate evidence-gate phase.

```
prepare         depends_on: []                outputs: ssh_endpoint, tailnet_ip,
                                                       working_dir, bridge_ready
llm-work        depends_on: prepare           jobs: test-plan + implement
                                              outputs: test_plan, implementation,
                                                       branch_name
llm-verify      depends_on: llm-work          verify: true
                                              outputs: verification
                                              recycle_policy: 1 attempt on
                                              [verify_fail, verify_malformed],
                                              lands_at=prepare
cleanup_early   depends_on: llm-verify        teardown,
                                              when: run.preserve_test_env == 'false'
touchpoint      depends_on: cleanup_early     primitive=pr_touchpoint
touchpoint_gate depends_on: touchpoint        primitive=pr_merge
cleanup_final   depends_on: touchpoint_gate   always
vars.feature_type: stats-display
pr.recycle_policy: 1 attempt on [pr_review_changes_requested], lands_at=prepare
budget.total: 25
```

`llm-verify` carries the recycle policy directly: Glimmung verification phases
own their verdicts, so the standalone evidence-gate phase (retired platform-wide)
is gone. `cleanup_early`'s `when` is the modern replacement for the retired
`skip_when_preserve_test_env` field — with `preserve_test_env=true` the condition
resolves false at dispatch, the early teardown is skipped (zero compute, a
synthesized skipped leg), and the STS2 environment is left up through the
touchpoint review window. `cleanup_final` always tears the environment down.

There is no pre-implementation issue-contract stage. Public names settle by
declaration: the implementation declares its surface and the verify phase checks
that what was declared actually serves. The test-plan and verify phases derive
their context from the issue and the live game directly.

## Step slugs (per phase)

Each phase script branches on `$GLIMMUNG_STEP_SLUG`. The slugs below are what
the registration's `phases[].jobs[].steps[].slug` field must contain — the
phase script's `native_run_selected_step` dispatcher accepts exactly these.

### prepare / env-prep job
- `mint-credentials` — generates ed25519 keypair, mints SSH cert + Tailscale auth key.
- `bring-up-tailnet` — tailscaled in userspace networking mode + `tailscale up`.
- `resolve-host-ip` — looks up the laptop's tailnet IPv4 by `tag:spirelens-host`.
- `probe-ssh` — 30-second SSH reachability deadline.
- `probe-mod-set` — fails closed on any mod outside `{BaseLib, SpireLens, SpireLensMcp}`.
- `install-mcp-start-sts2` — calls `prepare-host.ps1 -InstallMcp -StartSts2`.
- `probe-bridge-ready` — polls `localhost:15526/api/v1/singleplayer` for ≤90s.
- `emit-env-outputs` — writes `ssh_endpoint`, `working_dir`.

### llm-work (two jobs)
**test-plan**: `run-test-plan`, `collect-test-plan`.
**implement**: `run-implementation`, `push-branch`, `collect-implementation`.
`push-branch` commits the laptop checkout after the implementation LLM exits and
pushes the run-scoped `glimmung/<run_id>` branch with the per-run GitHub token.

### llm-verify
- `build-and-deploy` — checks out the implementation branch, `dotnet build` the
  loader and core into the live `mods/` folder.
- `prepare-scenario` — runs `prepare-scenario.ps1`.
- `run-verification` — runs `run-phases.ps1 -PhaseName verification`. The
  unit-test gate is harness-owned and deterministic: before invoking the
  verification agent, the harness runs `dotnet test` on
  `Tests/SpireLens.Core.Tests` with a `trx` logger and reads the **observed**
  exit code + TRX (`Get-ObservedUnitTestResult`) — `passed` is
  `exit_code == 0 && failed == 0`. If unit tests fail, it writes a determined
  `unit_tests_failed` verdict carrying the actual failing test names/counts and
  does NOT spend budget invoking the agent (tests are a hard gate). If they
  pass, it invokes the agent for live-MCP + screenshot evidence only, then
  stamps the observed `unit_tests` block into `verification.json`
  authoritatively (the agent neither runs nor judges unit tests, and its
  self-report cannot override the observed result). The earlier design — where
  the agent ran `dotnet test` and the harness inferred pass/fail by
  regex-scanning the agent's prose — is retired end to end.
- `collect-evidence` — scp `verification.json` + screenshots back to the pod.
- `upload-screenshots` — pushes screenshots to `romaineglimmungartifacts`.
- `emit-verification` — emits the `verification` phase output that the llm-verify
  phase's own recycle policy evaluates (ADVANCE / RETRY / ABORT).

### cleanup_early, touchpoint, touchpoint_gate, cleanup_final
These reuse Glimmung's native primitives directly. See ambience's registered
workflow for the canonical inline shell snippets: `primitive: pr_touchpoint` and
`primitive: pr_merge` resolve to Glimmung-supplied handlers. `cleanup_early`
carries `when: "${{ run.preserve_test_env }} == 'false'"`, so it is skipped (zero
compute, a synthesized skipped leg) when the run preserves its test env.

## Source of truth

The live shape is the `spirelens.default` registration in Glimmung — read it with
the glimmung MCP `list_workflows project=spirelens`, replace it with
`register_workflow`. It is not edited by hand from this repo. When the registered
shape changes, update the phase-shape map above in the same change so this
document keeps describing the final behavior.
