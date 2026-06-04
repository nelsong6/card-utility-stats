# Glimmung workflow registration

This document captures the phase shape spirelens registers with Glimmung. The
live workflow shape is the Postgres-backed `spirelens.default` row in Glimmung;
dispatch does not read a workflow file from this repo.

## Phase shape

Mirrors `romaine-life/ambience`'s registered workflow exactly, with the per-phase
shell scripts swapped for spirelens's SSH-over-Tailscale variants. Phase
ordering and recycle policy are identical because spirelens's verify loop
is the same shape: prepare → work → verify → gate → cleanup → touchpoint →
merge → cleanup.

```
prepare         depends_on: []                outputs: ssh_endpoint, tailnet_ip,
                                                       working_dir, bridge_ready
llm-work        depends_on: prepare           jobs: test-plan + implement
                                              outputs: test_plan, implementation,
                                                       branch_name
llm-verify      depends_on: llm-work          verify: true
                                              outputs: verification
evidence-gate   depends_on: llm-verify        recycle_policy: 3 attempts on
                                              [verify_fail, verify_malformed],
                                              lands_at=prepare
cleanup_early   depends_on: evidence-gate     always, skip_when_preserve_test_env
touchpoint      depends_on: cleanup_early     always, primitive=pr_touchpoint
touchpoint_gate depends_on: touchpoint        primitive=pr_merge
cleanup_final   depends_on: touchpoint_gate   always
pr.recycle_policy: 3 attempts on [pr_review_changes_requested], lands_at=prepare
budget.total: 25
```

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
- `run-verification` — runs `run-phases.ps1 -PhaseName verification`.
- `collect-evidence` — scp `verification.json` + screenshots back to the pod.
- `upload-screenshots` — pushes screenshots to `romaineglimmungartifacts`.
- `emit-verification` — emits the `verification` phase output the
  evidence-gate reads.

### evidence-gate, cleanup_early, touchpoint, touchpoint_gate, cleanup_final
These reuse Glimmung's native primitives directly. See ambience's registered
workflow for the canonical inline shell snippets (`primitive: pr_touchpoint`
and `primitive: pr_merge` resolve to Glimmung-supplied handlers; the
evidence-gate's `evidence_verification_gate=true` resolves to the canonical
verdict-checker).

## Historical Registration JSON

The JSON below is retained as historical shape documentation. Do not copy it
into the live API by hand for ordinary changes; update the Glimmung workflow
registration through the admin/control-plane path. The `abridged...`
placeholders elide the evidence-gate / pr_touchpoint / pr_merge / cleanup
snippets.

