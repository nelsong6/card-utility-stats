#!/usr/bin/env bash

# Shared helpers for spirelens's glimmung-native phase scripts.
#
# Phase scripts source this and branch on $GLIMMUNG_STEP_SLUG via
# native_run_selected_step. The helpers below cover the things every
# spirelens phase needs but glimmung-native doesn't bake into the
# runner image:
#
#   - Per-step dispatch matching ambience's pattern.
#   - Minting per-run credentials (SSH user certificate + Tailscale
#     auth key) via glimmung's /v1/run-callbacks/{token}/native/*
#     primitives, documented in romaine-life/glimmung's
#     docs/remote-host-execution.md. URLs are pre-baked into
#     $GLIMMUNG_SSH_CERT_URL and $GLIMMUNG_TAILSCALE_AUTHKEY_URL by
#     the native launcher; auth rides as the
#     X-Glimmung-Attempt-Token header.
#   - Bringing up tailscaled in userspace networking mode so the
#     orchestrator pod can dial the warm gaming-laptop host.
#   - Wrapping ssh/scp invocations against the laptop with the right
#     identity + cert files + StrictHostKeyChecking posture.
#
# Spirelens does NOT use a glimmung-managed validation namespace,
# helm release, or per-run hostname — the laptop is the only
# execution venue and is preprovisioned. Anything ambience puts
# under native_helm_install / native_clone_repo / etc. is therefore
# absent here on purpose.

set -Eeuo pipefail

# ---------------------------------------------------------------------
# Step dispatch
# ---------------------------------------------------------------------

native_init() {
  : "${GLIMMUNG_OUTPUT_FILE:=/dev/null}"
  : "${GLIMMUNG_WORKING_DIR:=/tmp/glimmung-${GLIMMUNG_RUN_REF:-localdev}}"
  mkdir -p "$GLIMMUNG_WORKING_DIR"
  export GLIMMUNG_WORKING_DIR
}

native_require_env() {
  local missing=()
  local name
  for name in "$@"; do
    if [ -z "${!name:-}" ]; then
      missing+=("$name")
    fi
  done
  if [ "${#missing[@]}" -gt 0 ]; then
    printf 'missing required env: %s\n' "${missing[*]}" >&2
    exit 2
  fi
}

native_issue_repo() {
  # Glimmung derives the primary checkout repo from the project's github_repo
  # and carries that canonical owner/repo slug on the run as issue_repo. The
  # native launcher exposes it as GLIMMUNG_ISSUE_REPO; do not rederive it here.
  local repo="${GLIMMUNG_ISSUE_REPO:-}"
  if [ -z "$repo" ]; then
    echo "native_issue_repo: GLIMMUNG_ISSUE_REPO is required" >&2
    return 1
  fi
  printf '%s' "$repo"
}

native_issue_repo_url() {
  local repo
  repo="$(native_issue_repo)"
  printf 'https://github.com/%s.git' "$repo"
}

native_run_selected_step() {
  local selected="${GLIMMUNG_STEP_SLUG:-}"
  while [ "$#" -gt 0 ]; do
    local slug="$1"
    local fn="$2"
    shift 2
    if [ "$selected" = "$slug" ]; then
      "$fn"
      return $?
    fi
  done
  echo "unknown managed step: ${selected}" >&2
  return 2
}

# ---------------------------------------------------------------------
# Phase-output emission
# ---------------------------------------------------------------------

native_emit_output() {
  # Append a `key=value` line to the file glimmung reads phase outputs
  # from. The value MUST be single-line: the runner parses GLIMMUNG_OUTPUT_FILE
  # line by line, so a multi-line value leaves orphaned continuation lines the
  # parser rejects ("invalid output line"). For JSON artifacts use
  # native_emit_json_output instead.
  local key="$1"
  local value="$2"
  printf '%s=%s\n' "$key" "$value" >>"${GLIMMUNG_OUTPUT_FILE}"
}

