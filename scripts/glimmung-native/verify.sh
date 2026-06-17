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
# opentofu-screenshot-storage workflow).
#
# This is the verify-loop's verdict-emitting phase: it writes the typed
# `verification` job completion to GLIMMUNG_COMPLETION_FILE (the run
# attempt's source-of-truth Verification, from which glimmung synthesizes
# the declared `verification` phase output). The llm-verify phase's own
# recycle policy (on [verify_fail, verify_malformed], lands_at=prepare)
# decides ADVANCE / RETRY / ABORT. Verification phases own their verdict —
# there is no separate downstream gate phase.

set -Eeuo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=lib.sh
source "${SCRIPT_DIR}/lib.sh"

# The git_ref verification harness (.github/scripts/* + .mcp.json) is staged onto
# the laptop only inside the steps that actually run it (prepare-scenario and
# run-verification), distinct from D:\repos\SpireLens (the feature-branch code
# under test). Staging it there rather than top-level keeps the steps that never
# invoke the grader — build-and-deploy, collect-evidence, upload-screenshots,
# emit-verification — from depending on it or failing on a stage error.

build_and_deploy_mod() {
  local gh_token_b64
  gh_token_b64="$(native_github_token_b64)"
  native_ssh_run "$HOST_IP" <<PWSH
\$ErrorActionPreference = 'Stop'
\$env:GLIMMUNG_RUN_ID = '${GLIMMUNG_RUN_ID}'
\$env:GLIMMUNG_WORKING_DIR = "C:\\glimmung-runs\\${GLIMMUNG_RUN_REF}"
\$env:GLIMMUNG_REPO_ROOT = 'D:\\repos\\SpireLens'
\$token = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('${gh_token_b64}'))
\$authHeader = 'AUTHORIZATION: basic ' + [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes("x-access-token:\$token"))
Set-Location -LiteralPath \$env:GLIMMUNG_REPO_ROOT
git -c "http.https://github.com/.extraheader=\$authHeader" fetch origin '${GLIMMUNG_INPUT_BRANCH_NAME}'
if (\$LASTEXITCODE -ne 0) { throw 'git fetch implementation branch failed' }
git checkout --force FETCH_HEAD
if (\$LASTEXITCODE -ne 0) { throw 'git checkout implementation branch failed' }
# Pass the STS2 install ROOT as Sts2Path. Sts2PathDiscovery.props then derives
# Sts2DataDir = \$(Sts2Path)/data_sts2_windows_x86_64 (where sts2.dll actually
# lives) and ModsPath = \$(Sts2Path)/mods/. Passing the root as Sts2DataDir
# instead points the sts2.dll HintPath at <root>/sts2.dll, which does not exist,
# so the reference fails to resolve (MSB3245 -> CS0246) and the build aborts.
\$sts2Path = 'D:\\SteamLibrary\\steamapps\\common\\Slay the Spire 2'
# Build the Loader (SpireLens.csproj) compile-only with SkipModsDeploy=true. The
# Loader mod is host-persistent (already deployed) and STS2 holds an exclusive
# lock on the loaded SpireLens.dll while running, so the per-run mods/ deploy
# (CopyToModsFolderOnBuild) fails with MSB3027 "file in use". Core is hot-reloaded
# from an unlocked temp copy, so its deploy below lands the run's changes while
# the game keeps running.
dotnet build 'SpireLens.csproj' -c Debug "-p:Sts2Path=\$sts2Path" "-p:SkipModsDeploy=true"
if (\$LASTEXITCODE -ne 0) { throw 'SpireLens loader build failed.' }
dotnet build 'Core\\SpireLens.Core.csproj' -c Debug "-p:Sts2Path=\$sts2Path"
if (\$LASTEXITCODE -ne 0) { throw 'SpireLens core build/deploy failed.' }
PWSH
}

