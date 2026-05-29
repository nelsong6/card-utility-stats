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
#     primitives, documented in nelsong6/glimmung's
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
  # from. Quoting / multi-line values are the caller's problem.
  local key="$1"
  local value="$2"
  printf '%s=%s\n' "$key" "$value" >>"${GLIMMUNG_OUTPUT_FILE}"
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

# ---------------------------------------------------------------------
# Glimmung run-callback primitives
# ---------------------------------------------------------------------

# Both primitives are documented in
# nelsong6/glimmung/docs/remote-host-execution.md. They're scoped by
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
# spirelens#179 — single-user, no dedicated issue-agent account).
native_ssh_user() {
  printf '%s' "${SPIRELENS_SSH_USER:-nelsonlaptopuser}"
}

# native_ssh_args echoes the ssh option flags every invocation needs.
# Caller composes the final argv as: ssh $(native_ssh_args) user@host ...
native_ssh_args() {
  local id="${GLIMMUNG_WORKING_DIR}/id_ed25519"
  local cert="${GLIMMUNG_WORKING_DIR}/id_ed25519-cert.pub"

  # StrictHostKeyChecking=accept-new is safe here because the only
  # reachable host on this orchestrator's tailnet is the
  # tag:spirelens-host device, and Tailscale's ACL pins that. A
  # man-in-the-middle would have to first compromise the tailnet,
  # which has its own trust model.
  printf -- '-i %s -o IdentityFile=%s -o CertificateFile=%s -o UserKnownHostsFile=/dev/null -o StrictHostKeyChecking=accept-new -o LogLevel=ERROR' \
    "$id" "$id" "$cert"
}

# native_ssh_run <host-ip> <pwsh script body>
# Runs a chunk of pwsh on the laptop via SSH. The script body is fed
# on stdin so we don't have to worry about argv-quoting nightmares.
native_ssh_run() {
  local host_ip="$1"
  shift
  # shellcheck disable=SC2046
  ssh $(native_ssh_args) "$(native_ssh_user)@${host_ip}" pwsh -NoProfile -Command -
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
