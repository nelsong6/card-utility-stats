package host

import (
	"context"
	"encoding/json"
	"fmt"
	"os"
	"path/filepath"
	"time"

	"github.com/romaine-life/glimmung/harness/agent"
	"github.com/romaine-life/glimmung/harness/evidence"
	"github.com/romaine-life/glimmung/harness/step"
	"github.com/romaine-life/spirelens/internal/mcpconfig"
	"github.com/romaine-life/spirelens/internal/prompts"
	"github.com/romaine-life/spirelens/internal/verify"
)

// RunPhaseParams are the run-phase host subcommand inputs, passed as flags over
// ssh (RunSelf) rather than a here-doc of env vars — the typed replacement for
// failure mode #4 (per-step env here-docs).
type RunPhaseParams struct {
	Phase        string
	IssueNumber  string
	RepoSlug     string
	RepoRoot     string
	WorkingDir   string
	RunID        string
	AttemptIndex string
	GitHubToken  string // already-decoded token (for GH_TOKEN); empty for non-implementation phases
	// Decoded handoff artifacts hydrated into the host artifact dir before the
	// phase runs, so a verify-only run (llm-work skipped) has the same artifact
	// set on disk a full run leaves. Mirrors verify.sh's native_hydrate_input_artifact.
	TestPlan       string
	Implementation string
}

func (p RunPhaseParams) artifactDir() string { return filepath.Join(p.WorkingDir, "sts2-artifacts") }
func (p RunPhaseParams) screenshotDir() string {
	return filepath.Join(p.WorkingDir, "sts2-screenshots")
}
func (p RunPhaseParams) logDir() string { return filepath.Join(p.WorkingDir, "logs") }

// RunPhase executes one phase's grading on the host. It builds ONLY the selected
// phase's prompt, runs the deterministic unit-test gate before the agent for
// verification (writing a determined verdict and skipping the agent on failure),
// invokes claude.exe via harness/agent (the only model-layer origin), runs the
// evidence guard, and writes the rich verification.json. A nil return is success;
// a *step.LayeredError attributes a typed failure.
func RunPhase(ctx context.Context, p RunPhaseParams) *step.LayeredError {
	def, ok := prompts.Definition(p.Phase)
	if !ok {
		return step.HarnessError("unknown_phase", fmt.Sprintf("unknown phase %q", p.Phase), nil)
	}

	claudePath, err := ResolveClaudeCLIPath()
	if err != nil {
		return step.HostError("claude_cli_unresolved", err.Error(), nil)
	}
	gameDir, err := ResolveSts2GameDir()
	if err != nil {
		return step.HostError("sts2_dir_unresolved", err.Error(), nil)
	}
	dataDir := Sts2DataDir(gameDir)
	mcpPath, err := mcpconfig.BuildRunConfig(p.WorkingDir, gameDir, p.RunID, p.AttemptIndex)
	if err != nil {
		return step.HarnessError("mcp_config_build", err.Error(), nil)
	}

	os.Setenv("SPIRELENS_HOST_STS2_GAME_DIR", gameDir)
	os.Setenv("SPIRELENS_HOST_STS2_DATA_DIR", dataDir)
	if p.GitHubToken != "" {
		os.Setenv("GH_TOKEN", p.GitHubToken)
	}

	artifactDir := p.artifactDir()
	screenshotDir := p.screenshotDir()
	// Mirror run-phases.ps1's per-phase dir hygiene: a fresh verification run
	// clears the screenshot dir (prior phase artifacts are preserved).
	if p.Phase == prompts.PhaseVerification {
		_ = os.RemoveAll(screenshotDir)
	}
	for _, dir := range []string{artifactDir, screenshotDir, p.logDir()} {
		if err := os.MkdirAll(dir, 0o755); err != nil {
			return step.HostError("artifact_dir", "create artifact dir", err)
		}
	}
	os.Setenv("SCREENSHOT_DIR", screenshotDir)
	os.Setenv("VALIDATION_ARTIFACT_DIR", artifactDir)

	// Hydrate handoff artifacts so a verify-only run is self-sufficient.
	if p.TestPlan != "" {
		_ = os.WriteFile(filepath.Join(artifactDir, "test-plan.json"), []byte(p.TestPlan), 0o644)
	}
	if p.Implementation != "" {
		_ = os.WriteFile(filepath.Join(artifactDir, "implementation.json"), []byte(p.Implementation), 0o644)
	}

	prompt, perr := prompts.BuildPrompt(p.Phase, prompts.Params{
		IssueNumber:           p.IssueNumber,
		RepoSlug:              p.RepoSlug,
		RepoRoot:              p.RepoRoot,
		McpConfigPath:         mcpPath,
		ValidationArtifactDir: artifactDir,
		ScreenshotDir:         screenshotDir,
	})
	if perr != nil {
		return step.HarnessError("prompt_build", perr.Error(), nil)
	}

	if p.Phase == prompts.PhaseVerification {
		return runVerification(ctx, p, def, claudePath, mcpPath, dataDir, prompt)
	}
	return runAgentPhase(ctx, p, def, claudePath, mcpPath, prompt)
}

