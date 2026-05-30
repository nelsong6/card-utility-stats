#!/usr/bin/env bash

# verify phase for spirelens. Runs the verification slice on the
# laptop, which:
#
#   - Checks out the implementation branch produced by the implement
#     phase.
#   - Builds + deploys the SpireLens .dll into the live mods/ folder.
#   - Runs prepare-scenario.ps1 to rig the STS2 save the
#     verification scenario expects.
#   - Runs run-phases.ps1 -PhaseName verification, which
#     drives Claude through SpireLensMcp's game-control surface and
#     writes verification.json + a screenshot directory.
#
# The screenshot directory and verification.json are scp'd back to
# the orchestrator pod. Screenshots are then uploaded to the shared
# romaineglimmungartifacts storage account via the pod's federated
# workload identity (no per-project blob storage — that's been
# retired alongside this migration; see the deleted
# opentofu-issue-agent-screenshot-storage workflow).
#
# This is the verify-loop's verdict-emitting phase; the
# evidence-gate phase that follows reads `verification` from this
# phase's outputs and decides ADVANCE / RETRY / ABORT.

set -Eeuo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=lib.sh
source "${SCRIPT_DIR}/lib.sh"

native_init
native_require_env GLIMMUNG_RUN_ID GLIMMUNG_RUN_REF GLIMMUNG_ISSUE_NUMBER GLIMMUNG_INPUT_BRANCH_NAME

# This phase's pod has none of env-prep's connection state; establish our own.
HOST_IP="$(native_connect_host)" || native_emit_abort "host_unavailable"

build_and_deploy_mod() {
  native_ssh_run "$HOST_IP" <<PWSH
\$env:GLIMMUNG_RUN_ID = '${GLIMMUNG_RUN_ID}'
\$env:GLIMMUNG_WORKING_DIR = "C:\\glimmung-runs\\${GLIMMUNG_RUN_REF}"
\$env:GLIMMUNG_REPO_ROOT = 'D:\\repos\\SpireLens'
Set-Location -LiteralPath \$env:GLIMMUNG_REPO_ROOT
git fetch origin '${GLIMMUNG_INPUT_BRANCH_NAME}'
git checkout '${GLIMMUNG_INPUT_BRANCH_NAME}'
# Resolve the laptop's STS2 install and pass it as Sts2Path so the props derive
# BOTH Sts2DataDir (=<gamedir>\data_sts2_windows_x86_64, where sts2.dll lives and
# the csproj HintPaths resolve) AND ModsPath (=<gamedir>\mods\, the deploy
# target). Overriding Sts2DataDir alone would fix the .dll references but leave
# the post-build deploy pointing at the auto-discovered (wrong) mods folder.
. 'D:\\repos\\SpireLens\\.github\\scripts\\Sts2HostPaths.ps1'
\$sts2Path = Resolve-Sts2GameDir -RepoRoot \$env:GLIMMUNG_REPO_ROOT
dotnet build 'SpireLens.csproj' -c Debug "-p:Sts2Path=\$sts2Path"
if (\$LASTEXITCODE -ne 0) { throw 'SpireLens loader build/deploy failed.' }
dotnet build 'Core\\SpireLens.Core.csproj' -c Debug "-p:Sts2Path=\$sts2Path"
if (\$LASTEXITCODE -ne 0) { throw 'SpireLens core build/deploy failed.' }
PWSH
}

