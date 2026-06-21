package verify

import (
	"os"
	"path/filepath"
	"testing"

	"github.com/romaine-life/glimmung/harness/evidence"
	"github.com/romaine-life/glimmung/harness/step"
	"github.com/romaine-life/spirelens/internal/catalog"
)

// The #2 regression guard, end-to-end through the orchestrator: a failing
// deterministic unit-test result must produce the unit_tests_failed verdict with
// AgentRan=false — proving the agent (the only $-spending, model-attributed step)
// is never invoked when the harness already knows the run is red.
func TestRunVerification_UnitTestFailureNeverInvokesAgent(t *testing.T) {
	agentInvoked := false
	out := RunVerification(VerifyIO{
		RunUnitTests: func() evidence.UnitTestResult {
			return evidence.ObservedUnitTestResult(1, newTRX(t, 4, 1, []string{"SchemaLoadingTests.Boom"}, []string{"A"}))
		},
		InvokeAgent: func() *step.LayeredError {
			agentInvoked = true
			return nil
		},
		ReadResult: func() (Doc, error) { t.Fatal("ReadResult must not run when units fail"); return nil, nil },
	})

	if agentInvoked {
		t.Fatal("agent was invoked despite failing unit tests (#2 $0-misattribution regression)")
	}
	if out.AgentRan {
		t.Error("AgentRan must be false on the hard-gate path")
	}
	if out.Verdict.Str("abort_reason") != "unit_tests_failed" {
		t.Errorf("verdict abort_reason=%q, want unit_tests_failed", out.Verdict.Str("abort_reason"))
	}
	if out.Markdown == "" {
		t.Error("determined failure must supply verification.md body")
	}
}

// Passing units: agent runs, observed verdict is stamped over the agent's claim,
// and a satisfied contract passes.
func TestRunVerification_PassPath(t *testing.T) {
	jsonPath := filepath.Join(t.TempDir(), "live.json")
	_ = os.WriteFile(jsonPath, []byte(`{"relics":[{"id":"AKABEKO"}]}`), 0o644)
	tru := true
	plan := &TestPlan{
		Status: "pass",
		ScenarioIDValidation: &catalog.ScenarioIDValidation{
			Passed: true,
			Relics: []catalog.ScenarioEntry{{Field: "add_relics", Input: "AKABEKO", ID: "AKABEKO", Name: "Akabeko", Source: "lookup_relic"}},
		},
		RequiredEvidence: []RequiredEvidence{{ID: "target-present", Kind: "live_mcp", Required: &tru, MustShow: "present", TargetIDRef: "AKABEKO"}},
	}

	agentInvoked := false
	out := RunVerification(VerifyIO{
		RunUnitTests: func() evidence.UnitTestResult { return evidence.ObservedUnitTestResult(0, newTRX(t, 3, 0, nil, nil)) },
		InvokeAgent:  func() *step.LayeredError { agentInvoked = true; return nil },
		ReadResult: func() (Doc, error) {
			return Doc{
				"status": "pass",
				"evidence_results": []any{
					map[string]any{"evidence_id": "target-present", "kind": "live_mcp", "passed": true, "artifact_paths": []any{jsonPath}},
				},
			}, nil
		},
		TestPlan:     plan,
		ReadArtifact: func(paths []string) string { b, _ := os.ReadFile(jsonPath); return string(b) },
	})

	if !agentInvoked || !out.AgentRan {
		t.Fatal("agent must run when units pass")
	}
	if out.Verdict.Str("status") != "pass" {
		t.Errorf("expected pass, got %v", out.Verdict)
	}
	if ut := out.Verdict.Sub("unit_tests"); ut == nil || !ut.Bool("passed") {
		t.Error("authoritative unit_tests block must be stamped")
	}
}

// Agent process failure surfaces as a typed model-layer error.
func TestRunVerification_AgentFailureIsModelLayer(t *testing.T) {
	out := RunVerification(VerifyIO{
		RunUnitTests: func() evidence.UnitTestResult { return evidence.ObservedUnitTestResult(0, newTRX(t, 1, 0, nil, nil)) },
		InvokeAgent:  func() *step.LayeredError { return step.ModelError("agent_exit_nonzero", "claude crashed", nil) },
	})
	if out.Err == nil {
		t.Fatal("expected a model-layer error")
	}
	if out.Err.SuspectedCause() != "code_bug" {
		t.Errorf("model layer should map to code_bug suspected cause, got %s", out.Err.SuspectedCause())
	}
}