native_emit_json_output() {
  # Emit a phase output whose value is the JSON contents of <file>, as a single
  # JSON-object line — the form glimmung's output parser accepts for complex
  # values (the issue-contract phase emits its contract exactly this way).
  #
  # native_emit_output's `key=value` form breaks on pretty-printed JSON: the
  # agent writes multi-line artifacts, and the runner rejects the orphaned
  # `  "field": ...,` continuation lines. We compact to one line and wrap the
  # JSON as an escaped string value, so the emitted line is valid regardless of
  # the artifact's contents. Fails loudly (rather than emitting a broken/empty
  # output) if the artifact is missing or not valid JSON.
  local key="$1"
  local file="$2"
  if [ ! -s "$file" ]; then
    echo "native_emit_json_output: artifact '$file' is missing or empty" >&2
    return 1
  fi
  local compact
  if ! compact="$(jq -c . "$file")"; then
    echo "native_emit_json_output: artifact '$file' is not valid JSON" >&2
    return 1
  fi
  jq -nc --arg k "$key" --arg v "$compact" '{($k): $v}' >>"${GLIMMUNG_OUTPUT_FILE}"
}

native_emit_abort() {
  # Tell glimmung this step short-circuited the phase. The runner will
  # surface `abort_reason` on the Run object so the operator sees why
  # the run never advanced.
  local reason="$1"
  native_emit_output abort_reason "$reason"
  echo "abort: ${reason}" >&2
  exit 0
}

native_mint_github_token() {
  # Glimmung's native launcher bakes a per-attempt callback URL into the pod.
  # Mint once per job pod and cache it under GLIMMUNG_WORKING_DIR so later
  # managed steps in the same k8s Job can reuse the exact token.
  local token_file="${GLIMMUNG_WORKING_DIR}/gh_token"
  if [ -s "$token_file" ]; then
    local cached
    cached="$(tr -d '\r\n' <"$token_file")"
    if [[ "$cached" =~ [^[:space:]] ]]; then
      return 0
    fi
    rm -f "$token_file"
  fi
  native_require_env GLIMMUNG_GITHUB_TOKEN_URL GLIMMUNG_ATTEMPT_TOKEN

  local response token
  if ! response="$(curl -fsS -X POST \
      -H "X-Glimmung-Attempt-Token: ${GLIMMUNG_ATTEMPT_TOKEN}" \
      "${GLIMMUNG_GITHUB_TOKEN_URL}")"; then
    echo "native_mint_github_token: token endpoint request failed" >&2
    return 1
  fi
  if ! token="$(jq -r '.token // empty' <<<"$response" | tr -d '\r\n')"; then
    echo "native_mint_github_token: token endpoint returned invalid JSON" >&2
    return 1
  fi
  if ! [[ "$token" =~ [^[:space:]] ]]; then
    echo "native_mint_github_token: token endpoint returned no usable .token" >&2
    return 1
  fi

  printf '%s\n' "$token" >"$token_file"
  chmod 600 "$token_file"
}

native_github_token() {
  native_mint_github_token
  local token
  token="$(tr -d '\r\n' <"${GLIMMUNG_WORKING_DIR}/gh_token")"
  if ! [[ "$token" =~ [^[:space:]] ]]; then
    echo "native_github_token: cached token is blank" >&2
    return 1
  fi
  printf '%s' "$token"
}

native_github_token_b64() {
  local token
  token="$(native_github_token)"
  printf '%s' "$token" | base64 | tr -d '\n'
}

# ---------------------------------------------------------------------
# Version comparison
# ---------------------------------------------------------------------