prepare_scenario() {
  native_ssh_run "$HOST_IP" <<PWSH
\$env:GLIMMUNG_RUN_ID = '${GLIMMUNG_RUN_ID}'
\$env:GLIMMUNG_WORKING_DIR = "C:\\glimmung-runs\\${GLIMMUNG_RUN_REF}"
\$env:GLIMMUNG_REPO_ROOT = 'D:\\repos\\SpireLens'
# prepare-scenario.ps1 (and the restart-sts2.ps1 it calls) read STS2_GAME_DIR
# out of the MCP config, so resolve the laptop's real game dir and write a
# substituted config first. The committed .mcp.json template ships a placeholder
# game dir; passing it unresolved makes restart-sts2.ps1 throw on a missing path.
. 'D:\\repos\\SpireLens\\.github\\scripts\\Sts2HostPaths.ps1'
\$gameDir = Resolve-Sts2GameDir -RepoRoot \$env:GLIMMUNG_REPO_ROOT
\$mcpConfigPath = Join-Path \$env:GLIMMUNG_WORKING_DIR 'issue-agent-mcp\\scenario.mcp.json'
\$mcpConfigPath = Write-ResolvedMcpConfig -RepoRoot \$env:GLIMMUNG_REPO_ROOT -GameDir \$gameDir -OutPath \$mcpConfigPath
& 'D:\\repos\\SpireLens\\.github\\scripts\\prepare-scenario.ps1' \`
    -TestPlanPath "\$env:GLIMMUNG_WORKING_DIR\\sts2-artifacts\\issue-agent-test-plan.json" \`
    -McpConfigPath \$mcpConfigPath \`
    -RepoRoot \$env:GLIMMUNG_REPO_ROOT \`
    -ValidationArtifactDir "\$env:GLIMMUNG_WORKING_DIR\\sts2-artifacts" \`
    -IssueNumber '${GLIMMUNG_ISSUE_NUMBER}'
PWSH
}

run_verification() {
  native_ssh_run "$HOST_IP" <<PWSH
\$env:GLIMMUNG_RUN_ID = '${GLIMMUNG_RUN_ID}'
\$env:GLIMMUNG_ATTEMPT_INDEX = '${GLIMMUNG_ATTEMPT_INDEX:-0}'
\$env:GLIMMUNG_PROJECT_REPO = '${GLIMMUNG_PROJECT_REPO:-nelsong6/spirelens}'
\$env:GLIMMUNG_WORKING_DIR = "C:\\glimmung-runs\\${GLIMMUNG_RUN_REF}"
\$env:GLIMMUNG_REPO_ROOT = 'D:\\repos\\SpireLens'
& 'D:\\repos\\SpireLens\\.github\\scripts\\run-issue-agent-phase.ps1' \`
    -PhaseName verification \`
    -IssueNumber '${GLIMMUNG_ISSUE_NUMBER}' \`
    -RepoSlug '${GLIMMUNG_PROJECT_REPO:-nelsong6/spirelens}' \`
    -RepoRoot \$env:GLIMMUNG_REPO_ROOT
PWSH
}

collect_evidence() {
  local artifacts="${GLIMMUNG_WORKING_DIR}/artifacts"
  mkdir -p "$artifacts"
  native_scp_pull "$HOST_IP" \
    "C:/glimmung-runs/${GLIMMUNG_RUN_REF}/sts2-artifacts/issue-agent-verification.json" \
    "${artifacts}/issue-agent-verification.json"
  # Mirror the screenshots directory back. -r so the directory
  # tree comes with it.
  # shellcheck disable=SC2046
  scp -r $(native_ssh_args) \
    "$(native_ssh_user)@${HOST_IP}:C:/glimmung-runs/${GLIMMUNG_RUN_REF}/sts2-screenshots" \
    "${artifacts}/" || true
}

upload_screenshots() {
  # Uses the orchestrator pod's federated workload identity to push
  # screenshots into the shared romaineglimmungartifacts blob
  # container under a per-run prefix. ambience's verify.sh uses the
  # same env contract — re-using it here so the operator-side
  # rollout is symmetric.
  : "${AGENT_SCREENSHOT_STORAGE_ACCOUNT:?missing}"
  : "${AGENT_SCREENSHOT_CONTAINER:?missing}"
  local prefix="spirelens/${GLIMMUNG_RUN_REF}/sts2-screenshots"
  local shots="${GLIMMUNG_WORKING_DIR}/artifacts/sts2-screenshots"
  if [ ! -d "$shots" ]; then
    echo "no screenshots to upload"
    return 0
  fi
  az storage blob upload-batch \
    --account-name "$AGENT_SCREENSHOT_STORAGE_ACCOUNT" \
    --destination "$AGENT_SCREENSHOT_CONTAINER" \
    --destination-path "$prefix" \
    --source "$shots" \
    --auth-mode login \
    --overwrite true
}

emit_verification() {
  native_emit_output verification "$(<"${GLIMMUNG_WORKING_DIR}/artifacts/issue-agent-verification.json")"
}

native_run_selected_step \
  build-and-deploy   build_and_deploy_mod \
  prepare-scenario   prepare_scenario \
  run-verification   run_verification \
  collect-evidence   collect_evidence \
  upload-screenshots upload_screenshots \
  emit-verification  emit_verification