prepare_scenario() {
  local repo_slug HARNESS_ROOT
  repo_slug="$(native_issue_repo)"
  HARNESS_ROOT="$(native_stage_harness "$HOST_IP")" || native_emit_abort "harness_stage_failed"
  native_ssh_run "$HOST_IP" <<PWSH
\$ErrorActionPreference = 'Stop'
\$env:GLIMMUNG_RUN_ID = '${GLIMMUNG_RUN_ID}'
\$env:GLIMMUNG_ATTEMPT_INDEX = '${GLIMMUNG_ATTEMPT_INDEX:-0}'
\$env:GLIMMUNG_PROJECT_REPO = '${repo_slug}'
\$env:GLIMMUNG_WORKING_DIR = "C:\\glimmung-runs\\${GLIMMUNG_RUN_REF}"
\$env:GLIMMUNG_REPO_ROOT = 'D:\\repos\\SpireLens'
\$env:GLIMMUNG_HARNESS_ROOT = '${HARNESS_ROOT}'
& pwsh -NoProfile -File "\$env:GLIMMUNG_HARNESS_ROOT\\.github\\scripts\\native-runtime.ps1" \`
    -Mode prepare_scenario \`
    -IssueNumber '${GLIMMUNG_ISSUE_NUMBER}' \`
    -RepoSlug '${repo_slug}' \`
    -HarnessRoot \$env:GLIMMUNG_HARNESS_ROOT \`
    -RepoRoot \$env:GLIMMUNG_REPO_ROOT
\$exitCode = if (\$null -eq \$LASTEXITCODE) { 0 } else { [int]\$LASTEXITCODE }
if (\$exitCode -ne 0) { exit \$exitCode }
PWSH
}

run_verification() {
  local gh_token_b64 repo_slug HARNESS_ROOT
  gh_token_b64="$(native_github_token_b64)"
  repo_slug="$(native_issue_repo)"
  HARNESS_ROOT="$(native_stage_harness "$HOST_IP")" || native_emit_abort "harness_stage_failed"
  native_ssh_run "$HOST_IP" <<PWSH
\$ErrorActionPreference = 'Stop'
\$ghToken = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('${gh_token_b64}'))
\$env:GLIMMUNG_RUN_ID = '${GLIMMUNG_RUN_ID}'
\$env:GLIMMUNG_ATTEMPT_INDEX = '${GLIMMUNG_ATTEMPT_INDEX:-0}'
\$env:GLIMMUNG_PROJECT_REPO = '${repo_slug}'
\$env:GLIMMUNG_WORKING_DIR = "C:\\glimmung-runs\\${GLIMMUNG_RUN_REF}"
\$env:GLIMMUNG_REPO_ROOT = 'D:\\repos\\SpireLens'
\$env:GLIMMUNG_HARNESS_ROOT = '${HARNESS_ROOT}'
\$env:GH_TOKEN = \$ghToken
& pwsh -NoProfile -File "\$env:GLIMMUNG_HARNESS_ROOT\\.github\\scripts\\native-runtime.ps1" \`
    -Mode run_phase \`
    -PhaseName verification \`
    -IssueNumber '${GLIMMUNG_ISSUE_NUMBER}' \`
    -RepoSlug '${repo_slug}' \`
    -HarnessRoot \$env:GLIMMUNG_HARNESS_ROOT \`
    -RepoRoot \$env:GLIMMUNG_REPO_ROOT \`
    -GitHubToken \$ghToken
\$exitCode = if (\$null -eq \$LASTEXITCODE) { 0 } else { [int]\$LASTEXITCODE }
if (\$exitCode -ne 0) { exit \$exitCode }
PWSH
}

collect_evidence() {
  local artifacts="${GLIMMUNG_WORKING_DIR}/artifacts"
  mkdir -p "$artifacts"
  native_scp_pull "$HOST_IP" \
    "C:/glimmung-runs/${GLIMMUNG_RUN_REF}/sts2-artifacts/verification.json" \
    "${artifacts}/verification.json"
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
  : "${GLIMMUNG_PROJECT:?missing}"
  : "${GLIMMUNG_RUN_ID:?missing}"
  local prefix="runs/${GLIMMUNG_PROJECT}/${GLIMMUNG_RUN_ID}/screenshots"
  local shots="${GLIMMUNG_WORKING_DIR}/artifacts/sts2-screenshots"
  local refs_file="${GLIMMUNG_WORKING_DIR}/artifacts/uploaded-screenshot-refs.json"
  if [ ! -d "$shots" ]; then
    echo "no screenshots to upload"
    printf '[]\n' >"$refs_file"
    return 0
  fi
  # TEMPORARY scaffolding — retired by glimmung's evidence_upload managed
  # primitive (docs/design/evidence-upload-primitive.md). The pod carries a
  # projected workload-identity token, but the az CLI does not consume it the
  # way the SDK credential chain does; --auth-mode login needs an explicit
  # federated login first. Unguarded on purpose: the token is a platform
  # invariant (the runner pod is always labeled azure.workload.identity/use),
  # so a missing AZURE_* here is a broken platform and this fails loudly.
  az login --service-principal \
    --username "$AZURE_CLIENT_ID" \
    --tenant "$AZURE_TENANT_ID" \
    --federated-token "$(cat "$AZURE_FEDERATED_TOKEN_FILE")" \
    --allow-no-subscriptions >/dev/null
  az storage blob upload-batch \
    --account-name "$AGENT_SCREENSHOT_STORAGE_ACCOUNT" \
    --destination "$AGENT_SCREENSHOT_CONTAINER" \
    --destination-path "$prefix" \
    --source "$shots" \
    --auth-mode login \
    --overwrite true

  find "$shots" -type f -print | sort | while IFS= read -r file; do
    rel="${file#"$shots"/}"
    printf '%s/%s\n' "$prefix" "$rel"
  done | jq -R . | jq -s . >"$refs_file"
}

emit_verification() {
  local verification="${GLIMMUNG_WORKING_DIR}/artifacts/verification.json"
  local refs_file="${GLIMMUNG_WORKING_DIR}/artifacts/uploaded-screenshot-refs.json"
  if [ ! -s "$verification" ]; then
    echo "emit_verification: verification artifact '$verification' is missing or empty" >&2
    return 1
  fi
  if ! jq -e . "$verification" >/dev/null 2>&1; then
    echo "emit_verification: verification artifact '$verification' is not valid JSON" >&2
    return 1
  fi
  # Fold the uploaded STS2 screenshots into the verdict as durable evidence:
  # evidence_refs + typed screenshot evidence entries live INSIDE the
  # verification object so they ride the typed completion into the attempt's
  # Verification.EvidenceRefs/Evidence, which review finalize persists as
  # durable artifact evidence.
  if [ -s "$refs_file" ]; then
    local tmp
    tmp="$(mktemp)"
    jq --slurpfile refs "$refs_file" '
      .evidence_refs = (((.evidence_refs // []) + $refs[0]) | unique)
      | .evidence = (((.evidence // []) + ($refs[0] | map({kind:"screenshot", ref:.}))) | unique_by(.kind + "\u0000" + .ref))
    ' "$verification" >"$tmp"
    mv "$tmp" "$verification"
  fi
  # The verdict is the run's source of truth and MUST be a typed job completion,
  # not a `verification` phase output. Glimmung rejects a verify phase that emits
  # only the phase output (verifier_contract_missing) and synthesizes the
  # declared `verification` output from this typed completion — so we write the
  # completion and do not emit the phase output ourselves.
  native_completed "null" "$(cat "$verification")" "" "" "null"
}

# Sourcing this file with SPIRELENS_VERIFY_SOURCE_ONLY=1 defines the step
# functions without connecting to the laptop or dispatching a step, so the
# native contract test can exercise emit_verification in isolation. Under the
# managed runner (normal execution) this guard is a no-op.
if [ "${SPIRELENS_VERIFY_SOURCE_ONLY:-}" != "1" ]; then
  native_init
  native_require_env GLIMMUNG_RUN_ID GLIMMUNG_RUN_REF GLIMMUNG_ISSUE_NUMBER GLIMMUNG_INPUT_BRANCH_NAME

  # This phase's pod has none of env-prep's connection state; establish our own.
  HOST_IP="$(native_connect_host)" || native_emit_abort "host_unavailable"

  native_run_selected_step \
    build-and-deploy   build_and_deploy_mod \
    prepare-scenario   prepare_scenario \
    run-verification   run_verification \
    collect-evidence   collect_evidence \
    upload-screenshots upload_screenshots \
    emit-verification  emit_verification
fi
