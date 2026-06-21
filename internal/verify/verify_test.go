package verify

import (
	"fmt"
	"os"
	"path/filepath"
	"strings"
	"testing"

	"github.com/romaine-life/glimmung/harness/evidence"
	"github.com/romaine-life/spirelens/internal/catalog"
)

// newTRX builds a minimal VSTest TRX in the 2010 TeamTest namespace, mirroring
// UnitTestResult.Tests.ps1's New-TrxFixture.
func newTRX(t *testing.T, total, failed int, failedNames, passedNames []string) string {
	t.Helper()
	var b strings.Builder
	b.WriteString("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n")
	b.WriteString("<TestRun xmlns=\"http://microsoft.com/schemas/VisualStudio/TeamTest/2010\">\n  <Results>\n")
	for _, n := range passedNames {
		fmt.Fprintf(&b, "    <UnitTestResult testName=%q outcome=\"Passed\" />\n", n)
	}
	for _, n := range failedNames {
		fmt.Fprintf(&b, "    <UnitTestResult testName=%q outcome=\"Failed\" />\n", n)
	}
	b.WriteString("  </Results>\n  <ResultSummary outcome=\"Completed\">\n")
	fmt.Fprintf(&b, "    <Counters total=%q executed=%q passed=%q failed=%q />\n",
		itoa(total), itoa(total), itoa(total-failed), itoa(failed))
	b.WriteString("  </ResultSummary>\n</TestRun>\n")
	path := filepath.Join(t.TempDir(), "unit-tests.trx")
	if err := os.WriteFile(path, []byte(b.String()), 0o644); err != nil {
		t.Fatal(err)
	}
	return path
}

func itoa(n int) string { return fmt.Sprintf("%d", n) }

// Ported from UnitTestResult.Tests.ps1: the observed-result parser the SDK now
// owns must preserve the spirelens contract (exit code + TRX -> verdict).
func TestObservedUnitTestResult_Ported(t *testing.T) {
	// all pass
	r := evidence.ObservedUnitTestResult(0, newTRX(t, 99, 0, nil, []string{"A", "B", "C"}))
	if !r.Passed || r.Total != 99 || r.Failed != 0 || len(r.FailedNames) != 0 {
		t.Errorf("all-pass: %+v", r)
	}
	// N failures
	r = evidence.ObservedUnitTestResult(1, newTRX(t, 50, 2,
		[]string{"SchemaLoadingTests.LoadsPooledShape", "PoisonTooltipTests.ShowsDownstreamDamage"}, []string{"X", "Y"}))
	if r.Passed || r.Total != 50 || r.Failed != 2 {
		t.Errorf("failures: %+v", r)
	}
	if !containsStr(r.FailedNames, "SchemaLoadingTests.LoadsPooledShape") || !strings.Contains(r.Notes, "SchemaLoadingTests.LoadsPooledShape") {
		t.Errorf("failing names not surfaced: %+v", r)
	}
	// the historical trap: "99 passed, 0 failed" -> passed (no prose)
	r = evidence.ObservedUnitTestResult(0, newTRX(t, 99, 0, nil, nil))
	if !r.Passed {
		t.Error("99 passed 0 failed must be passed")
	}
	// exit code is part of the verdict
	r = evidence.ObservedUnitTestResult(1, newTRX(t, 10, 0, nil, nil))
	if r.Passed {
		t.Error("clean TRX with nonzero exit must be passed=false")
	}
	// enumerated failing rows outvote a stale 0 counter
	r = evidence.ObservedUnitTestResult(1, newTRX(t, 5, 0, []string{"Flaky.Test"}, nil))
	if r.Passed || r.Failed != 1 || !containsStr(r.FailedNames, "Flaky.Test") {
		t.Errorf("enumerated rows must outvote stale counter: %+v", r)
	}
	// missing TRX
	missing := filepath.Join(t.TempDir(), "nope.trx")
	if r = evidence.ObservedUnitTestResult(1, missing); r.Passed || !strings.Contains(r.Notes, "no structured TRX") {
		t.Errorf("missing TRX nonzero: %+v", r)
	}
	if r = evidence.ObservedUnitTestResult(0, missing); !r.Passed || !strings.Contains(r.Notes, "no structured TRX") {
		t.Errorf("missing TRX zero: %+v", r)
	}
	// unparseable TRX
	junk := filepath.Join(t.TempDir(), "junk.trx")
	_ = os.WriteFile(junk, []byte("this is not xml <<<"), 0o644)
	if r = evidence.ObservedUnitTestResult(1, junk); r.Passed {
		t.Errorf("unparseable TRX nonzero must be passed=false: %+v", r)
	}
}

