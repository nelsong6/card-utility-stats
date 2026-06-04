#!/usr/bin/env bash

# env-prep phase for spirelens's glimmung-native workflow.
#
# Spirelens's verify loop runs on a Windows gaming laptop that hosts
# the warm Slay the Spire 2 install. This phase stands up the
# orchestrator pod's connection to that laptop:
#
#   1. Generate a per-run ed25519 keypair on the pod.
#   2. Mint a short-TTL SSH user certificate via glimmung.
#   3. Mint an ephemeral, pre-authorized Tailscale auth key via
#      glimmung's OIDC-federation path.
#   4. Bring tailscaled up in userspace networking mode and join the
#      tailnet as an ephemeral node.
#   5. Resolve the laptop's tailnet IP from its `tag:spirelens-host`
#      device record.
#   6. Probe the SSH connection — emit `host_unavailable` if the
#      laptop is asleep / pre-logon.
#   7. Check the mods/ directory contents against the spirelens
#      AGENTS.md mod policy (BaseLib + SpireLens + SpireLensMcp only)
#      — emit `unexpected_mod:<name>` and fail-closed on anything
#      else (spirelens#179 Q3) — and enforce the BaseLib >= v3.1.8
#      version floor (emit found/expected detail on failure).
#   8. Run the existing prepare-host pwsh script on the
#      laptop with -InstallMcp -StartSts2, so SpireLensMcp is
#      installed and STS2 is launched with the bridge accessible.
#   9. Poll the bridge on localhost:15526 until it returns 2xx, with
#      a bounded deadline that produces `bridge_not_ready` on miss.
#
# All later phases (test-plan, implement, verify) assume this phase
# produced the credentials + warm tailnet connection + bridge-ready
# laptop. Phase outputs are emitted as key=value to GLIMMUNG_OUTPUT_FILE
# so glimmung's decision engine can project them into the next phase's
# inputs.

set -Eeuo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=lib.sh
source "${SCRIPT_DIR}/lib.sh"

native_init
native_require_env GLIMMUNG_RUN_ID GLIMMUNG_RUN_REF

# Minimum BaseLib mod version. BaseLib < 3.1.8 hard-references
# Creature.ShowsInfiniteHp, which the current STS2 build renamed to
# Creature.HpDisplay; its combat HP-display patch then throws
# MissingMethodException on every HP-bar refresh, freezing the combat HUD
# at placeholder values on debug-loaded saves (spire-lens-mcp#10). v3.1.8
# added the graceful HpDisplay reflection fallback. See
# docs/laptop-host-setup.md "BaseLib version floor".
BASELIB_MIN_VERSION="3.1.8"

mint_credentials() {
  # Generates a per-run ed25519 keypair on the pod, asks glimmung to
  # sign the public key against the SSH CA, and asks glimmung for a
  # one-shot Tailscale auth key tagged tag:spirelens-orchestrator.
  # Stored under GLIMMUNG_WORKING_DIR. Nothing here is logged at info
  # level — the cert and auth key are credentials.
  local pubkey_file
  pubkey_file="$(native_generate_user_keypair)"
  native_mint_ssh_cert "$pubkey_file" "${GLIMMUNG_WORKING_DIR}/id_ed25519-cert.pub"
  native_mint_tailscale_authkey >"${GLIMMUNG_WORKING_DIR}/tailscale-authkey" 2>/dev/null
  chmod 600 "${GLIMMUNG_WORKING_DIR}/tailscale-authkey" "${GLIMMUNG_WORKING_DIR}/id_ed25519"
}

bring_up_tailnet() {
  local authkey
  authkey="$(<"${GLIMMUNG_WORKING_DIR}/tailscale-authkey")"
  native_tailscale_up "$authkey"
}

resolve_host_ip() {
  # Pin to the tag declared in the spirelens tenant's tailnet ACL
  # (spirelens#179 Q5). The Tailscale ACL restricts what the
  # orchestrator tag can dial; this resolve is just for picking the
  # tailnet IPv4 to feed into ssh.
  local ip
  if ! ip="$(native_tailscale_host_ip tag:spirelens-host)"; then
    native_emit_abort "host_unavailable"
  fi
  native_emit_output tailnet_ip "$ip"
  printf '%s' "$ip" >"${GLIMMUNG_WORKING_DIR}/host_ip"
}

