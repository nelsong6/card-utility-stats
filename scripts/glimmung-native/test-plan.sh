#!/usr/bin/env bash

# test-plan phase for spirelens. Runs the existing
# run-phases.ps1 script on the laptop with
# -PhaseName test_plan; pulls the resulting JSON artifact back to
# the orchestrator pod and emits it as a phase output for the
# verify-loop's later evidence-gate to read.

set -Eeuo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=lib.sh
source "${SCRIPT_DIR}/lib.sh"

native_init
native_require_env GLIMMUNG_RUN_ID GLIMMUNG_RUN_REF GLIMMUNG_ISSUE_NUMBER

# This phase runs in its own ephemeral pod, so env-prep's SSH cert + tailnet
# are not present here. Establish this phase's own connection to the laptop and
# resolve the tagged host IP.
HOST_IP="$(native_connect_host)" || native_emit_abort "host_unavailable"

run_test_plan() {
  native_ssh_run "$HOST_IP" <<PWSH
\$ErrorActionPreference = 'Stop'
\$env:GLIMMUNG_RUN_ID = '${GLIMMUNG_RUN_ID}'
\$env:GLIMMUNG_ATTEMPT_INDEX = '${GLIMMUNG_ATTEMPT_INDEX:-0}'
\$env:GLIMMUNG_PROJECT_REPO = '${GLIMMUNG_PROJECT_REPO:-romaine-life/spirelens}'
\$env:GLIMMUNG_WORKING_DIR = "C:\\glimmung-runs\\${GLIMMUNG_RUN_REF}"
\$env:GLIMMUNG_REPO_ROOT = 'D:\\repos\\SpireLens'
& pwsh -NoProfile -File 'D:\\repos\\SpireLens\\.github\\scripts\\native-runtime.ps1' \`
    -Mode run_phase \`
    -PhaseName test_plan \`
    -IssueNumber '${GLIMMUNG_ISSUE_NUMBER}' \`
    -RepoSlug '${GLIMMUNG_PROJECT_REPO:-romaine-life/spirelens}' \`
    -RepoRoot \$env:GLIMMUNG_REPO_ROOT
\$exitCode = if (\$null -eq \$LASTEXITCODE) { 0 } else { [int]\$LASTEXITCODE }
if (\$exitCode -ne 0) { exit \$exitCode }
PWSH
}

collect_test_plan() {
  # The pwsh script writes the artifact at a known sub-path under
  # the laptop's per-run working dir. We pull it back and emit as
  # `test_plan` for the llm-work phase outputs.
  local local_path="${GLIMMUNG_WORKING_DIR}/test-plan.json"
  native_scp_pull "$HOST_IP" \
    "C:/glimmung-runs/${GLIMMUNG_RUN_REF}/sts2-artifacts/test-plan.json" \
    "$local_path"
  native_emit_json_output test_plan "$local_path"
}

native_run_selected_step \
  run-test-plan      run_test_plan \
  collect-test-plan  collect_test_plan
