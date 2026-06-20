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
native_require_env GLIMMUNG_RUN_ID GLIMMUNG_RUN_REF GLIMMUNG_ISSUE_NUMBER
# When the implement job is when-skipped (skip_implement=true) the implementation
# under test is provided via the git_ref checkout and branch_name arrives empty;
# default it to git_ref so build-and-deploy fetches the provided code. Real runs
# always supply branch_name, so this defaulting is a no-op for them.
: "${GLIMMUNG_INPUT_BRANCH_NAME:=${GLIMMUNG_RUN_INPUT_GIT_REF:-}}"
native_require_env GLIMMUNG_INPUT_BRANCH_NAME

# This phase's pod has none of env-prep's connection state; establish our own.
HOST_IP="$(native_connect_host)" || native_emit_abort "host_unavailable"

# The git_ref verification harness (.github/scripts/* + .mcp.json) is staged onto
# the laptop only inside the steps that actually run it (prepare-scenario and
# run-verification), distinct from D:\repos\SpireLens (the feature-branch code
# under test). Staging it there rather than top-level keeps the steps that never
# invoke the grader, build-and-deploy and collect-evidence, from depending on
# it or failing on a stage error.

# native_hydrate_input_artifact <host-ip> <artifact-filename> <input-env-var>
# Materialize a declared phase input into the laptop's per-run artifact dir
# (C:\glimmung-runs\<run-ref>\sts2-artifacts\<artifact-filename>).
#
# In a normal full run, the llm-work phase (test-plan + implement, running on the
# laptop) writes test-plan.json and implementation.json into this dir as an
# on-disk side effect, and the verify steps read them back from there. A
# synthetic verify-only run (start_at_phase=llm-verify, llm-work skipped) never
# produces those files on the laptop — even though the verify phase is handed
# their content as declared inputs (GLIMMUNG_INPUT_TEST_PLAN /
# GLIMMUNG_INPUT_IMPLEMENTATION, exactly the mechanism by which it already
# receives GLIMMUNG_INPUT_BRANCH_NAME). This hydrates the verify steps from those
# declared inputs so they no longer depend on a prior phase's disk residue.
#
# The supplied input IS the authoritative artifact, so writing it is correct
# whether or not llm-work also ran; the on-disk overwrite lands identical content
# on a full run. The content rides as base64 through SSH and is decoded host-side
# (the same way native_github_token_b64 passes the GitHub token) so an arbitrary
# JSON blob with quotes, newlines, or backslashes can never break the pwsh
# here-doc by interpolation.
native_hydrate_input_artifact() {
  local host_ip="$1" filename="$2" var_name="$3"
  native_require_env "$var_name"
  local content_b64
  content_b64="$(printf '%s' "${!var_name}" | base64 | tr -d '\n')"
  local artifact_fwd="C:/glimmung-runs/${GLIMMUNG_RUN_REF}/sts2-artifacts"
  native_ssh_run "$host_ip" <<PWSH
\$ErrorActionPreference = 'Stop'
\$artifactDir = '${artifact_fwd}'
New-Item -ItemType Directory -Force -Path \$artifactDir | Out-Null
\$content = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('${content_b64}'))
\$target = Join-Path \$artifactDir '${filename}'
[System.IO.File]::WriteAllText(\$target, \$content, (New-Object System.Text.UTF8Encoding(\$false)))
PWSH
}

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
  # prepare-scenario.ps1 reads the run's test plan from the artifact dir. Hydrate
  # it from the declared input first so a verify-only run (no llm-work on disk)
  # reaches scenario rigging instead of dying on a missing test-plan.json.
  native_hydrate_input_artifact "$HOST_IP" 'test-plan.json' GLIMMUNG_INPUT_TEST_PLAN
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
  # The verification agent reads test-plan.json and implementation.json (prior
  # llm-work artifacts) off the artifact dir, and the harness reads test-plan.json
  # to bind the unit-test evidence id. Hydrate both from their declared inputs so
  # a verify-only run has the same artifact set a full run leaves on disk. (This
  # step persists the same per-run dir prepare-scenario used, but re-hydrating
  # keeps run-verification self-sufficient if it is retried independently.)
  native_hydrate_input_artifact "$HOST_IP" 'test-plan.json' GLIMMUNG_INPUT_TEST_PLAN
  # implementation.json is absent when the implement job is when-skipped
  # (skip_implement=true); hydrate it only when the input was actually provided.
  if [ -n "${GLIMMUNG_INPUT_IMPLEMENTATION:-}" ]; then
    native_hydrate_input_artifact "$HOST_IP" 'implementation.json' GLIMMUNG_INPUT_IMPLEMENTATION
  fi
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
  local evidence="${artifacts}/evidence"
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

  # Mirror the verifier's non-screenshot evidence into the finalizer's evidence/
  # tree. The verification agent writes each live_mcp get_game_state proof to the
  # artifact dir as live-mcp-<evidence_id>.json (run-phases.ps1). verification_finalize
  # uploads screenshots/, videos/, and evidence/ to durable blob storage and folds
  # the uploaded refs into the typed verification completion; without this, the
  # live_mcp JSON survives only as a host path inside verification.json and is lost
  # when the laptop goes away — it can never be replayed and is silently dropped at
  # review. Stage the files into a host-side dir first (the SSH server's default
  # shell does not reliably glob-expand an scp source pattern), then pull that tree
  # exactly like the screenshots.
  rm -rf "$evidence"
  local evidence_status=0
  if native_ssh_run "$HOST_IP" <<PWSH
\$ErrorActionPreference = 'Stop'
\$artifactDir = 'C:/glimmung-runs/${GLIMMUNG_RUN_REF}/sts2-artifacts'
\$evidenceDir = 'C:/glimmung-runs/${GLIMMUNG_RUN_REF}/sts2-evidence'
if (Test-Path -LiteralPath \$evidenceDir) { Remove-Item -LiteralPath \$evidenceDir -Recurse -Force }
\$files = @(Get-ChildItem -LiteralPath \$artifactDir -Filter 'live-mcp-*.json' -File -ErrorAction SilentlyContinue)
if (\$files.Count -eq 0) { exit 3 }
New-Item -ItemType Directory -Force -Path \$evidenceDir | Out-Null
foreach (\$f in \$files) { Copy-Item -LiteralPath \$f.FullName -Destination \$evidenceDir -Force }
exit 0
PWSH
  then
    # shellcheck disable=SC2046
    scp -r $(native_ssh_args) \
      "$(native_ssh_user)@${HOST_IP}:C:/glimmung-runs/${GLIMMUNG_RUN_REF}/sts2-evidence" \
      "$evidence"
  else
    evidence_status=$?
    if [ "$evidence_status" -ne 3 ]; then
      return "$evidence_status"
    fi
    echo "no live_mcp evidence files produced"
    mkdir -p "$evidence"
  fi
}

native_run_selected_step \
  build-and-deploy   build_and_deploy_mod \
  prepare-scenario   prepare_scenario \
  run-verification   run_verification \
  collect-evidence   collect_evidence
