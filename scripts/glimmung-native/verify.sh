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
# conventional artifact paths in the orchestrator pod. The Glimmung-owned
# verification_finalize step uploads those artifacts and writes the typed
# `verification` completion payload that the llm-verify recycle policy
# evaluates.

set -Eeuo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=lib.sh
source "${SCRIPT_DIR}/lib.sh"

native_init
native_require_env GLIMMUNG_RUN_ID GLIMMUNG_RUN_REF GLIMMUNG_ISSUE_NUMBER GLIMMUNG_INPUT_BRANCH_NAME

# This phase's pod has none of env-prep's connection state; establish our own.
HOST_IP="$(native_connect_host)" || native_emit_abort "host_unavailable"

# The git_ref verification harness (.github/scripts/* + .mcp.json) is staged onto
# the laptop only inside the steps that actually run it (prepare-scenario and
# run-verification), distinct from D:\repos\SpireLens (the feature-branch code
# under test). Staging it there rather than top-level keeps the steps that never
# invoke the grader, build-and-deploy and collect-evidence, from depending on
# it or failing on a stage error.

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
  local screenshots="${artifacts}/screenshots"
  mkdir -p "$artifacts"
  native_scp_pull "$HOST_IP" \
    "C:/glimmung-runs/${GLIMMUNG_RUN_REF}/sts2-artifacts/verification.json" \
    "${artifacts}/verification.json"

  rm -rf "$screenshots"
  local screenshot_status=0
  if native_ssh_run "$HOST_IP" <<PWSH
if (Test-Path -LiteralPath 'C:/glimmung-runs/${GLIMMUNG_RUN_REF}/sts2-screenshots' -PathType Container) { exit 0 }
exit 3
PWSH
  then
    # Mirror the screenshots directory back under the finalizer's conventional
    # local path. -r so the directory tree comes with it.
    # shellcheck disable=SC2046
    scp -r $(native_ssh_args) \
      "$(native_ssh_user)@${HOST_IP}:C:/glimmung-runs/${GLIMMUNG_RUN_REF}/sts2-screenshots" \
      "$screenshots"
  else
    screenshot_status=$?
    if [ "$screenshot_status" -ne 3 ]; then
      return "$screenshot_status"
    fi
    echo "no screenshot directory produced"
    mkdir -p "$screenshots"
  fi
}

native_run_selected_step \
  build-and-deploy   build_and_deploy_mod \
  prepare-scenario   prepare_scenario \
  run-verification   run_verification \
  collect-evidence   collect_evidence
