#!/usr/bin/env bash
#
# Contract test for spirelens's glimmung-native verify completion path.
#
# Guards the migration that made the verify phase emit a TYPED `verification`
# job completion (written to GLIMMUNG_COMPLETION_FILE) instead of a
# `verification` phase output. Glimmung treats the typed completion as the run
# attempt's source-of-truth Verification and REJECTS a verify phase that emits
# only a phase output (terminal observation verifier_contract_missing — see
# romaine-life/glimmung internal/store/store/postgres.go:terminalObservationForRun).
# This test fails if spirelens regresses to the retired phase-output verdict.

set -Eeuo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
NATIVE_DIR="${SCRIPT_DIR}/glimmung-native"
TMP_DIR="$(mktemp -d)"
trap 'rm -rf "$TMP_DIR"' EXIT

fail() {
  echo "CONTRACT FAIL: $*" >&2
  exit 1
}

# --- 1. native_completed writes a typed verification completion (managed) -----
(
  # shellcheck source=glimmung-native/lib.sh
  source "${NATIVE_DIR}/lib.sh"

  export GLIMMUNG_MANAGED_RUNNER=1
  export GLIMMUNG_OUTPUT_FILE="${TMP_DIR}/output.jsonl"
  export GLIMMUNG_COMPLETION_FILE="${TMP_DIR}/completion.json"
  : >"$GLIMMUNG_OUTPUT_FILE"
  rm -f "$GLIMMUNG_COMPLETION_FILE"

  native_completed \
    "null" \
    '{"status":"pass","reasons":["tooltip showed Energy generated 1"],"evidence_refs":["runs/spirelens/run-1/screenshots/tooltip.png"],"evidence":[{"kind":"screenshot","ref":"runs/spirelens/run-1/screenshots/tooltip.png"}]}' \
    "" "" "null"

  jq -e '
    .verification.status == "pass"
    and (.verification.evidence_refs[0] == "runs/spirelens/run-1/screenshots/tooltip.png")
    and (.verification.evidence[0].kind == "screenshot")
  ' "$GLIMMUNG_COMPLETION_FILE" >/dev/null \
    || fail "native_completed did not write a typed verification to GLIMMUNG_COMPLETION_FILE"

  # The verdict must never leak into phase outputs as a `verification` key.
  if [ -s "$GLIMMUNG_OUTPUT_FILE" ] && grep -q '"verification"' "$GLIMMUNG_OUTPUT_FILE"; then
    fail "native_completed must not write a verification phase output"
  fi
) || exit 1

# --- 2. native_completed refuses to run outside the managed runner ------------
(
  # shellcheck source=glimmung-native/lib.sh
  source "${NATIVE_DIR}/lib.sh"
  unset GLIMMUNG_MANAGED_RUNNER
  export GLIMMUNG_COMPLETION_FILE="${TMP_DIR}/unmanaged-completion.json"
  rm -f "$GLIMMUNG_COMPLETION_FILE"
  if native_completed "null" '{"status":"pass"}' "" "" "null" 2>/dev/null; then
    fail "native_completed must fail when not under the managed runner"
  fi
  [ ! -e "$GLIMMUNG_COMPLETION_FILE" ] \
    || fail "native_completed wrote a completion file outside the managed runner"
) || exit 1

# --- 3. emit_verification writes the typed completion + folds screenshots ------
(
  export GLIMMUNG_WORKING_DIR="${TMP_DIR}/work"
  mkdir -p "${GLIMMUNG_WORKING_DIR}/artifacts"
  cat >"${GLIMMUNG_WORKING_DIR}/artifacts/verification.json" <<'JSON'
{"status":"pass","reasons":["Happy Flower produced Energy generated 1"]}
JSON
  cat >"${GLIMMUNG_WORKING_DIR}/artifacts/uploaded-screenshot-refs.json" <<'JSON'
["runs/spirelens/run-1/screenshots/happy-flower-tooltip.png"]
JSON

  export GLIMMUNG_MANAGED_RUNNER=1
  export GLIMMUNG_STEP_SLUG="emit-verification"
  export GLIMMUNG_OUTPUT_FILE="${TMP_DIR}/emit-output.jsonl"
  export GLIMMUNG_COMPLETION_FILE="${TMP_DIR}/emit-completion.json"
  : >"$GLIMMUNG_OUTPUT_FILE"
  rm -f "$GLIMMUNG_COMPLETION_FILE"
  export SPIRELENS_VERIFY_SOURCE_ONLY=1

  # shellcheck source=glimmung-native/verify.sh
  source "${NATIVE_DIR}/verify.sh"
  emit_verification

  jq -e '
    .verification.status == "pass"
    and (.verification.evidence_refs | index("runs/spirelens/run-1/screenshots/happy-flower-tooltip.png") != null)
    and (.verification.evidence | any(.kind == "screenshot" and .ref == "runs/spirelens/run-1/screenshots/happy-flower-tooltip.png"))
  ' "$GLIMMUNG_COMPLETION_FILE" >/dev/null \
    || fail "emit_verification did not fold screenshot evidence into the typed verification completion"

  if [ -s "$GLIMMUNG_OUTPUT_FILE" ] && grep -q '"verification"' "$GLIMMUNG_OUTPUT_FILE"; then
    fail "emit_verification must not emit a verification phase output"
  fi
) || exit 1

# --- 4. emit_verification fails the step on a missing/malformed verdict --------
(
  export GLIMMUNG_WORKING_DIR="${TMP_DIR}/work-missing"
  mkdir -p "${GLIMMUNG_WORKING_DIR}/artifacts"
  export GLIMMUNG_MANAGED_RUNNER=1
  export GLIMMUNG_STEP_SLUG="emit-verification"
  export GLIMMUNG_OUTPUT_FILE="${TMP_DIR}/missing-output.jsonl"
  export GLIMMUNG_COMPLETION_FILE="${TMP_DIR}/missing-completion.json"
  : >"$GLIMMUNG_OUTPUT_FILE"
  rm -f "$GLIMMUNG_COMPLETION_FILE"
  export SPIRELENS_VERIFY_SOURCE_ONLY=1
  # shellcheck source=glimmung-native/verify.sh
  source "${NATIVE_DIR}/verify.sh"

  # Missing verification.json -> non-zero exit, no completion written.
  if emit_verification 2>/dev/null; then
    fail "emit_verification must fail when verification.json is missing"
  fi
  [ ! -e "$GLIMMUNG_COMPLETION_FILE" ] \
    || fail "emit_verification wrote a completion file for a missing verdict"

  # Malformed verification.json -> non-zero exit.
  printf 'not json' >"${GLIMMUNG_WORKING_DIR}/artifacts/verification.json"
  if emit_verification 2>/dev/null; then
    fail "emit_verification must fail when verification.json is malformed"
  fi
) || exit 1

# --- 5. static guard: the verdict never routes through a phase output ----------
if grep -nE 'native_emit_json_output[[:space:]]+verification' "${NATIVE_DIR}/verify.sh"; then
  fail "verify.sh routes the verdict through native_emit_json_output (retired phase-output path)"
fi
grep -q 'native_completed' "${NATIVE_DIR}/verify.sh" \
  || fail "verify.sh no longer writes the verdict via native_completed"

echo "glimmung-native verify contract OK"
