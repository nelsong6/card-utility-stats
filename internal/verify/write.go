package verify

import (
	"encoding/json"
	"fmt"
	"os"
	"path/filepath"
	"strings"

	sdkverify "github.com/romaine-life/glimmung/harness/verification"
)

// ValidateStatus checks the verdict status against the SDK's finalizer rules
// (pass/fail/error/abort, abort requires abort_reason) — the same contract
// harness/verification.Verification.Validate enforces, applied to spirelens's
// richer document. Keeping the rule sourced from the SDK constants means a
// finalizer change is a compile-time concern here, not silent drift.
func ValidateStatus(doc Doc) error {
	switch doc.Str("status") {
	case sdkverify.StatusPass, sdkverify.StatusFail, sdkverify.StatusError:
		return nil
	case sdkverify.StatusAbort:
		if strings.TrimSpace(doc.Str("abort_reason")) == "" {
			return fmt.Errorf("verification status=abort requires abort_reason")
		}
		return nil
	default:
		return fmt.Errorf("invalid verification status %q (want pass, fail, error, or abort)", doc.Str("status"))
	}
}

// WriteVerificationJSON validates doc and writes it as verification.json under
// artifactDir, creating artifactDir if needed. It is the spirelens-local
// equivalent of harness/verification.WriteFinalizable: the SDK's narrow
// Verification struct cannot carry spirelens's rich domain fields (unit_tests,
// live_mcp_validation, screenshot_validation, the rich evidence_results rows),
// which the glimmung finalizer reads-the-known-fields-and-preserves-the-rest, so
// the producer must write the document itself. This reuses the SDK's status
// enum and validation rule (see ValidateStatus). [SDK GAP — flagged for the hub.]
func WriteVerificationJSON(artifactDir string, doc Doc) error {
	if err := ValidateStatus(doc); err != nil {
		return err
	}
	if err := os.MkdirAll(artifactDir, 0o755); err != nil {
		return fmt.Errorf("create artifact dir: %w", err)
	}
	return writeJSONIndent(filepath.Join(artifactDir, "verification.json"), doc)
}

// EnsureFinalizerArtifactTree creates the exact directory tree the glimmung
// verification_finalize primitive scans under a working dir:
// artifacts/{screenshots,evidence}. This mirrors the dir-creation half of
// harness/verification.WriteFinalizable and returns the artifacts dir. Used by
// the pod collect-evidence step that assembles the finalizer tree from the
// host-pulled verdict + evidence.
func EnsureFinalizerArtifactTree(workingDir string) (string, error) {
	artifacts := sdkverify.ArtifactsDir(workingDir)
	for _, dir := range []string{artifacts, filepath.Join(artifacts, "screenshots"), filepath.Join(artifacts, "evidence")} {
		if err := os.MkdirAll(dir, 0o755); err != nil {
			return "", fmt.Errorf("create %s: %w", dir, err)
		}
	}
	return artifacts, nil
}

// WriteMarkdown writes a phase markdown body to artifactDir/<name>.
func WriteMarkdown(artifactDir, name, body string) error {
	if err := os.MkdirAll(artifactDir, 0o755); err != nil {
		return err
	}
	return os.WriteFile(filepath.Join(artifactDir, name), []byte(body), 0o644)
}

// SyntheticRollup builds the rollup result.json document for an aborted phase,
// mirroring Write-SyntheticRollup. unitTests, when non-nil, carries the phase's
// authoritative unit_tests block into the rollup.
func SyntheticRollup(issueNumber int, abortLayer, abortReason, notes string, unitTests map[string]any) Doc {
	unitTestsBlock := unitTests
	if unitTestsBlock == nil {
		unitTestsBlock = map[string]any{"passed": nil, "status": "blocked", "notes": ""}
	}
	layers := map[string]any{
		"test_plan":      map[string]any{"status": "not_run", "abort_reason": nil},
		"implementation": map[string]any{"status": "not_run", "abort_reason": nil},
		"verification":   map[string]any{"status": "not_run", "abort_reason": nil},
	}
	if l, ok := layers[abortLayer].(map[string]any); ok {
		l["status"] = "abort"
		l["abort_reason"] = abortReason
	}
	return Doc{
		"issue_number":             issueNumber,
		"status":                   "blocked",
		"abort_layer":              abortLayer,
		"abort_reason":             abortReason,
		"retryable":                false,
		"human_action_required":    true,
		"layers":                   layers,
		"unit_tests":               unitTestsBlock,
		"live_mcp_validation":      map[string]any{"passed": nil, "status": "blocked", "notes": ""},
		"screenshot_validation":    map[string]any{"passed": nil, "status": "blocked", "count": 0, "notes": ""},
		"card_metadata_discovery":  map[string]any{"passed": nil, "status": "blocked", "notes": ""},
		"used_mcp":                 nil,
		"used_raw_bridge_or_queue": nil,
		"opened_pr":                nil,
		"opened_pr_url":            nil,
		"should_close_issue":       false,
		"evidence_summary":         []any{notes},
	}
}

func writeJSONIndent(path string, v any) error {
	encoded, err := json.MarshalIndent(v, "", "  ")
	if err != nil {
		return fmt.Errorf("encode %s: %w", path, err)
	}
	if err := os.WriteFile(path, append(encoded, '\n'), 0o644); err != nil {
		return fmt.Errorf("write %s: %w", path, err)
	}
	return nil
}
