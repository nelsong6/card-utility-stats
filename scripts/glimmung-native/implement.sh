#!/usr/bin/env bash

# implement phase for spirelens. Runs run-phases.ps1 with
# -PhaseName implementation on the laptop, then pushes the resulting
# branch under glimmung/<run_id> to nelsong6/spirelens using the
# per-run GitHub token glimmung mints via the existing native-runner
# github-token callback.

set -Eeuo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=lib.sh
source "${SCRIPT_DIR}/lib.sh"

native_init
native_require_env GLIMMUNG_RUN_ID GLIMMUNG_RUN_REF GLIMMUNG_ISSUE_NUMBER

# This phase's pod has none of env-prep's connection state; establish our own.
HOST_IP="$(native_connect_host)" || native_emit_abort "host_unavailable"

mint_github_token() {
  # Glimmung's existing native-runner GitHub token endpoint mints a
  # per-run installation token scoped to the consuming project. URL
  # is pre-baked by the native launcher; auth rides as
  # X-Glimmung-Attempt-Token. Caches the token under the working dir;
  # subsequent steps reuse it.
  local token_file="${GLIMMUNG_WORKING_DIR}/gh_token"
  if [ -s "$token_file" ]; then return 0; fi
  native_require_env GLIMMUNG_GITHUB_TOKEN_URL GLIMMUNG_ATTEMPT_TOKEN
  # Capture + validate before writing: a non-2xx response or a body without a
  # `.token` field must NOT leave a file containing "null"/empty, because the
  # `[ -s "$token_file" ]` cache check above would then treat that garbage as a
  # valid cached token on every subsequent step (GH_TOKEN='null').
  local token
  token="$(curl -fsS -X POST \
    -H "X-Glimmung-Attempt-Token: ${GLIMMUNG_ATTEMPT_TOKEN}" \
    "${GLIMMUNG_GITHUB_TOKEN_URL}" | jq -r '.token // empty')" || true
  if [ -z "$token" ]; then
    echo "mint_github_token: token endpoint returned no usable .token" >&2
    return 1
  fi
  printf '%s' "$token" >"$token_file"
  chmod 600 "$token_file"
}

run_implementation() {
  mint_github_token
  local gh_token
  gh_token="$(<"${GLIMMUNG_WORKING_DIR}/gh_token")"
  native_ssh_run "$HOST_IP" <<PWSH
\$ErrorActionPreference = 'Stop'
\$env:GLIMMUNG_RUN_ID = '${GLIMMUNG_RUN_ID}'
\$env:GLIMMUNG_ATTEMPT_INDEX = '${GLIMMUNG_ATTEMPT_INDEX:-0}'
\$env:GLIMMUNG_PROJECT_REPO = '${GLIMMUNG_PROJECT_REPO:-nelsong6/spirelens}'
\$env:GLIMMUNG_WORKING_DIR = "C:\\glimmung-runs\\${GLIMMUNG_RUN_REF}"
\$env:GLIMMUNG_REPO_ROOT = 'D:\\repos\\SpireLens'
\$env:GH_TOKEN = '${gh_token}'
& pwsh -NoProfile -File 'D:\\repos\\SpireLens\\.github\\scripts\\native-runtime.ps1' \`
    -Mode run_phase \`
    -PhaseName implementation \`
    -IssueNumber '${GLIMMUNG_ISSUE_NUMBER}' \`
    -RepoSlug '${GLIMMUNG_PROJECT_REPO:-nelsong6/spirelens}' \`
    -RepoRoot \$env:GLIMMUNG_REPO_ROOT
\$exitCode = if (\$null -eq \$LASTEXITCODE) { 0 } else { [int]\$LASTEXITCODE }
if (\$exitCode -ne 0) { exit \$exitCode }
PWSH
}

push_branch() {
  # The implementation pwsh script does the actual git push to
  # glimmung/<run_id> from the laptop. Here we just verify the
  # branch exists on the remote and surface its name as a phase
  # output for the verify phase to check out.
  local gh_token
  gh_token="$(<"${GLIMMUNG_WORKING_DIR}/gh_token")"
  local branch="glimmung/${GLIMMUNG_RUN_ID}"
  if ! curl -fsS -H "Authorization: token ${gh_token}" \
      "https://api.github.com/repos/${GLIMMUNG_PROJECT_REPO:-nelsong6/spirelens}/branches/${branch}" \
      >/dev/null; then
    native_emit_abort "implementation_branch_missing:${branch}"
  fi
  native_emit_output branch_name "$branch"
}

collect_implementation() {
  local local_path="${GLIMMUNG_WORKING_DIR}/implementation.json"
  native_scp_pull "$HOST_IP" \
    "C:/glimmung-runs/${GLIMMUNG_RUN_REF}/sts2-artifacts/implementation.json" \
    "$local_path"
  native_emit_json_output implementation "$local_path"
}

native_run_selected_step \
  run-implementation    run_implementation \
  push-branch           push_branch \
  collect-implementation collect_implementation