probe_ssh() {
  # 30s deadline. If the laptop is asleep, pre-logon, or unreachable
  # for any reason, the phase aborts with a verdict the operator can
  # action on (the laptop is plugged in / signed in per Q1+Q4, but
  # the cert + tailnet handshake can still fail for environmental
  # reasons we want surfaced cleanly).
  local ip
  ip="$(<"${GLIMMUNG_WORKING_DIR}/host_ip")"
  if ! NATIVE_SSH_TIMEOUT=30 native_ssh_run "$ip" <<'PROBE'
$PSVersionTable.PSVersion | Out-Null
Write-Output 'ok'
PROBE
  then
    native_emit_abort "host_unavailable"
  fi
}

probe_mod_set() {
  # Spirelens AGENTS.md: only BaseLib, SpireLens, SpireLensMcp.
  # SpireLensMcp is per-run; it may legitimately be absent before
  # install_mcp runs. We tolerate that and reject anything ELSE.
  local ip
  ip="$(<"${GLIMMUNG_WORKING_DIR}/host_ip")"
  local mod_state
  if ! mod_state="$(native_ssh_run "$ip" <<'PROBE'
$modsPath = 'D:\SteamLibrary\steamapps\common\Slay the Spire 2\mods'
$manifestPath = Join-Path $modsPath 'BaseLib\BaseLib.json'
$allowed = @('BaseLib', 'SpireLens', 'SpireLensMcp')
$modNames = @()
if (Test-Path -LiteralPath $modsPath) {
  $names = [System.Collections.Generic.List[string]]::new()
  foreach ($dir in [System.IO.Directory]::GetDirectories($modsPath)) {
    $names.Add([System.IO.Path]::GetFileName($dir))
  }
  $modNames = [string[]]$names.ToArray()
  [Array]::Sort($modNames, [System.StringComparer]::OrdinalIgnoreCase)
}
$manifestExists = Test-Path -LiteralPath $manifestPath
$baselibVersion = ''
$parseError = ''
if ($manifestExists) {
  try {
    $manifestRaw = [System.IO.File]::ReadAllText($manifestPath)
    $manifest = ConvertFrom-Json -InputObject $manifestRaw
    if ($null -ne $manifest.version) {
      $baselibVersion = [string]$manifest.version
    }
  } catch {
    $parseError = ($_.Exception.Message -replace '[\r\n\t]+', ' ')
  }
}

Write-Output "mods_dir=$modsPath"
Write-Output ("mods_dir_exists=" + (Test-Path -LiteralPath $modsPath).ToString().ToLowerInvariant())
Write-Output ("allowed_mods=" + ($allowed -join ','))
Write-Output ("mods_found=" + ($modNames -join ','))
Write-Output "baselib_manifest=$manifestPath"
Write-Output ("baselib_manifest_exists=" + $manifestExists.ToString().ToLowerInvariant())
Write-Output "baselib_version_raw=$baselibVersion"
Write-Output ("baselib_version_normalized=" + $baselibVersion.Trim())
if ($parseError -ne '') {
  Write-Output "baselib_parse_error=$parseError"
}
PROBE
  )"; then
    native_emit_abort "host_unavailable"
  fi
  printf '%s\n' "$mod_state"

  local mods
  mods="$(printf '%s\n' "$mod_state" | sed -n 's/^mods_found=//p' | tail -n 1 | tr -d '\r' | tr ',' '\n')"
  local allowed=("BaseLib" "SpireLens" "SpireLensMcp")
  local unexpected=()
  local m a found
  while IFS= read -r m; do
    [ -n "$m" ] || continue
    found=0
    for a in "${allowed[@]}"; do
      if [ "$m" = "$a" ]; then found=1; break; fi
    done
    [ "$found" = 1 ] || unexpected+=("$m")
  done <<<"$mods"
  if [ "${#unexpected[@]}" -gt 0 ]; then
    native_emit_abort "unexpected_mod:${unexpected[*]}"
  fi

  # BaseLib version floor. The name check above is necessary but not
  # sufficient: a stale BaseLib (< 3.1.8, pre-HpDisplay rename) keeps
  # the correct *name* and so passes silently, but freezes the combat
  # HUD at placeholder values on debug-loaded saves, corrupting the
  # screenshot evidence the verify loop depends on. Read the deployed
  # version and fail closed below the floor. BaseLib/SpireLens are
  # host-local persistent state (not installed per run), so this gate is
  # the enforcement point — see docs/laptop-host-setup.md.
  local baselib_raw baselib_ver manifest_exists
  baselib_raw="$(printf '%s\n' "$mod_state" | sed -n 's/^baselib_version_raw=//p' | tail -n 1)"
  baselib_ver="$(printf '%s' "$baselib_raw" | tr -d '\r\n ')"
  manifest_exists="$(printf '%s\n' "$mod_state" | sed -n 's/^baselib_manifest_exists=//p' | tail -n 1)"
  if [ -z "$baselib_ver" ]; then
    native_emit_abort "baselib_missing_or_unversioned:found=<empty>:manifest_exists=${manifest_exists:-unknown}:expected>=${BASELIB_MIN_VERSION}"
  fi
  if ! native_semver_ge "$baselib_ver" "$BASELIB_MIN_VERSION"; then
    native_emit_abort "baselib_too_old:found=${baselib_ver}:expected>=${BASELIB_MIN_VERSION}"
  fi
  native_emit_output baselib_version "$baselib_ver"
}

