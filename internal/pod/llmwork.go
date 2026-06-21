package pod

import (
	"context"
	"encoding/json"
	"fmt"
	"os"
	"path/filepath"
	"strconv"

	"github.com/romaine-life/glimmung/harness/runcallbacks"
	"github.com/romaine-life/glimmung/harness/step"
)

// llmWorkHandlers returns the llm-work phase handlers (slugs unchanged from
// scripts/glimmung-native/{test-plan,implement}.sh).
func llmWorkHandlers() []step.Handler {
	return []step.Handler{
		fn("run-test-plan", runTestPlan),
		fn("collect-test-plan", collectTestPlan),
		fn("run-implementation", runImplementation),
		fn("push-branch", pushBranch),
		fn("collect-implementation", collectImplementation),
	}
}

func runTestPlan(c *step.Context) (step.Result, error) {
	return runPhaseOnHost(c, "test_plan")
}

func runImplementation(c *step.Context) (step.Result, error) {
	return runPhaseOnHost(c, "implementation")
}

// runPhaseOnHost connects, stages the host binary, mints the GitHub token, and
// runs the run-phase host subcommand for an agent phase. extra carries
// phase-specific flags (e.g. hydration b64).
func runPhaseOnHost(c *step.Context, phase string, extra ...string) (step.Result, error) {
	ctx := c.RunContext()
	conn, lerr := connect(ctx, c)
	if lerr != nil {
		return abortOrFail(c, lerr)
	}
	if lerr := stageHostBinary(ctx, conn, c); lerr != nil {
		return step.Result{}, lerr
	}
	token, lerr := runcallbacks.FromContext(c).MintGitHubToken(ctx)
	if lerr != nil {
		return step.Result{}, lerr
	}
	args := append([]string{
		"--phase", phase,
		"--issue-number", strconv.Itoa(c.IssueNumber()),
		"--repo-slug", c.IssueRepo(),
		"--repo-root", hostCheckoutPath,
		"--working-dir", hostWorkingDir(c),
		"--run-id", c.RunID(),
		"--attempt-index", strconv.Itoa(c.AttemptIndex()),
		"--github-token-b64", base64Token(token),
	}, extra...)
	if lerr := conn.RunSelf(ctx, "run-phase", args...); lerr != nil {
		return step.Result{}, lerr
	}
	return step.Result{}, nil
}

func collectTestPlan(c *step.Context) (step.Result, error) {
	return collectJSONOutput(c, "test-plan.json", "test_plan")
}

func collectImplementation(c *step.Context) (step.Result, error) {
	return collectJSONOutput(c, "implementation.json", "implementation")
}

// collectJSONOutput pulls a phase JSON artifact from the laptop and emits it as
// a phase output, mirroring native_emit_json_output (one JSON-object line whose
// value is the compact artifact JSON).
func collectJSONOutput(c *step.Context, artifactName, outputKey string) (step.Result, error) {
	ctx := c.RunContext()
	conn, lerr := connect(ctx, c)
	if lerr != nil {
		return abortOrFail(c, lerr)
	}
	local := filepath.Join(c.WorkingDir(), artifactName)
	if lerr := conn.ScpPull(ctx, hostArtifact(c, artifactName), local); lerr != nil {
		return step.Result{}, lerr
	}
	b, err := os.ReadFile(local)
	if err != nil {
		return step.Result{}, step.HarnessError("artifact_unreadable", "read "+artifactName, err)
	}
	var parsed any
	if err := json.Unmarshal(b, &parsed); err != nil {
		return step.Result{}, step.HarnessError("artifact_invalid", artifactName+" is not valid JSON", err)
	}
	if err := c.EmitJSONOutput(outputKey, parsed); err != nil {
		return step.Result{}, err
	}
	return step.Result{}, nil
}

// pushBranch commits + force-pushes the laptop checkout to glimmung/<run_id> and
// verifies the branch exists, the Go port of implement.sh's push_branch.
func pushBranch(c *step.Context) (step.Result, error) {
	ctx := c.RunContext()
	conn, lerr := connect(ctx, c)
	if lerr != nil {
		return abortOrFail(c, lerr)
	}
	token, lerr := runcallbacks.FromContext(c).MintGitHubToken(ctx)
	if lerr != nil {
		return step.Result{}, lerr
	}
	repo := c.IssueRepo()
	branch := "glimmung/" + c.RunID()
	remote := fmt.Sprintf("https://x-access-token:%s@github.com/%s.git", token, repo)
	commitMsg := fmt.Sprintf("glimmung: implement %s#%d (%s)", repo, c.IssueNumber(), c.RunID())

	gitSteps := [][]string{
		{"git", "-C", hostCheckoutPath, "config", "user.email", "glimmung-native@romaine.life"},
		{"git", "-C", hostCheckoutPath, "config", "user.name", "Glimmung Native Runner"},
		{"git", "-C", hostCheckoutPath, "checkout", "-B", branch},
		{"git", "-C", hostCheckoutPath, "add", "-A"},
		{"git", "-C", hostCheckoutPath, "commit", "--allow-empty", "-m", commitMsg},
		{"git", "-C", hostCheckoutPath, "push", "--force", remote, "HEAD:refs/heads/" + branch},
	}
	for _, s := range gitSteps {
		if err := conn.RunCommand(ctx, s...); err != nil {
			return step.Result{}, step.HostError("push_branch", "remote git "+s[3], err)
		}
	}

	if !branchExists(ctx, repo, branch, token) {
		return step.Result{}, c.Abort("implementation_branch_missing:" + branch)
	}
	if err := c.EmitOutput("branch_name", branch); err != nil {
		return step.Result{}, err
	}
	return step.Result{}, nil
}

func branchExists(ctx context.Context, repo, branch, token string) bool {
	url := fmt.Sprintf("https://api.github.com/repos/%s/branches/%s", repo, branch)
	req, err := httpNewRequest(ctx, url, token)
	if err != nil {
		return false
	}
	resp, err := httpDo(req)
	if err != nil {
		return false
	}
	defer resp.Body.Close()
	return resp.StatusCode >= 200 && resp.StatusCode < 300
}
