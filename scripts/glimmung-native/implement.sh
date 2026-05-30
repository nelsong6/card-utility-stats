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
\$env:GLIMMUNG_RUN_ID = '${GLIMMUNG_RUN_ID}'
\$env:GLIMMUNG_ATTEMPT_INDEX = '${GLIMMUNG_ATTEMPT_INDEX:-0}'
\$env:GLIMMUNG_PROJECT_REPO = '${GLIMMUNG_PROJECT_REPO:-nelsong6/spirelens}'
\$env:GLIMMUNG_WORKING_DIR = "C:\\glimmung-runs\\${GLIMMUNG_RUN_REF}"
\$env:GLIMMUNG_REPO_ROOT = 'D:\\repos\\SpireLens'
\$env:GH_TOKEN = '${gh_token}'
& 'D:\\repos\\SpireLens\\.github\\scripts\\run-issue-agent-phase.ps1' \`
    -PhaseName implementation \`
    -IssueNumber '${GLIMMUNG_ISSUE_NUMBER}' \`
    -RepoSlug '${GLIMMUNG_PROJECT_REPO:-nelsong6/spirelens}' \`
    -RepoRoot \$env:GLIMMUNG_REPO_ROOT
PWSH
}

push_branch() {
  # The implementation PHASE is sealed from every git mutation — the
  # run-phases.ps1 implementation prompt forbids branches/commits/pushes,
  # and Claude has no push credential. The bash glue, which holds the
  # per-run minted GitHub token, owns the publish: commit the
  # implementation phase's working-tree edits in the laptop checkout to
  # glimmung/<run_id> and push, then verify the branch landed on the
  # remote and surface its name for the verify phase to check out.
  mint_github_token
  local gh_token
  gh_token="$(<"${GLIMMUNG_WORKING_DIR}/gh_token")"
  local repo="${GLIMMUNG_PROJECT_REPO:-nelsong6/spirelens}"
  local branch="glimmung/${GLIMMUNG_RUN_ID}"

  # The branch name is run-id-scoped (a UUID), so force-push is safe — no
  # other run can target it. --allow-empty so a legitimately no-op
  # implementation (no code change required) still publishes a branch the
  # verify phase can build against base. The remote URL carries the token
  # inline; same exposure profile as GH_TOKEN in run-implementation above.
  native_ssh_run "$HOST_IP" <<PWSH
\$ErrorActionPreference = 'Stop'
\$repo = 'D:\\repos\\SpireLens'
git -C \$repo config user.email 'glimmung-issue-agent@romaine.life'
git -C \$repo config user.name 'glimmung issue-agent'
git -C \$repo checkout -B '${branch}'
if (\$LASTEXITCODE -ne 0) { throw 'git checkout -B ${branch} failed' }
git -C \$repo add -A
if (\$LASTEXITCODE -ne 0) { throw 'git add failed' }
git -C \$repo commit --allow-empty -m 'glimmung issue-agent: ${repo}#${GLIMMUNG_ISSUE_NUMBER} (run ${GLIMMUNG_RUN_ID})'
if (\$LASTEXITCODE -ne 0) { throw 'git commit failed' }
git -C \$repo push --force 'https://x-access-token:${gh_token}@github.com/${repo}.git' 'HEAD:refs/heads/${branch}'
if (\$LASTEXITCODE -ne 0) { throw 'git push failed' }
PWSH

  if ! curl -fsS -H "Authorization: token ${gh_token}" \
      "https://api.github.com/repos/${repo}/branches/${branch}" \
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