install_mcp_and_start_sts2() {
  # Calls the existing pwsh prep script over SSH. The prep script
  # already knows where Claude CLI / SpireLensMcp / STS2 live; we
  # just hand it the new GLIMMUNG_* env contract and the
  # -InstallMcp -StartSts2 switches that were on the GHA workflow's
  # env-prep step.
  local ip repo_slug
  ip="$(<"${GLIMMUNG_WORKING_DIR}/host_ip")"
  repo_slug="$(native_issue_repo)"

  # Own the working directory: force the laptop's persistent checkout to this
  # run's commit BEFORE invoking any .ps1, so we never run stale phase scripts.
  # This replaces the old manual `git pull` cutover step that humans had to
  # remember after every .ps1 change landed on main.
  native_sync_host_checkout "$ip"

  native_ssh_run "$ip" <<PWSH
\$ErrorActionPreference = 'Stop'
\$env:GLIMMUNG_RUN_ID = '${GLIMMUNG_RUN_ID}'
\$env:GLIMMUNG_ATTEMPT_INDEX = '${GLIMMUNG_ATTEMPT_INDEX:-0}'
\$env:GLIMMUNG_PROJECT_REPO = '${repo_slug}'
\$env:GLIMMUNG_WORKING_DIR = "C:\\glimmung-runs\\${GLIMMUNG_RUN_REF}"
\$env:GLIMMUNG_REPO_ROOT = 'D:\\repos\\SpireLens'
& pwsh -NoProfile -File 'D:\\repos\\SpireLens\\.github\\scripts\\prepare-host.ps1' \`
    -CheckoutPath \$env:GLIMMUNG_REPO_ROOT \`
    -InstallMcp \`
    -StartSts2
\$exitCode = if (\$null -eq \$LASTEXITCODE) { 0 } else { [int]\$LASTEXITCODE }
if (\$exitCode -ne 0) { exit \$exitCode }
PWSH
}

probe_bridge_ready() {
  # SpireLensMcp is in-process with STS2; once STS2 has loaded the
  # mod and the bridge is bound to localhost:15526, /singleplayer
  # returns 2xx. We poll for up to 90s; longer than that and
  # something's wedged.
  local ip
  ip="$(<"${GLIMMUNG_WORKING_DIR}/host_ip")"
  if ! native_ssh_run "$ip" <<'PROBE'
$deadline = (Get-Date).AddSeconds(90)
while ((Get-Date) -lt $deadline) {
  try {
    $r = Invoke-WebRequest -UseBasicParsing -Uri 'http://localhost:15526/api/v1/singleplayer' -TimeoutSec 3
    if ($r.StatusCode -ge 200 -and $r.StatusCode -lt 400) { exit 0 }
  } catch {}
  Start-Sleep -Seconds 3
}
exit 1
PROBE
  then
    native_emit_abort "bridge_not_ready"
  fi
  native_emit_output bridge_ready "true"
}

emit_env_outputs() {
  local ip
  ip="$(<"${GLIMMUNG_WORKING_DIR}/host_ip")"
  native_emit_output ssh_endpoint "$(native_ssh_user)@${ip}"
  native_emit_output working_dir "${GLIMMUNG_WORKING_DIR}"
}

native_run_selected_step \
  mint-credentials       mint_credentials \
  bring-up-tailnet       bring_up_tailnet \
  resolve-host-ip        resolve_host_ip \
  probe-ssh              probe_ssh \
  probe-mod-set          probe_mod_set \
  install-mcp-start-sts2 install_mcp_and_start_sts2 \
  probe-bridge-ready     probe_bridge_ready \
  emit-env-outputs       emit_env_outputs
