package verify

import (
	"fmt"
	"strings"

	"github.com/romaine-life/glimmung/harness/evidence"
)

// ShouldInvokeAgent reports whether the verification agent should run given the
// harness's deterministic unit-test verdict. A failing result is a hard gate:
// the agent is NOT invoked, so a harness/test failure can never be billed as a
// model run at $0 of real model work (the #2 misattribution this migration kills).
func ShouldInvokeAgent(o evidence.UnitTestResult) bool { return o.Passed }

// UnitTestsBlock builds the authoritative unit_tests block from an observed
// dotnet-test verdict, matching run-phases.ps1's shape exactly.
func UnitTestsBlock(o evidence.UnitTestResult) map[string]any {
	status := "fail"
	if o.Passed {
		status = "pass"
	}
	names := o.FailedNames
	if names == nil {
		names = []string{}
	}
	return map[string]any{
		"passed":       o.Passed,
		"status":       status,
		"total":        o.Total,
		"failed":       o.Failed,
		"failed_names": names,
		"notes":        o.Notes,
	}
}

// StampAuthoritativeUnitTestResult overwrites the verification document's
// unit_tests block and the matching unit_test evidence_results row with the
// harness-observed verdict — the agent never owns this data. The unit_test row's
// evidence_id matches whatever id the test plan declared for its unit_test
// required-evidence item (defaulting to "unit-tests"). Mirrors
// Set-AuthoritativeUnitTestResult.
func StampAuthoritativeUnitTestResult(result Doc, o evidence.UnitTestResult, plan *TestPlan) {
	result.Set("unit_tests", UnitTestsBlock(o))

	unitEvidenceID := "unit-tests"
	if plan != nil {
		for _, item := range plan.RequiredEvidence {
			if item.Kind == "unit_test" && strings.TrimSpace(item.ID) != "" {
				unitEvidenceID = item.ID
				break
			}
		}
	}

	rows := result.Rows("evidence_results")
	for _, row := range rows {
		if row.Str("kind") == "unit_test" {
			row.Set("evidence_id", unitEvidenceID)
			row.Set("passed", o.Passed)
			row.Set("notes", o.Notes)
			return
		}
	}
	// No agent-written unit_test row: append one.
	names := o.FailedNames
	if names == nil {
		names = []string{}
	}
	newRow := map[string]any{
		"evidence_id":    unitEvidenceID,
		"kind":           "unit_test",
		"passed":         o.Passed,
		"artifact_paths": []any{},
		"target_visible": false,
		"text_visible":   false,
		"observed_text":  "",
		"notes":          o.Notes,
	}
	existing, _ := result["evidence_results"].([]any)
	result["evidence_results"] = append(existing, newRow)
}

// DeterminedUnitTestFailure builds the rich verification.json document written
// when the harness observes a failing unit-test result BEFORE the agent runs.
// The agent is never invoked, so the verdict carries the failing test names and
// counts straight from the observed TRX. Mirrors Write-DeterminedUnitTestFailure.
func DeterminedUnitTestFailure(o evidence.UnitTestResult) Doc {
	names := o.FailedNames
	if names == nil {
		names = []string{}
	}
	notes := fmt.Sprintf("Unit tests failed deterministically before live validation. %s", o.Notes)
	return Doc{
		"layer":                 "verification",
		"status":                "abort",
		"abort_reason":          "unit_tests_failed",
		"retryable":             true,
		"human_action_required": false,
		"notes":                 notes,
		"unit_tests": map[string]any{
			"passed":       false,
			"status":       "fail",
			"total":        o.Total,
			"failed":       o.Failed,
			"failed_names": names,
			"notes":        o.Notes,
		},
		"live_mcp_validation":   map[string]any{"passed": nil, "status": "not_run", "notes": "Skipped: unit tests are a hard gate and failed first."},
		"screenshot_validation": map[string]any{"passed": nil, "status": "not_run", "count": 0, "target_visible": false, "notes": "Skipped: unit tests are a hard gate and failed first."},
		"evidence_results": []any{
			map[string]any{
				"evidence_id":    "unit-tests",
				"kind":           "unit_test",
				"passed":         false,
				"artifact_paths": []any{},
				"target_visible": false,
				"text_visible":   false,
				"observed_text":  "",
				"notes":          o.Notes,
			},
		},
		"used_mcp":                 false,
		"used_raw_bridge_or_queue": false,
	}
}

// DeterminedUnitTestFailureMarkdown builds the verification.md body for a
// determined unit-test failure, mirroring Write-DeterminedUnitTestFailure.
func DeterminedUnitTestFailureMarkdown(o evidence.UnitTestResult) string {
	notes := fmt.Sprintf("Unit tests failed deterministically before live validation. %s", o.Notes)
	failingList := ""
	if len(o.FailedNames) > 0 {
		var b strings.Builder
		b.WriteString("\nFailing tests:\n")
		for _, n := range o.FailedNames {
			b.WriteString("- " + n + "\n")
		}
		failingList = strings.TrimRight(b.String(), "\n")
	}
	return fmt.Sprintf("Status: abort\n\nAbort reason: unit_tests_failed\n\n%s\n%s\n", notes, failingList)
}