// #2 regression guard at the gate level: a failing observed result must NOT
// proceed to the agent, and the determined verdict must carry the failing names.
func TestUnitTestGateBlocksAgentOnFailure(t *testing.T) {
	failed := evidence.ObservedUnitTestResult(1, newTRX(t, 3, 1, []string{"PoisonTooltipTests.Boom"}, []string{"A", "B"}))
	if ShouldInvokeAgent(failed) {
		t.Fatal("agent must NOT be invoked when unit tests failed (the $0-misattribution guard)")
	}
	doc := DeterminedUnitTestFailure(failed)
	if doc.Str("status") != "abort" || doc.Str("abort_reason") != "unit_tests_failed" {
		t.Errorf("determined failure verdict wrong: %v", doc)
	}
	if ut := doc.Sub("unit_tests"); ut == nil || ut.Bool("passed") {
		t.Error("unit_tests block must report passed=false")
	}
	names, _ := doc.Sub("unit_tests")["failed_names"].([]string)
	if !containsStr(names, "PoisonTooltipTests.Boom") {
		t.Errorf("determined failure must enumerate failing tests, got %v", names)
	}

	passed := evidence.ObservedUnitTestResult(0, newTRX(t, 3, 0, nil, []string{"A", "B", "C"}))
	if !ShouldInvokeAgent(passed) {
		t.Error("agent must be invoked when unit tests pass")
	}
}

// Stamping overwrites the agent's self-reported unit-test data with the observed
// verdict and keeps the unit_test evidence row consistent.
func TestStampAuthoritativeUnitTestResult(t *testing.T) {
	observed := evidence.ObservedUnitTestResult(0, newTRX(t, 7, 0, nil, nil))
	// Agent lied: claims a unit_test row passed with a different evidence_id.
	result := Doc{
		"status": "pass",
		"evidence_results": []any{
			map[string]any{"evidence_id": "made-up", "kind": "unit_test", "passed": false, "notes": "agent claim"},
		},
	}
	tru := true
	plan := &TestPlan{RequiredEvidence: []RequiredEvidence{{ID: "my-unit-tests", Kind: "unit_test", Required: &tru}}}
	StampAuthoritativeUnitTestResult(result, observed, plan)

	ut := result.Sub("unit_tests")
	if ut == nil || !ut.Bool("passed") || ut.IntField("total", -1) != 7 {
		t.Errorf("unit_tests block not stamped: %v", ut)
	}
	row := result.Rows("evidence_results")[0]
	if row.Str("evidence_id") != "my-unit-tests" || !row.Bool("passed") {
		t.Errorf("unit_test row not reconciled: %v", row)
	}
}

// Evidence guard: pass-through when not pass; abort when contract missing.
func TestApplyEvidenceGuard_ContractGuards(t *testing.T) {
	// non-pass returns unchanged
	d := Doc{"status": "abort", "abort_reason": "phase_timeout"}
	if got := ApplyEvidenceGuard(d, nil, nil); got.Str("abort_reason") != "phase_timeout" {
		t.Error("non-pass must pass through unchanged")
	}
	// pass with nil plan -> artifact_contract_missing
	d = Doc{"status": "pass"}
	got := ApplyEvidenceGuard(d, nil, nil)
	if got.Str("status") != "abort" || got.Str("abort_reason") != "artifact_contract_missing" {
		t.Errorf("missing contract must abort: %v", got)
	}
}

