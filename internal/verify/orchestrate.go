package verify

import (
	"github.com/romaine-life/glimmung/harness/evidence"
	"github.com/romaine-life/glimmung/harness/step"
)

// VerifyIO is the injectable I/O surface of the verification phase. The host
// wires real implementations (dotnet test, claude.exe via harness/agent, file
// reads); tests inject fakes. Keeping the orchestration pure is what lets the
// #2 ($0-misattribution) and evidence-guard regressions be locked down without
// spawning processes.
type VerifyIO struct {
	// RunUnitTests runs the deterministic dotnet-test gate and returns the
	// observed verdict (exit code + TRX). The harness owns this; it runs BEFORE
	// the agent.
	RunUnitTests func() evidence.UnitTestResult
	// InvokeAgent runs the verification agent (the ONLY model-layer origin). It
	// writes verification.json as a side effect and returns a model-layer error
	// if the agent process itself failed. It is never called when unit tests
	// failed.
	InvokeAgent func() *step.LayeredError
	// ReadResult reads the agent-written verification.json after a clean agent run.
	ReadResult func() (Doc, error)
	// TestPlan is the parsed test-plan.json (the evidence contract); may be nil,
	// which the guard treats as a contract violation.
	TestPlan *TestPlan
	// ReadArtifact reads the concatenated text of evidence artifact files for the
	// live_mcp identity check.
	ReadArtifact func(paths []string) string
}

// VerifyOutcome is the verification phase result. Verdict is the rich
// verification.json document to write; Markdown, when set, is the verification.md
// body (the determined-failure path supplies one). AgentRan reports whether the
// model actually ran (false on the unit-test hard-gate path — that is the honest
// signal that no model tokens were spent). Err is a typed model/harness failure
// that should fail the step rather than produce a verdict.
type VerifyOutcome struct {
	Verdict  Doc
	Markdown string
	AgentRan bool
	Err      *step.LayeredError
}

// RunVerification orchestrates the verification phase around the harness-owned
// unit-test gate, mirroring Invoke-VerificationPhase + the verification branch of
// Invoke-ClaudePhase:
//
//  1. Run unit tests deterministically FIRST.
//  2. If they fail, write the determined unit_tests_failed verdict and DO NOT
//     invoke the agent (AgentRan=false) — a deterministic failure never spends
//     model tokens and is never mislabelled as a model run.
//  3. If they pass, invoke the agent; a model-process failure is a typed
//     model-layer error.
//  4. Stamp the observed unit-test verdict authoritatively over whatever the
//     agent self-reported, then apply the deterministic evidence guard.
func RunVerification(io VerifyIO) VerifyOutcome {
	observed := io.RunUnitTests()
	if !ShouldInvokeAgent(observed) {
		return VerifyOutcome{
			Verdict:  DeterminedUnitTestFailure(observed),
			Markdown: DeterminedUnitTestFailureMarkdown(observed),
			AgentRan: false,
		}
	}

	if lerr := io.InvokeAgent(); lerr != nil {
		return VerifyOutcome{AgentRan: true, Err: lerr}
	}

	result, err := io.ReadResult()
	if err != nil {
		return VerifyOutcome{AgentRan: true, Err: step.HarnessError("verification_result_unreadable", "read agent verification.json", err)}
	}

	StampAuthoritativeUnitTestResult(result, observed, io.TestPlan)
	result = ApplyEvidenceGuard(result, io.TestPlan, io.ReadArtifact)
	return VerifyOutcome{Verdict: result, AgentRan: true}
}