```jsonc
{
  "project": "spirelens",
  "name": "default",
  "phases": [
    {
      "name": "prepare",
      "kind": "k8s_job",
      "depends_on": [],
      "outputs": ["ssh_endpoint", "tailnet_ip", "working_dir", "bridge_ready"],
      "jobs": [
        {
          "id": "env-prep",
          "name": "Environment prep",
          "checkout": { "ref": "main", "path": "/workspace/spirelens" },
          "working_directory": "/workspace",
          "managed": true,
          "timeout_seconds": 1200,
          "steps": [
            { "slug": "mint-credentials",       "title": "Mint SSH cert + Tailscale auth key", "type": "run", "run": "/bin/bash /workspace/spirelens/scripts/glimmung-native/env-prep.sh" },
            { "slug": "bring-up-tailnet",       "title": "Bring up Tailscale",                  "type": "run", "run": "/bin/bash /workspace/spirelens/scripts/glimmung-native/env-prep.sh" },
            { "slug": "resolve-host-ip",        "title": "Resolve laptop tailnet IP",            "type": "run", "run": "/bin/bash /workspace/spirelens/scripts/glimmung-native/env-prep.sh" },
            { "slug": "probe-ssh",              "title": "Probe SSH reachability",               "type": "run", "run": "/bin/bash /workspace/spirelens/scripts/glimmung-native/env-prep.sh" },
            { "slug": "probe-mod-set",          "title": "Verify allowed mods",                  "type": "run", "run": "/bin/bash /workspace/spirelens/scripts/glimmung-native/env-prep.sh" },
            { "slug": "install-mcp-start-sts2", "title": "Install MCP + start STS2",             "type": "run", "run": "/bin/bash /workspace/spirelens/scripts/glimmung-native/env-prep.sh" },
            { "slug": "probe-bridge-ready",     "title": "Wait for SpireLensMcp bridge",         "type": "run", "run": "/bin/bash /workspace/spirelens/scripts/glimmung-native/env-prep.sh" },
            { "slug": "emit-env-outputs",       "title": "Emit env outputs",                     "type": "run", "run": "/bin/bash /workspace/spirelens/scripts/glimmung-native/env-prep.sh" }
          ]
        }
      ]
    },
    {
      "name": "llm-work",
      "kind": "k8s_job",
      "depends_on": ["prepare"],
      "inputs": {
        "ssh_endpoint": "${{ phases.prepare.outputs.ssh_endpoint }}",
        "tailnet_ip":   "${{ phases.prepare.outputs.tailnet_ip }}",
        "working_dir":  "${{ phases.prepare.outputs.working_dir }}"
      },
      "outputs": ["test_plan", "implementation", "branch_name"],
      "jobs": [
        {
          "id": "llm-test-plan",
          "name": "LLM: author test plan",
          "checkout": { "ref": "main", "path": "/workspace/spirelens" },
          "working_directory": "/workspace",
          "managed": true,
          "timeout_seconds": 900,
          "steps": [
            { "slug": "run-test-plan",     "title": "Run test plan",   "type": "run", "run": "/bin/bash /workspace/spirelens/scripts/glimmung-native/test-plan.sh" },
            { "slug": "collect-test-plan", "title": "Collect artifact", "type": "run", "run": "/bin/bash /workspace/spirelens/scripts/glimmung-native/test-plan.sh" }
          ]
        },
        {
          "id": "llm-implement",
          "name": "LLM: implement",
          "checkout": { "ref": "main", "path": "/workspace/spirelens" },
          "working_directory": "/workspace",
          "managed": true,
          "timeout_seconds": 1800,
          "steps": [
            { "slug": "run-implementation",     "title": "Run implementation",   "type": "run", "run": "/bin/bash /workspace/spirelens/scripts/glimmung-native/implement.sh" },
            { "slug": "push-branch",            "title": "Publish branch",       "type": "run", "run": "/bin/bash /workspace/spirelens/scripts/glimmung-native/implement.sh" },
            { "slug": "collect-implementation", "title": "Collect implementation","type": "run", "run": "/bin/bash /workspace/spirelens/scripts/glimmung-native/implement.sh" }
          ]
        }
      ]
    },
    {
      "name": "llm-verify",
      "kind": "k8s_job",
      "depends_on": ["llm-work"],
      "verify": true,
      "inputs": {
        "ssh_endpoint":   "${{ phases.prepare.outputs.ssh_endpoint }}",
        "tailnet_ip":     "${{ phases.prepare.outputs.tailnet_ip }}",
        "working_dir":    "${{ phases.prepare.outputs.working_dir }}",
        "branch_name":    "${{ phases.llm-work.outputs.branch_name }}",
        "implementation": "${{ phases.llm-work.outputs.implementation }}",
        "test_plan":      "${{ phases.llm-work.outputs.test_plan }}"
      },
      "outputs": ["verification"],
      "jobs": [
        {
          "id": "llm-verify",
          "name": "LLM: verify in STS2",
          "checkout": { "ref": "main", "path": "/workspace/spirelens" },
          "working_directory": "/workspace",
          "managed": true,
          "timeout_seconds": 2400,
          "env": {
            "AGENT_SCREENSHOT_STORAGE_ACCOUNT": "romaineglimmungartifacts",
            "AGENT_SCREENSHOT_CONTAINER": "artifacts"
          },
          "steps": [
            { "slug": "build-and-deploy",   "title": "Build + deploy mod", "type": "run", "run": "/bin/bash /workspace/spirelens/scripts/glimmung-native/verify.sh" },
            { "slug": "prepare-scenario",   "title": "Prepare scenario",   "type": "run", "run": "/bin/bash /workspace/spirelens/scripts/glimmung-native/verify.sh" },
            { "slug": "run-verification",   "title": "Run verification",   "type": "run", "run": "/bin/bash /workspace/spirelens/scripts/glimmung-native/verify.sh" },
            { "slug": "collect-evidence",   "title": "Collect evidence",   "type": "run", "run": "/bin/bash /workspace/spirelens/scripts/glimmung-native/verify.sh" },
            { "slug": "upload-screenshots", "title": "Upload screenshots", "type": "run", "run": "/bin/bash /workspace/spirelens/scripts/glimmung-native/verify.sh" },
            { "slug": "emit-verification",  "title": "Emit verification",  "type": "run", "run": "/bin/bash /workspace/spirelens/scripts/glimmung-native/verify.sh" }
          ]
        }
      ]
    },
    "abridged: evidence-gate (copy from ambience, with input verification mapped to phases.llm-verify.outputs.verification)",
    {
      "name": "cleanup_early",
      "kind": "k8s_job",
      "depends_on": ["evidence-gate"],
      "always": true,
      "skip_when_preserve_test_env": true,
      "jobs": [
        {
          "id": "env-destroy",
          "name": "Cleanup",
          "checkout": { "ref": "main", "path": "/workspace/spirelens" },
          "working_directory": "/workspace",
          "managed": true,
          "timeout_seconds": 600,
          "steps": [
            { "slug": "stop-laptop-processes",     "title": "Stop STS2 + SpireLensMcp",  "type": "run", "run": "/bin/bash /workspace/spirelens/scripts/glimmung-native/env-destroy.sh" },
            { "slug": "remove-laptop-working-dir", "title": "Remove per-run working dir","type": "run", "run": "/bin/bash /workspace/spirelens/scripts/glimmung-native/env-destroy.sh" },
            { "slug": "tailscale-logout",          "title": "Tailscale logout",          "type": "run", "run": "/bin/bash /workspace/spirelens/scripts/glimmung-native/env-destroy.sh" },
            { "slug": "emit",                      "title": "Emit cleanup",              "type": "run", "run": "/bin/bash /workspace/spirelens/scripts/glimmung-native/env-destroy.sh" }
          ]
        }
      ]
    },
    "abridged: touchpoint (primitive=pr_touchpoint), touchpoint_gate (primitive=pr_merge), cleanup_final (always, same env-destroy.sh shape)"
  ],
  "pr": { "recycle_policy": { "max_attempts": 3, "on": ["pr_review_changes_requested"], "lands_at": "prepare" } },
  "budget": { "total": 25 }
}
```
