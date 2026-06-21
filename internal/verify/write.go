package verify

import (
	"encoding/json"
	"fmt"
	"os"
	"path/filepath"

	sdkverify "github.com/romaine-life/glimmung/harness/verification"
)

// WriteVerdict serializes spirelens's rich verification verdict through the SDK's
// harness/verification.WriteFinalizable — there is no spirelens-local writer fork
// anymore. It maps the finalizer-read keys onto verification.Verification's typed
// fields (status / reasons / failure / abort_reason / notes / evidence_refs /
// evidence / evidence_results) and rides every other producer-domain key
// (layer / unit_tests / live_mcp_validation / screenshot_validation / used_mcp /
// used_raw_bridge_or_queue / retryable / human_action_required / …) through
// Verification.Extra, which the glimmung finalizer preserves verbatim. The result
// lands at the exact finalizer paths ${workingDir}/artifacts/{verification.json,
// screenshots,evidence} and WriteVerdict returns that artifacts dir.
//
// NOTE [flagged for the hub]: verification.EvidenceResult is {kind, passed,
// detail} and `evidence_results` is a reserved Extra key, so spirelens's richer
// per-row evidence fields (evidence_id / artifact_paths / target_visible /
// text_visible / observed_text) are reduced to the finalizer-read kind+passed
// subset in the persisted document. Those rich rows are still fully present in
// the in-memory verdict the deterministic evidence guard derives its decision
// from (verify.Doc); only the on-disk evidence_results array is the SDK shape.
// If the SDK grows a richer EvidenceResult (or admits evidence_results through
// Extra), spirelens can carry the full rows again with no other change.
func WriteVerdict(workingDir string, verdict Doc) (string, error) {
	v := sdkverify.Verification{
		Status:      verdict.Str("status"),
		AbortReason: verdict.Str("abort_reason"),
		Notes:       verdict.Str("notes"),
	}
	if reasons := stringSlice(verdict, "reasons"); len(reasons) > 0 {
		v.Reasons = reasons
	}
	if f := verdict.Sub("failure"); f != nil {
		v.Failure = &sdkverify.Failure{
			Expected:       f.Str("expected"),
			Observed:       f.Str("observed"),
			Where:          f.Str("where"),
			SuspectedCause: f.Str("suspected_cause"),
			CauseDetail:    f.Str("cause_detail"),
		}
	}
	if refs := stringSlice(verdict, "evidence_refs"); len(refs) > 0 {
		v.EvidenceRefs = refs
	}
	for _, e := range verdict.Rows("evidence") {
		v.Evidence = append(v.Evidence, sdkverify.EvidenceRef{Kind: e.Str("kind"), Ref: e.Str("ref")})
	}
	for _, row := range verdict.Rows("evidence_results") {
		v.EvidenceResults = append(v.EvidenceResults, sdkverify.EvidenceResult{
			Kind:   row.Str("kind"),
			Passed: row.Bool("passed"),
		})
	}

	extra := map[string]json.RawMessage{}
	for key, val := range verdict {
		switch key {
		case "status", "reasons", "failure", "abort_reason", "notes", "evidence_refs", "evidence", "evidence_results":
			continue // owned by a typed finalizer field above
		}
		raw, err := json.Marshal(val)
		if err != nil {
			return "", fmt.Errorf("encode verification extra field %q: %w", key, err)
		}
		extra[key] = raw
	}
	if len(extra) > 0 {
		v.Extra = extra
	}

	return sdkverify.WriteFinalizable(workingDir, v)
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