// runVerification wires the tested verify orchestrator to real I/O.
func runVerification(ctx context.Context, p RunPhaseParams, def prompts.PhaseDef, claudePath, mcpPath, dataDir, prompt string) *step.LayeredError {
	artifactDir := p.artifactDir()
	plan := readTestPlan(artifactDir)

	out := verify.RunVerification(verify.VerifyIO{
		RunUnitTests: func() evidence.UnitTestResult {
			return runDeterministicUnitTests(ctx, p.RepoRoot, artifactDir, dataDir)
		},
		InvokeAgent: func() *step.LayeredError {
			return invokeClaude(ctx, def, claudePath, mcpPath, p.RepoRoot, prompt)
		},
		ReadResult:   func() (verify.Doc, error) { return readDoc(filepath.Join(artifactDir, def.JSON)) },
		TestPlan:     plan,
		ReadArtifact: makeArtifactReader(artifactDir, p.screenshotDir(), p.RepoRoot),
	})
	if out.Err != nil {
		return out.Err
	}

	// Serialize the verdict through the SDK finalizable writer: it lands
	// ${WorkingDir}/artifacts/verification.json and creates the screenshots/ and
	// evidence/ dirs the finalizer scans.
	finalizerArtifacts, err := verify.WriteVerdict(p.WorkingDir, out.Verdict)
	if err != nil {
		return step.HarnessError("verification_write", err.Error(), nil)
	}
	md := out.Markdown
	if md == "" {
		md = fmt.Sprintf("Status: %s\n\nAbort reason: %s\n\n%s\n", out.Verdict.Str("status"), out.Verdict.Str("abort_reason"), out.Verdict.Str("notes"))
	}
	_ = verify.WriteMarkdown(finalizerArtifacts, def.Markdown, md)
	// Assemble the screenshot + live-mcp evidence into the same finalizer tree so
	// the whole thing pulls back to the pod with one recursive ScpPullTree.
	if err := StageFinalizerEvidence(p.WorkingDir); err != nil {
		return step.HarnessError("evidence_stage", err.Error(), nil)
	}
	return nil
}

// runAgentPhase runs test_plan / implementation: invoke the agent, read its
// JSON, and assert the phase contract.
func runAgentPhase(ctx context.Context, p RunPhaseParams, def prompts.PhaseDef, claudePath, mcpPath, prompt string) *step.LayeredError {
	if lerr := invokeClaude(ctx, def, claudePath, mcpPath, p.RepoRoot, prompt); lerr != nil {
		return lerr
	}
	artifactDir := p.artifactDir()
	doc, err := readDoc(filepath.Join(artifactDir, def.JSON))
	if err != nil {
		return step.HarnessError("phase_result_unreadable", fmt.Sprintf("read %s", def.JSON), err)
	}
	if err := verify.AssertCommonStatus(doc.Str("status"), doc.Str("abort_reason"), def.AllowedAbortReasons); err != nil {
		return step.HarnessError("phase_contract", err.Error(), nil)
	}
	if doc.Str("status") != "pass" {
		return nil
	}
	switch p.Phase {
	case prompts.PhaseTestPlan:
		plan := readTestPlan(artifactDir)
		if plan == nil {
			return step.HarnessError("phase_contract", "test-plan.json unreadable for contract check", nil)
		}
		if err := verify.AssertTestPlanContract(plan); err != nil {
			return step.HarnessError("phase_contract", err.Error(), nil)
		}
	case prompts.PhaseImplementation:
		var impl struct {
			VerificationRequired *bool `json:"verification_required"`
		}
		_ = json.Unmarshal(rawJSON(filepath.Join(artifactDir, def.JSON)), &impl)
		if err := verify.AssertImplementationContract(impl.VerificationRequired); err != nil {
			return step.HarnessError("phase_contract", err.Error(), nil)
		}
	}
	return nil
}

