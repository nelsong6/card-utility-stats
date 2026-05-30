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
native_require_env GLIMMUNG_RUN_ID GLIMMUNG_RUN_REF GLIMMUNG_ISSUE_NUMBER GLIMMUNG_INPUT_TAILNET_IP

# Tailnet IP comes from env-prep's `tailnet_ip` phase output, projected in
# as GLIMMUNG_INPUT_TAILNET_IP. The host_ip file env-prep writes is
# env-prep-pod-local and is gone by the time this fresh pod runs.
HOST_IP="${GLIMMUNG_INPUT_TAILNET_IP}"

mint_github_token() {
  # Glimmung's existing native-runner GitHub token endpoint mints a
  # per-run installation token scoped to the consuming project. URL
  # is pre-baked by the native launcher; auth rides as
  # X-Glimmung-Attempt-Token. Caches the token under the working dir;
  # subsequent steps reuse it.
  local token_file="${GLIMMUNG_WORKING_DIR}/gh_token"
  if [ -s "$token_file" ]; then return 0; fi
  native_require_env GLIMMUNG_GITHUB_TOKEN_URL GLIMMUNG_ATTEMPT_TOKEN
  curl -fsS -X POST \
    -H "X-Glimmung-Attempt-Token: ${GLIMMUNG_ATTEMPT_TOKEN}" \
    "${GLIMMUNG_GITHUB_TOKEN_URL}" | jq -r .token >"$token_file"
  chmod 600 "$token_file"
}

run_implementation() {
  mint_github_token
  local gh_token
  gh_token="$(<"${GLIMMUNG_WORKING_DIR}/gh_token")"
  native_ssh_run "$HOST_IP" <<PWSH
\$env:GLIMMUNG_RUN_ID = '${GLIMMUNG_RUN_ID}'
\$env:GLIMMUNG_ATTEMPT_INDEX = '${GLIMMUNG_ATTEMPT_INDEX:-0}'
\$env:GLIMMUNG_PROJECT_REPO = '${GLIMMUNG_PROJECT_REPO:-nelsong6/spirelens}'
\$env:GLIMMUNG_WORKING_DIR = "C:\\glimmung-runs\\${GLIMMUNG_RUN_REF}"
\$env:GLIMMUNG_REPO_ROOT = 'D:\\repos\\SpireLens'
\$env:GH_TOKEN = '${gh_token}'
& 'D:\\repos\\SpireLens\\.github\\scripts\\run-phases.ps1' \`
    -PhaseName implementation \`
    -IssueNumber '${GLIMMUNG_ISSUE_NUMBER}' \`
    -RepoSlug '${GLIMMUNG_PROJECT_REPO:-nelsong6/spirelens}' \`
    -RepoRoot \$env:GLIMMUNG_REPO_ROOT
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
  local local_path="${GLIMMUNG_WORKING_DIR}/issue-agent-implementation.json"
  native_scp_pull "$HOST_IP" \
    "C:/glimmung-runs/${GLIMMUNG_RUN_REF}/sts2-artifacts/issue-agent-implementation.json" \
    "$local_path"
  native_emit_output implementation "$(<"$local_path")"
}

native_run_selected_step \
  run-implementation    run_implementation \
  push-branch           push_branch \
  collect-implementation collect_implementation
