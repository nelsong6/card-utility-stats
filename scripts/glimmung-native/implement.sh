#!/usr/bin/env bash

# implement phase for spirelens. Runs run-phases.ps1 with
# -PhaseName implementation on the laptop, then publishes the resulting
# working-tree changes to glimmung/<run_id> using the per-run GitHub token
# glimmung mints via the native-runner github-token callback.

set -Eeuo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=lib.sh
source "${SCRIPT_DIR}/lib.sh"

native_init
native_require_env GLIMMUNG_RUN_ID GLIMMUNG_RUN_REF GLIMMUNG_ISSUE_NUMBER

# This phase's pod has none of env-prep's connection state; establish our own.
HOST_IP="$(native_connect_host)" || native_emit_abort "host_unavailable"

run_implementation() {
  local gh_token_b64
  gh_token_b64="$(native_github_token_b64)"
  native_ssh_run "$HOST_IP" <<PWSH
\$ErrorActionPreference = 'Stop'
\$ghToken = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('${gh_token_b64}'))
\$env:GLIMMUNG_RUN_ID = '${GLIMMUNG_RUN_ID}'
\$env:GLIMMUNG_ATTEMPT_INDEX = '${GLIMMUNG_ATTEMPT_INDEX:-0}'
\$env:GLIMMUNG_PROJECT_REPO = '${GLIMMUNG_PROJECT_REPO:-romaine-life/spirelens}'
\$env:GLIMMUNG_WORKING_DIR = "C:\\glimmung-runs\\${GLIMMUNG_RUN_REF}"
\$env:GLIMMUNG_REPO_ROOT = 'D:\\repos\\SpireLens'
\$env:GH_TOKEN = \$ghToken
& pwsh -NoProfile -File 'D:\\repos\\SpireLens\\.github\\scripts\\native-runtime.ps1' \`
    -Mode run_phase \`
    -PhaseName implementation \`
    -IssueNumber '${GLIMMUNG_ISSUE_NUMBER}' \`
    -RepoSlug '${GLIMMUNG_PROJECT_REPO:-romaine-life/spirelens}' \`
    -RepoRoot \$env:GLIMMUNG_REPO_ROOT
\$exitCode = if (\$null -eq \$LASTEXITCODE) { 0 } else { [int]\$LASTEXITCODE }
if (\$exitCode -ne 0) { exit \$exitCode }
PWSH
}

push_branch() {
  # The implementation LLM is sealed from git mutation. This step owns
  # publishing: commit whatever the implementation phase changed in the
  # laptop checkout, push it to the run-scoped branch, then verify the
  # branch exists and surface its name for verification.
  local gh_token gh_token_b64
  gh_token="$(native_github_token)"
  gh_token_b64="$(printf '%s' "$gh_token" | base64 | tr -d '\n')"
  local repo="${GLIMMUNG_PROJECT_REPO:-romaine-life/spirelens}"
  local branch="glimmung/${GLIMMUNG_RUN_ID}"

  native_ssh_run "$HOST_IP" <<PWSH
\$ErrorActionPreference = 'Stop'
\$ghToken = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('${gh_token_b64}'))
\$repo = 'D:\\repos\\SpireLens'
\$branch = '${branch}'
\$remote = "https://x-access-token:\$ghToken@github.com/${repo}.git"
Set-Location -LiteralPath \$repo
git config user.email 'glimmung-native@romaine.life'
if (\$LASTEXITCODE -ne 0) { throw 'git config user.email failed' }
git config user.name 'Glimmung Native Runner'
if (\$LASTEXITCODE -ne 0) { throw 'git config user.name failed' }
git checkout -B \$branch
if (\$LASTEXITCODE -ne 0) { throw "git checkout -B \$branch failed" }
git add -A
if (\$LASTEXITCODE -ne 0) { throw 'git add failed' }
git commit --allow-empty -m "glimmung: implement ${repo}#${GLIMMUNG_ISSUE_NUMBER} (${GLIMMUNG_RUN_ID})"
if (\$LASTEXITCODE -ne 0) { throw 'git commit failed' }
git push --force \$remote "HEAD:refs/heads/\$branch"
if (\$LASTEXITCODE -ne 0) { throw "git push \$branch failed" }
PWSH

  if ! curl -fsS -H "Authorization: token ${gh_token}" \
      "https://api.github.com/repos/${repo}/branches/${branch}" \
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