// invokeClaude runs claude.exe via harness/agent — the only model-layer origin.
// A phase timeout becomes a clean phase_timeout verdict (exit 0), not a model
// failure; a non-zero agent exit is a typed model-layer error.
func invokeClaude(ctx context.Context, def prompts.PhaseDef, claudePath, mcpPath, repoRoot, prompt string) *step.LayeredError {
	phaseCtx, cancel := context.WithTimeout(ctx, time.Duration(def.TimeoutSeconds)*time.Second)
	defer cancel()

	args := []string{
		"-p", prompt,
		"--model", "sonnet",
		"--permission-mode", "bypassPermissions",
		"--output-format", "stream-json",
		"--verbose",
		"--strict-mcp-config",
		"--mcp-config=" + mcpPath,
		"--no-session-persistence",
		"--max-budget-usd", def.MaxBudgetUSD,
		"--add-dir", repoRoot,
	}
	if csv := joinCSV(def.AllowedTools); csv != "" {
		args = append(args, "--allowedTools", csv)
	}
	if csv := joinCSV(def.DisallowedTools); csv != "" {
		args = append(args, "--disallowedTools", csv)
	}

	_, lerr := agent.Invoke(phaseCtx, agent.Spec{
		Name: claudePath,
		Args: args,
		Dir:  repoRoot,
		// Stdout/Stderr default to os.Stdout/os.Stderr (the runner's log stream),
		// line-unbuffered, so internal/domain/agentcost prices each usage line.
	})
	if lerr != nil && phaseCtx.Err() == context.DeadlineExceeded {
		return step.HarnessError("phase_timeout", fmt.Sprintf("phase %q exceeded its %ds budget before writing a result", def.Name, def.TimeoutSeconds), nil)
	}
	return lerr
}

// runDeterministicUnitTests runs the SpireLens.Core test project and returns the
// observed verdict, mirroring Invoke-DeterministicUnitTests.
func runDeterministicUnitTests(ctx context.Context, repoRoot, artifactDir, dataDir string) evidence.UnitTestResult {
	trxName := "unit-tests.trx"
	trxPath := filepath.Join(artifactDir, trxName)
	_ = os.Remove(trxPath)
	testProject := filepath.Join(repoRoot, "Tests", "SpireLens.Core.Tests", "SpireLens.Core.Tests.csproj")

	exitCode, _ := runCmd(ctx, os.Stdout, "", nil, "dotnet", "test", testProject,
		"-c", "Debug",
		"--logger", "trx;LogFileName="+trxName,
		"--results-directory", artifactDir,
		"-p:Sts2DataDir="+dataDir,
	)
	if !fileExists(trxPath) {
		if found := findTRX(artifactDir, trxName); found != "" {
			trxPath = found
		}
	}
	return evidence.ObservedUnitTestResult(exitCode, trxPath)
}

func findTRX(dir, name string) string {
	var found string
	_ = filepath.Walk(dir, func(path string, info os.FileInfo, err error) error {
		if err == nil && info != nil && !info.IsDir() && info.Name() == name && found == "" {
			found = path
		}
		return nil
	})
	return found
}

// makeArtifactReader ports Read-EvidenceArtifactText: resolve each path literal,
// then relative to the artifact dir, screenshot dir, or repo root; concatenate.
func makeArtifactReader(artifactDir, screenshotDir, repoRoot string) func(paths []string) string {
	return func(paths []string) string {
		blob := ""
		for _, path := range paths {
			if path == "" {
				continue
			}
			for _, candidate := range []string{path, filepath.Join(artifactDir, path), filepath.Join(screenshotDir, path), filepath.Join(repoRoot, path)} {
				if b, err := os.ReadFile(candidate); err == nil {
					if blob != "" {
						blob += "\n"
					}
					blob += string(b)
					break
				}
			}
		}
		return blob
	}
}

func readTestPlan(artifactDir string) *verify.TestPlan {
	b := rawJSON(filepath.Join(artifactDir, "test-plan.json"))
	if b == nil {
		return nil
	}
	var plan verify.TestPlan
	if json.Unmarshal(b, &plan) != nil {
		return nil
	}
	return &plan
}

func readDoc(path string) (verify.Doc, error) {
	b, err := os.ReadFile(path)
	if err != nil {
		return nil, err
	}
	var doc verify.Doc
	if err := json.Unmarshal(b, &doc); err != nil {
		return nil, fmt.Errorf("invalid JSON in %s: %w", path, err)
	}
	return doc, nil
}

func rawJSON(path string) []byte {
	b, err := os.ReadFile(path)
	if err != nil {
		return nil
	}
	return b
}

func joinCSV(tools []string) string {
	out := ""
	for _, t := range tools {
		if t == "" {
			continue
		}
		if out != "" {
			out += ","
		}
		out += t
	}
	return out
}