# native_semver_ge <a> <b>
# Returns 0 (true) if version a >= version b, else 1. Accepts an
# optional leading 'v' and MAJOR.MINOR.PATCH (missing components are
# treated as 0; a trailing pre-release suffix like -beta is stripped
# per component). Comparison is numeric per component, NOT lexical, so
# v3.1.10 >= v3.1.8 holds. An empty or non-numeric `a` is treated as
# below any `b` (returns 1) — fail-closed for the version-floor gate.
native_semver_ge() {
  local a="${1#v}" b="${2#v}"
  local IFS=.
  local -a av=() bv=()
  read -ra av <<<"$a"
  read -ra bv <<<"$b"
  local i ai bi
  for i in 0 1 2; do
    ai="${av[i]:-0}"; bi="${bv[i]:-0}"
    ai="${ai%%-*}"; bi="${bi%%-*}"
    [[ "$ai" =~ ^[0-9]+$ ]] || return 1
    [[ "$bi" =~ ^[0-9]+$ ]] || return 1
    if ((10#$ai > 10#$bi)); then return 0; fi
    if ((10#$ai < 10#$bi)); then return 1; fi
  done
  return 0
}

# ---------------------------------------------------------------------
# Glimmung run-callback primitives
# ---------------------------------------------------------------------

# Both primitives are documented in
# romaine-life/glimmung/docs/remote-host-execution.md. They're scoped by
# possession of the run callback token (baked into the URL by
# glimmung's native launcher) plus the X-Glimmung-Attempt-Token header.
# Same shape as $GLIMMUNG_GITHUB_TOKEN_URL — pre-baked URLs land on
# the pod as env vars; we never construct one ourselves.

# native_mint_ssh_cert <user-pubkey-file> <cert-out-file>
# Returns: writes the OpenSSH user certificate to cert-out-file.
native_mint_ssh_cert() {
  local pubkey_file="$1"
  local cert_out="$2"
  native_require_env GLIMMUNG_SSH_CERT_URL GLIMMUNG_ATTEMPT_TOKEN

  local pubkey
  pubkey="$(<"$pubkey_file")"
  local body
  body="$(jq -nc --arg pk "$pubkey" '{public_key:$pk}')"

  local response
  response="$(curl -fsS -X POST \
    -H 'Content-Type: application/json' \
    -H "X-Glimmung-Attempt-Token: ${GLIMMUNG_ATTEMPT_TOKEN}" \
    -d "$body" \
    "${GLIMMUNG_SSH_CERT_URL}")"

  # Response: { certificate, principals, key_id, valid_after, valid_before }
  jq -r .certificate <<<"$response" >"$cert_out"
}

# native_mint_tailscale_authkey
# Echoes the auth key on stdout. Caller redirects to a file or
# captures in a variable.
native_mint_tailscale_authkey() {
  native_require_env GLIMMUNG_TAILSCALE_AUTHKEY_URL GLIMMUNG_ATTEMPT_TOKEN

  curl -fsS -X POST \
    -H 'Content-Type: application/json' \
    -H "X-Glimmung-Attempt-Token: ${GLIMMUNG_ATTEMPT_TOKEN}" \
    -d '{}' \
    "${GLIMMUNG_TAILSCALE_AUTHKEY_URL}" | jq -r .authkey
}

# ---------------------------------------------------------------------
# Tailscale bring-up
# ---------------------------------------------------------------------

# Brings up a tailscaled in userspace networking mode under
# GLIMMUNG_WORKING_DIR. Subsequent native_tailscale invocations point
# at the same socket. Idempotent — if tailscaled is already running
# under this working dir, returns immediately.
native_tailscale_up() {
  local authkey="$1"
  native_require_env GLIMMUNG_WORKING_DIR GLIMMUNG_RUN_ID

  local statedir="${GLIMMUNG_WORKING_DIR}/ts"
  local sock="${GLIMMUNG_WORKING_DIR}/ts.sock"
  mkdir -p "$statedir"

  if ! pgrep -f "tailscaled.*${sock}" >/dev/null 2>&1; then
    tailscaled --tun=userspace-networking \
      --statedir="$statedir" \
      --socket="$sock" >"${GLIMMUNG_WORKING_DIR}/tailscaled.log" 2>&1 &
    # tailscaled takes a beat to bind the socket; wait briefly.
    local i
    for i in 1 2 3 4 5 6 7 8 9 10; do
      [ -S "$sock" ] && break
      sleep 0.5
    done
  fi
  # Ephemerality is a property of the minted authkey (the run-callback
  # primitive issues a single-use, pre-authorized, ephemeral key), not a
  # flag on `tailscale up`. The CLI has no --ephemeral flag, so passing it
  # aborts bring-up with "flag provided but not defined: -ephemeral".
  #
  # The hostname must be a valid DNS label. GLIMMUNG_RUN_REF
  # (e.g. "spirelens#177/runs/3") contains '#' and '/', which Tailscale
  # rejects. GLIMMUNG_RUN_ID is a UUID — a valid label, run-unique, and
  # already the basis for the work branch (glimmung/<run_id>).
  tailscale --socket="$sock" up \
    --authkey="$authkey" \
    --hostname="glimmung-${GLIMMUNG_RUN_ID}" \
    --accept-routes=false \
    --accept-dns=false
}

# native_tailscale_host_ip <tag>
# Returns the tailnet IPv4 of the device tagged with <tag>. Fails if
# no device is found or more than one matches.
native_tailscale_host_ip() {
  local tag="$1"
  local sock="${GLIMMUNG_WORKING_DIR}/ts.sock"

  tailscale --socket="$sock" status --json | jq -er --arg tag "$tag" '
    [
      .Peer[]
      | select(.Tags? // [] | index($tag))
      | .TailscaleIPs[]
      | select(test("^100\\."))
    ] as $ips
    | if ($ips | length) == 1 then $ips[0]
      elif ($ips | length) == 0 then error("no tailnet host tagged \($tag)")
      else error("multiple tailnet hosts tagged \($tag): \($ips)")
      end
  '
}

# ---------------------------------------------------------------------
# SSH wrapper
# ---------------------------------------------------------------------

# native_ssh_user returns the Windows local account on the laptop the
# SSH cert authenticates as. Pinned per project decision (Q5 from
# spirelens#179 — single-user, no dedicated automation account).
native_ssh_user() {
  printf '%s' "${SPIRELENS_SSH_USER:-nelsonlaptopuser}"
}

# native_ssh_args echoes the ssh option flags every invocation needs.
# Caller composes the final argv as: ssh $(native_ssh_args) user@host ...
native_ssh_args() {
  local id="${GLIMMUNG_WORKING_DIR}/id_ed25519"
  local cert="${GLIMMUNG_WORKING_DIR}/id_ed25519-cert.pub"
  local sock="${GLIMMUNG_WORKING_DIR}/ts.sock"
  local cfg="${GLIMMUNG_WORKING_DIR}/ssh_config"

  # tailscaled runs in --tun=userspace-networking (the pod has no
  # NET_ADMIN / /dev/net/tun), so there is NO kernel route onto the
  # tailnet: a bare `ssh 100.x.y.z` is not routed and hangs until it
  # times out. Dial through Tailscale's userspace proxy by using
  # `tailscale nc` as an ssh ProxyCommand, pointed at the same socket
  # native_tailscale_up brought up.
  #
  # This is emitted as an ssh_config (referenced with -F) rather than
  # inline -o flags because the ProxyCommand value contains spaces and
  # callers expand `$(native_ssh_args)` unquoted (word-split) — a
  # ProxyCommand flag with embedded spaces cannot survive that.
  #
  # StrictHostKeyChecking=accept-new is safe here because the only
  # reachable host on this orchestrator's tailnet is the
  # tag:spirelens-host device, and Tailscale's ACL pins that. A
  # man-in-the-middle would have to first compromise the tailnet,
  # which has its own trust model.
  cat >"$cfg" <<EOF
Host *
  IdentityFile ${id}
  CertificateFile ${cert}
  UserKnownHostsFile /dev/null
  StrictHostKeyChecking accept-new
  LogLevel ERROR
  ProxyCommand tailscale --socket=${sock} nc %h %p
EOF
  printf -- '-F %s' "$cfg"
}

# native_ps_encode
# Reads a UTF-8 PowerShell script on stdin and writes its PowerShell
# `-EncodedCommand` form on stdout: base64 of the script's UTF-16LE bytes, no
# BOM, on a single line. Verified byte-for-byte against
# `[Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($s))`.
#
# iconv is the normal path. The awk fallback covers runner images that ship
# without iconv (e.g. musl-based); it is ASCII-only, which every phase script
# here is — these bodies are generated shell/pwsh, never localized text.
native_ps_encode() {
  if command -v iconv >/dev/null 2>&1; then
    iconv -f UTF-8 -t UTF-16LE | base64 | tr -d '\n'
  else
    LC_ALL=C awk 'BEGIN{ORS=""}{n=length($0);for(i=1;i<=n;i++){printf "%s%c",substr($0,i,1),0};printf "%c%c",10,0}' | base64 | tr -d '\n'
  fi
}

# native_ssh_run <host-ip> <pwsh script body>
# Runs a chunk of pwsh on the laptop via SSH. The caller pipes the script body
# on this function's stdin (a heredoc); we write it to a temporary .ps1, copy
# that file to the laptop, and execute it with pwsh -File.
#
# We deliberately do NOT use `pwsh -Command -` (read the script from the SSH
# *session* stdin). The per-phase tailnet SSH session that native_connect_host
# stands up in a fresh work/cleanup pod does not reliably forward session stdin
# to the remote process: pwsh then reads an EMPTY command, runs nothing, and
# exits 0 — so the step "succeeds" having done no work (no test-plan.json
# written, no branch pushed; the missing output only surfaces later as the
# collect/scp + push-branch failures). We also do NOT use -EncodedCommand for
# the full script: the checkout bootstrap is large enough that the base64
# UTF-16 argv can exceed Windows/OpenSSH command-line limits.
#
# Set NATIVE_SSH_TIMEOUT=<seconds> to bound the call. The bound is
# applied here, wrapping the real `ssh` binary — `timeout` execs a
# program, so it cannot wrap native_ssh_run itself (a shell function);
# doing so fails with "failed to run command 'native_ssh_run'".
native_ssh_run() {
  local host_ip="$1"
  shift
  local -a timeout_cmd=()
  [ -n "${NATIVE_SSH_TIMEOUT:-}" ] && timeout_cmd=(timeout "$NATIVE_SSH_TIMEOUT")

  local local_script remote_script status
  local_script="$(mktemp "${GLIMMUNG_WORKING_DIR}/native-pwsh.XXXXXX.ps1")"
  cat >"$local_script"
  remote_script="C:/Windows/Temp/glimmung-${GLIMMUNG_RUN_ID:-local}-${GLIMMUNG_JOB_ID:-job}-${GLIMMUNG_STEP_SLUG:-step}-$$-${RANDOM}.ps1"

  # shellcheck disable=SC2046
  if ! "${timeout_cmd[@]}" scp $(native_ssh_args) "$local_script" "$(native_ssh_user)@${host_ip}:${remote_script}"; then
    rm -f "$local_script"
    return 1
  fi
  rm -f "$local_script"

  # -n: the script body is staged as a file, so the SSH session carries no
  # stdin to forward; point it at /dev/null and make that explicit.
  # shellcheck disable=SC2046
  "${timeout_cmd[@]}" ssh -n $(native_ssh_args) "$(native_ssh_user)@${host_ip}" pwsh -NoProfile -ExecutionPolicy Bypass -File "$remote_script"
  status=$?

  # Best-effort cleanup. Preserve the real command status.
  # shellcheck disable=SC2046
  ssh -n $(native_ssh_args) "$(native_ssh_user)@${host_ip}" pwsh -NoProfile -Command "Remove-Item -LiteralPath '${remote_script}' -Force -ErrorAction SilentlyContinue" >/dev/null 2>&1 || true
  return "$status"
}

# native_sync_host_checkout <host-ip>
# Force the laptop's persistent SpireLens checkout (D:\repos\SpireLens) to
# the exact commit this run is executing against, so the run OWNS the working
# directory instead of depending on a human having remembered to
# `git pull` after the last script change landed on main.
#
# The run's commit is read from the orchestrator pod's own checkout — it's a
# fresh `checkout.ref: main` clone per run, so `git rev-parse HEAD` is the SHA
# glimmung resolved main to when the run started. We pin the laptop to that
# exact SHA (not a floating `git pull`) so the laptop runs the same source the
# cluster-side `.sh` scripts were cut from, even if main advanced mid-run.
#
# This is deliberately implemented in the always-fresh cluster-side `.sh`
# layer using plain `git` — it has NO dependency on any `.ps1` path or name,
# so it keeps working even when a phase script on the laptop side has been
# renamed or moved on main. That's the chicken-and-egg the old manual cutover
# couldn't escape.
#
# The canonical upstream is part of this bootstrap contract. The laptop clone's
# existing `origin` is host-local state, so this function repairs it before
# fetching and ignores host-global Git config while doing so. A stale URL rewrite
# or old origin must fail here instead of silently fetching a different repo.
#
# After resetting tracked files, remove stale untracked files from the checkout.
# Preserve local cache/build directories that the laptop may keep warm, but do
# not allow old source/test files from prior runs to bleed into the next branch.
native_sync_host_checkout() {
  local host_ip="$1"
  local cluster_root sha gh_token_b64 remote_url
  # lib.sh lives at <repo>/scripts/glimmung-native/lib.sh; the repo root is
  # two levels up. That's the orchestrator pod's per-run checkout.
  cluster_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
  sha="$(git -C "$cluster_root" rev-parse HEAD 2>/dev/null)"
  gh_token_b64="$(native_github_token_b64)"
  remote_url="$(native_issue_repo_url)"

  native_ssh_run "$host_ip" <<PWSH
\$ErrorActionPreference = 'Stop'
\$repo = 'D:\\repos\\SpireLens'
\$remoteUrl = '${remote_url}'
\$target = '${sha}'
\$token = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('${gh_token_b64}'))
if ([string]::IsNullOrWhiteSpace(\$target)) { throw 'native sync target commit is empty' }
if ([string]::IsNullOrWhiteSpace(\$token)) { throw 'native sync GitHub token is empty' }
\$authHeader = 'AUTHORIZATION: basic ' + [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes("x-access-token:\$token"))

\$emptyGitConfig = Join-Path ([System.IO.Path]::GetTempPath()) 'glimmung-empty-gitconfig'
if (-not (Test-Path -LiteralPath \$emptyGitConfig)) {
  New-Item -ItemType File -Path \$emptyGitConfig -Force | Out-Null
}
\$env:GIT_CONFIG_NOSYSTEM = '1'
\$env:GIT_CONFIG_GLOBAL = \$emptyGitConfig

\$repoParent = Split-Path -Parent \$repo
if (-not (Test-Path -LiteralPath \$repoParent)) {
  New-Item -ItemType Directory -Path \$repoParent -Force | Out-Null
}

\$gitDir = Join-Path \$repo '.git'
if (-not (Test-Path -LiteralPath \$gitDir)) {
  if (Test-Path -LiteralPath \$repo) {
    \$existing = @(Get-ChildItem -LiteralPath \$repo -Force -ErrorAction SilentlyContinue)
    if (\$existing.Count -gt 0) {
      throw "refusing to sync \$repo because it exists but is not a git checkout"
    }
  }
  git -c "http.https://github.com/.extraheader=\$authHeader" clone \$remoteUrl \$repo
  if (\$LASTEXITCODE -ne 0) { throw "git clone \$remoteUrl failed for \$repo" }
}

git -C \$repo remote get-url origin *> \$null
if (\$LASTEXITCODE -ne 0) {
  git -C \$repo remote add origin \$remoteUrl
  if (\$LASTEXITCODE -ne 0) { throw "git remote add origin failed for \$repo" }
} else {
  git -C \$repo remote set-url origin \$remoteUrl
  if (\$LASTEXITCODE -ne 0) { throw "git remote set-url origin failed for \$repo" }
}

\$configuredOrigin = (git -C \$repo config --local --get remote.origin.url)
if (\$LASTEXITCODE -ne 0 -or \$configuredOrigin.Trim() -ne \$remoteUrl) {
  throw "git origin mismatch for \$repo; expected \$remoteUrl, got \$configuredOrigin"
}

\$urlRewriteRules = @(git -C \$repo config --local --get-regexp '^url\\..*\\.insteadOf$' 2>\$null)
if (\$urlRewriteRules.Count -gt 0) {
  throw "refusing to sync \$repo with local git url rewrite rules: \$([string]::Join('; ', \$urlRewriteRules))"
}

git -C \$repo -c "http.https://github.com/.extraheader=\$authHeader" fetch --prune origin '+refs/heads/*:refs/remotes/origin/*'
if (\$LASTEXITCODE -ne 0) { throw "git fetch from \$remoteUrl failed for \$repo" }
git -C \$repo cat-file -e "\$target^{commit}"
if (\$LASTEXITCODE -ne 0) { throw "target commit \$target missing after fetch from \$remoteUrl" }
git -C \$repo checkout --force --detach \$target
if (\$LASTEXITCODE -ne 0) { throw "git checkout \$target failed" }
git -C \$repo reset --hard \$target
if (\$LASTEXITCODE -ne 0) { throw "git reset \$target failed" }
git -C \$repo clean -ffdx -e .godot/ -e bin/ -e obj/ -e .vs/ -e packages/ -e publish/
if (\$LASTEXITCODE -ne 0) { throw "git clean failed for \$repo" }
Write-Output ("synced {0} to {1} from {2}" -f \$repo, \$target, \$remoteUrl)
PWSH
}

# native_connect_host [host-tag]
# Stand up THIS pod's own connection to the remote laptop and echo its tailnet
# IPv4 on stdout. Every phase runs in a separate ephemeral Job pod, so the
# keypair, signed SSH cert, and Tailscale node env-prep created DO NOT exist in
# any later phase's pod (per romaine-life/glimmung docs/remote-host-execution.md:
# the per-run working dir, including Tailscale state, is discarded with the
# pod). Each laptop-touching phase must therefore establish its own connection
# rather than assume env-prep's survives — the lift-and-shift from a single
# GitHub Actions runner (where it DID survive) is what broke this.
#
# Idempotent within a pod across steps: native_run_selected_step re-invokes the
# phase script once per step, so this runs per step. tailscaled is brought up
# only if it is not already running on this pod's socket (env-prep proves the
# backgrounded daemon persists across step invocations within one pod). The
# short-TTL SSH cert (~10 min) is RE-minted every call so a later step never
# inherits an expired cert from an earlier long-running step (e.g. implement's
# multi-minute LLM step preceding the collect step's scp).
#
# Only the resolved IP is written to stdout; all other chatter is routed to
# stderr so callers can do HOST_IP="$(native_connect_host)".
native_connect_host() {
  local tag="${1:-tag:spirelens-host}"
  local sock="${GLIMMUNG_WORKING_DIR}/ts.sock"
  local cert="${GLIMMUNG_WORKING_DIR}/id_ed25519-cert.pub"
  local pubkey_file authkey ip

  pubkey_file="$(native_generate_user_keypair)"
  native_mint_ssh_cert "$pubkey_file" "$cert"

  if ! pgrep -f "tailscaled.*${sock}" >/dev/null 2>&1; then
    authkey="$(native_mint_tailscale_authkey)"
    native_tailscale_up "$authkey" >&2
  fi

  ip="$(native_tailscale_host_ip "$tag")" || return 1
  printf '%s' "$ip" >"${GLIMMUNG_WORKING_DIR}/host_ip"
  printf '%s\n' "$ip"
}

# native_scp_pull <host-ip> <remote-path> <local-path>
native_scp_pull() {
  local host_ip="$1"
  local remote="$2"
  local local_path="$3"
  # shellcheck disable=SC2046
  scp $(native_ssh_args) "$(native_ssh_user)@${host_ip}:${remote}" "$local_path"
}

# ---------------------------------------------------------------------
# Convenience: SA-keypair generation (per-run, never cached)
# ---------------------------------------------------------------------

native_generate_user_keypair() {
  local id="${GLIMMUNG_WORKING_DIR}/id_ed25519"
  if [ ! -f "$id" ]; then
    ssh-keygen -t ed25519 -N "" -f "$id" -C "run=${GLIMMUNG_RUN_REF:-unknown}" >/dev/null
  fi
  printf '%s.pub\n' "$id"
}
