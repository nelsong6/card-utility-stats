package pod

import (
	"archive/zip"
	"io"
	"os"
	"path/filepath"
	"strconv"
	"strings"

	"github.com/romaine-life/glimmung/harness/step"
	"github.com/romaine-life/spirelens/internal/verify"
)

// llmVerifyHandlers returns the llm-verify phase handlers (slugs unchanged from
// scripts/glimmung-native/verify.sh).
func llmVerifyHandlers() []step.Handler {
	return []step.Handler{
		fn("build-and-deploy", buildAndDeploy),
		fn("prepare-scenario", prepareScenario),
		fn("run-verification", runVerification),
		fn("collect-evidence", collectEvidence),
	}
}

// buildAndDeploy builds + deploys the implementation branch on the laptop.
func buildAndDeploy(c *step.Context) (step.Result, error) {
	ctx := c.RunContext()
	branch, err := c.Input("branch_name")
	if err != nil {
		return step.Result{}, err
	}
	conn, lerr := connect(ctx, c)
	if lerr != nil {
		return abortOrFail(c, lerr)
	}
	if lerr := stageHostBinary(ctx, conn, c); lerr != nil {
		return step.Result{}, lerr
	}
	token, lerr := mintGitHubToken(ctx, c)
	if lerr != nil {
		return step.Result{}, lerr
	}
	if lerr := conn.RunSelf(ctx, "build-deploy",
		"--branch", branch,
		"--repo-root", hostCheckoutPath,
		"--github-token-b64", base64Token(token),
	); lerr != nil {
		return step.Result{}, lerr
	}
	return step.Result{}, nil
}

// prepareScenario rigs the deterministic STS2 save, hydrating test-plan.json from
// the declared input so a verify-only run is self-sufficient.
func prepareScenario(c *step.Context) (step.Result, error) {
	ctx := c.RunContext()
	conn, lerr := connect(ctx, c)
	if lerr != nil {
		return abortOrFail(c, lerr)
	}
	if lerr := stageHostBinary(ctx, conn, c); lerr != nil {
		return step.Result{}, lerr
	}
	args := []string{"--working-dir", hostWorkingDir(c), "--issue-number", strconv.Itoa(c.IssueNumber())}
	if tp, ok := c.OptionalInput("test_plan"); ok {
		args = append(args, "--test-plan-b64", base64Token(tp))
	}
	if lerr := conn.RunSelf(ctx, "prepare-scenario", args...); lerr != nil {
		return step.Result{}, lerr
	}
	return step.Result{}, nil
}

// runVerification runs the verification phase on the laptop, hydrating the
// handoff artifacts so a verify-only run has the same artifact set on disk.
func runVerification(c *step.Context) (step.Result, error) {
	extra := []string{}
	if tp, ok := c.OptionalInput("test_plan"); ok {
		extra = append(extra, "--test-plan-b64", base64Token(tp))
	}
	if impl, ok := c.OptionalInput("implementation"); ok {
		extra = append(extra, "--implementation-b64", base64Token(impl))
	}
	return runPhaseOnHost(c, "verification", extra...)
}

// collectEvidence pulls the verdict + evidence from the laptop and assembles the
// exact finalizer artifact tree the verification_finalize primitive reads:
// ${GLIMMUNG_WORKING_DIR}/artifacts/{verification.json,screenshots,evidence}.
// The host packs them into one zip (remotehost has no directory pull), which the
// pod pulls with a single ScpPull and unpacks.
func collectEvidence(c *step.Context) (step.Result, error) {
	ctx := c.RunContext()
	conn, lerr := connect(ctx, c)
	if lerr != nil {
		return abortOrFail(c, lerr)
	}
	if lerr := stageHostBinary(ctx, conn, c); lerr != nil {
		return step.Result{}, lerr
	}
	remoteZip := hostWorkingDir(c) + "/evidence.zip"
	if lerr := conn.RunSelf(ctx, "pack-evidence", "--working-dir", hostWorkingDir(c), "--out", remoteZip); lerr != nil {
		return step.Result{}, lerr
	}
	localZip := filepath.Join(c.WorkingDir(), "evidence.zip")
	if lerr := conn.ScpPull(ctx, remoteZip, localZip); lerr != nil {
		return step.Result{}, lerr
	}
	artifacts, err := verify.EnsureFinalizerArtifactTree(c.WorkingDir())
	if err != nil {
		return step.Result{}, step.HarnessError("artifact_tree", err.Error(), nil)
	}
	if err := unzipInto(localZip, artifacts); err != nil {
		return step.Result{}, step.HarnessError("evidence_unpack", "unpack evidence zip", err)
	}
	return step.Result{}, nil
}

// unzipInto extracts a zip into destDir, refusing entries that escape it.
func unzipInto(zipPath, destDir string) error {
	zr, err := zip.OpenReader(zipPath)
	if err != nil {
		return err
	}
	defer zr.Close()
	for _, f := range zr.File {
		target := filepath.Join(destDir, f.Name)
		if !strings.HasPrefix(target, filepath.Clean(destDir)+string(os.PathSeparator)) && target != filepath.Clean(destDir) {
			continue // zip-slip guard
		}
		if f.FileInfo().IsDir() {
			_ = os.MkdirAll(target, 0o755)
			continue
		}
		if err := os.MkdirAll(filepath.Dir(target), 0o755); err != nil {
			return err
		}
		if err := extractZipFile(f, target); err != nil {
			return err
		}
	}
	return nil
}

func extractZipFile(f *zip.File, target string) error {
	rc, err := f.Open()
	if err != nil {
		return err
	}
	defer rc.Close()
	out, err := os.Create(target)
	if err != nil {
		return err
	}
	defer out.Close()
	_, err = io.Copy(out, rc)
	return err
}
