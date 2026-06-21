// Package pod is the orchestrator-pod face of the glimmung-spirelens binary: one
// step.Handler per workflow step slug. Handlers reach the warm gaming laptop
// through the SDK's harness/remotehost venue (MintAndConnect / RunSelf / ScpPull
// / ScpPushTree / SyncCheckout) and invoke the binary's own host face over ssh,
// replacing the retired pwsh-over-ssh here-docs in scripts/glimmung-native/*.sh.
//
// The host face is the SAME binary cross-compiled for Windows and staged onto
// the laptop per run, so git_ref controls the harness end to end (the binary is
// built from the run's checkout). This replaces the retired native_stage_harness
// scp of .github/scripts/*.ps1.
package pod

import (
	"context"
	"fmt"
	"os"
	"os/exec"
	"path/filepath"

	"github.com/romaine-life/glimmung/harness/remotehost"
	"github.com/romaine-life/glimmung/harness/step"
)

// hostTag is the tailnet tag the spirelens laptop advertises.
const hostTag = "tag:spirelens-host"

// sshUser is the Windows local account the SSH cert authenticates as (spirelens#179
// Q5: single-user, no dedicated automation account).
func sshUser() string {
	if u := os.Getenv("SPIRELENS_SSH_USER"); u != "" {
		return u
	}
	return "nelsonlaptopuser"
}

// hostWorkingDir is the laptop per-run working dir. Forward slashes throughout
// the host paths: scp's remote-path parser treats `C:\` as a second host (the
// colon), and the retired lib.sh used forward slashes for exactly this reason;
// Go's filepath and Windows file APIs both accept them.
func hostWorkingDir(c *step.Context) string {
	return "C:/glimmung-runs/" + c.RunRef()
}

// hostBinaryPath is where the staged Windows host binary lands on the laptop.
func hostBinaryPath(c *step.Context) string {
	return hostWorkingDir(c) + "/hostbin/glimmung-spirelens.exe"
}

// hostArtifact returns a forward-slash host path under the per-run sts2-artifacts dir.
func hostArtifact(c *step.Context, name string) string {
	return hostWorkingDir(c) + "/sts2-artifacts/" + name
}

// connect mints credentials, brings up the tailnet, resolves the laptop, and
// returns a connected remotehost.Conn whose RunSelf targets the staged host
// binary. Any venue failure is a host-layer error.
//
// NOTE [SDK gap, flagged for the hub]: MintAndConnect is monolithic — it
// re-keygens and re-mints the ssh cert + tailscale authkey on every call. The
// retired lib.sh native_connect_host was step-idempotent within a pod (reuse a
// running tailscaled, only re-mint the short-TTL cert). Re-running MintAndConnect
// per step works (tailscale up is idempotent) but re-mints more than necessary;
// a step-idempotent connect belongs in the SDK.
func connect(ctx context.Context, c *step.Context) (*remotehost.Conn, *step.LayeredError) {
	cfg := remotehost.FromContext(c, sshUser(), hostBinaryPath(c))
	return remotehost.MintAndConnect(ctx, cfg, hostTag)
}

// stageHostBinary cross-compiles the Windows host binary from the pod's git_ref
// checkout and scp's it onto the laptop, so RunSelf can exec it. git_ref controls
// the harness because the binary is built from the run's checkout.
//
// NOTE [SDK gap, flagged for the hub]: remotehost has ScpPushTree (a directory)
// but no single-file push, so the binary is staged via a one-file directory tree.
func stageHostBinary(ctx context.Context, c *remotehost.Conn, sc *step.Context) *step.LayeredError {
	repoRoot := sc.Env("GLIMMUNG_REPO_ROOT")
	if repoRoot == "" {
		repoRoot = "/workspace/spirelens"
	}
	stageDir := filepath.Join(sc.WorkingDir(), "hostbin")
	if err := os.MkdirAll(stageDir, 0o755); err != nil {
		return step.HarnessError("host_binary_stage", "create stage dir", err)
	}
	exePath := filepath.Join(stageDir, "glimmung-spirelens.exe")
	build := exec.CommandContext(ctx, "go", "build", "-o", exePath, "./cmd/glimmung-spirelens")
	build.Dir = repoRoot
	build.Env = append(os.Environ(), "GOOS=windows", "GOARCH=amd64")
	build.Stdout = os.Stdout
	build.Stderr = os.Stderr
	if err := build.Run(); err != nil {
		return step.HarnessError("host_binary_build", "cross-compile windows host binary", err)
	}
	// Ensure the laptop working dir exists, then push the staging tree.
	_ = c.RunCommand(ctx, "powershell", "-NoProfile", "-Command", fmt.Sprintf("New-Item -ItemType Directory -Force -Path '%s' | Out-Null", hostWorkingDir(sc)))
	if lerr := c.ScpPushTree(ctx, stageDir, hostWorkingDir(sc)); lerr != nil {
		return lerr
	}
	return nil
}
