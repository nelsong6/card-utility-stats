#!/usr/bin/env bash

# Always-run teardown for spirelens. Idempotent — missing processes
# or directories on the laptop are not failures. The Tailscale node
# is ephemeral so the orchestrator side disappears on its own; we
# only need to tear down the laptop-side per-run state.

set -Eeuo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=lib.sh
source "${SCRIPT_DIR}/lib.sh"

native_init
native_require_env GLIMMUNG_RUN_ID GLIMMUNG_RUN_REF

# Teardown is best-effort (run_on: always). Like the work phases, this cleanup
# pod has none of env-prep's connection state, so re-establish our own to reach
# the laptop. If the host is unreachable, skip laptop-side teardown rather than
# failing the run.
HOST_IP="$(native_connect_host 2>/dev/null)" || HOST_IP=""

stop_laptop_processes() {
  if [ -z "${HOST_IP:-}" ]; then
    echo "host_ip missing; skipping laptop teardown"
    return 0
  fi
  native_ssh_run "$HOST_IP" <<'PWSH' || true
Stop-Process -Name 'SpireLensMcp' -ErrorAction SilentlyContinue
Stop-Process -Name 'sts2' -ErrorAction SilentlyContinue
Stop-Process -Name 'STS2' -ErrorAction SilentlyContinue
PWSH
}

remove_laptop_working_dir() {
  if [ -z "${HOST_IP:-}" ]; then
    return 0
  fi
  native_ssh_run "$HOST_IP" <<PWSH || true
Remove-Item -Recurse -Force -LiteralPath "C:\\glimmung-runs\\${GLIMMUNG_RUN_REF}" -ErrorAction SilentlyContinue
PWSH
}

tailscale_logout() {
  local sock="${GLIMMUNG_WORKING_DIR}/ts.sock"
  if [ -S "$sock" ]; then
    tailscale --socket="$sock" logout || true
  fi
}

emit() {
  native_emit_output cleanup_status "done"
}

native_run_selected_step \
  stop-laptop-processes     stop_laptop_processes \
  remove-laptop-working-dir remove_laptop_working_dir \
  tailscale-logout          tailscale_logout \
  emit                      emit