// Evidence guard end-to-end: the corrected #147 contract passes; observed
// mismatch (vigor 88 vs needle 8) is caught as claimed_result_not_observed.
func TestApplyEvidenceGuard_EndToEnd(t *testing.T) {
	tru := true
	plan := &TestPlan{
		Status: "pass",
		ScenarioIDValidation: &catalog.ScenarioIDValidation{
			Passed: true,
			Relics: []catalog.ScenarioEntry{{Field: "add_relics", Input: "CLOAK_CLASP", ID: "CLOAK_CLASP", Name: "Cloak Clasp", Source: "lookup_relic"}},
		},
		RequiredEvidence: []RequiredEvidence{
			{ID: "target-present", Kind: "live_mcp", Required: &tru, MustShow: "relic present", TargetIDRef: "CLOAK_CLASP"},
			{ID: "value-visible", Kind: "screenshot", Required: &tru, MustShow: "tooltip block gained", TextVisibleRequired: &tru, ExpectedText: "Block Gained: 5"},
		},
	}
	jsonPath := filepath.Join(t.TempDir(), "live-mcp-target-present.json")
	_ = os.WriteFile(jsonPath, []byte(`{"player":{"relics":[{"id":"CLOAK_CLASP","block_gained":5}]}}`), 0o644)
	reader := func(paths []string) string {
		b, _ := os.ReadFile(jsonPath)
		return string(b)
	}

	good := Doc{
		"status":                "pass",
		"screenshot_validation": map[string]any{"passed": true, "target_visible": true, "count": 1, "notes": ""},
		"evidence_results": []any{
			map[string]any{"evidence_id": "target-present", "kind": "live_mcp", "passed": true, "artifact_paths": []any{jsonPath}},
			map[string]any{"evidence_id": "value-visible", "kind": "screenshot", "passed": true, "artifact_paths": []any{"shot.png"}, "target_visible": true, "text_visible": true, "observed_text": "Cloak Clasp — Block Gained: 5"},
		},
	}
	if got := ApplyEvidenceGuard(good, plan, reader); got.Str("status") != "pass" {
		t.Errorf("correct contract must pass, got %v", got)
	}

	// vigor 88 vs needle 8 -> claimed_result_not_observed (PR #170)
	bad := Doc{
		"status":                "pass",
		"screenshot_validation": map[string]any{"passed": true, "target_visible": true, "count": 1, "notes": ""},
		"evidence_results": []any{
			map[string]any{"evidence_id": "target-present", "kind": "live_mcp", "passed": true, "artifact_paths": []any{jsonPath}},
			map[string]any{"evidence_id": "value-visible", "kind": "screenshot", "passed": true, "artifact_paths": []any{"shot.png"}, "target_visible": true, "text_visible": true, "observed_text": "Block Gained: 55"},
		},
	}
	if got := ApplyEvidenceGuard(bad, plan, reader); got.Str("abort_reason") != "claimed_result_not_observed" {
		t.Errorf("mismatch must abort claimed_result_not_observed, got %v", got.Str("abort_reason"))
	}

	// live_mcp id absent from captured JSON -> mcp_state_mismatch
	absentReader := func(paths []string) string { return `{"relics":[{"id":"AKABEKO"}]}` }
	if got := ApplyEvidenceGuard(copyDoc(good), plan, absentReader); got.Str("abort_reason") != "mcp_state_mismatch" {
		t.Errorf("absent id must abort mcp_state_mismatch, got %v", got.Str("abort_reason"))
	}
}

func TestAssertTestPlanContract(t *testing.T) {
	tru := true
	plan := &TestPlan{
		Status:                "pass",
		ScenarioSetup:         &ScenarioSetup{BaseSaveName: "base_regent", ScenarioName: "issue_1", Deck: []string{"STRIKE"}},
		ScenarioIDValidation:  &catalog.ScenarioIDValidation{Passed: true, Cards: []catalog.ScenarioEntry{{Field: "deck", Input: "STRIKE", ID: "STRIKE", Name: "Strike", Source: "lookup_card"}}},
		CardMetadataDiscovery: &Discovery{Passed: &tru},
		RequiredEvidence:      []RequiredEvidence{{ID: "u", Kind: "unit_test", Required: &tru, MustShow: "tests"}},
	}
	if err := AssertTestPlanContract(plan); err != nil {
		t.Errorf("valid plan must pass: %v", err)
	}
	// scenario_setup id without matching validation entry -> error
	plan.ScenarioSetup.Deck = []string{"STRIKE", "GHOST"}
	if err := AssertTestPlanContract(plan); err == nil || !strings.Contains(err.Error(), "missing validated entry") {
		t.Errorf("uncovered id must fail, got %v", err)
	}
}

func containsStr(s []string, target string) bool {
	for _, v := range s {
		if v == target {
			return true
		}
	}
	return false
}

func copyDoc(d Doc) Doc {
	out := Doc{}
	for k, v := range d {
		out[k] = v
	}
	return out
}
