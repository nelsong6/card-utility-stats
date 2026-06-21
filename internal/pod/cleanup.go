package pod

import (
	"github.com/romaine-life/glimmung/harness/step"
)

// cleanupHandlers returns the always-run teardown handlers (slugs unchanged from
// scripts/glimmung-native/env-destroy.sh). Teardown is best-effort: an
// unreachable laptop skips laptop-side cleanup rather than failing the run.
func cleanupHandlers() []step.Handler {
	return []step.Handler{
		fn("stop-laptop-processes", stopLaptopProcesses),
		fn("remove-laptop-working-dir", removeLaptopWorkingDir),
		fn("tailscale-logout", tailscaleLogout),
		fn("emit", cleanupEmit),
	}
}

func stopLaptopProcesses(c *step.Context) (step.Result, error) {
	ctx := c.RunContext()
	conn, lerr := connect(ctx, c)
	if lerr != nil {
		// Host unreachable during teardown is not a failure.
		return step.Result{}, nil
	}
	// taskkill returns non-zero when an image is absent; ignore (best-effort).
	for _, image := range []string{"SpireLensMcp.exe", "SlayTheSpire2.exe", "crashpad_handler.exe"} {
		_ = conn.RunCommand(ctx, "taskkill", "/F", "/T", "/IM", image)
	}
	return step.Result{}, nil
}

func removeLaptopWorkingDir(c *step.Context) (step.Result, error) {
	ctx := c.RunContext()
	conn, lerr := connect(ctx, c)
	if lerr != nil {
		return step.Result{}, nil
	}
	_ = conn.RunCommand(ctx, "powershell", "-NoProfile", "-Command",
		"Remove-Item -Recurse -Force -LiteralPath '"+hostWorkingDir(c)+"' -ErrorAction SilentlyContinue")
	return step.Result{}, nil
}

// tailscaleLogout tears down the ephemeral tailnet node. The node is ephemeral
// so it dies with the pod regardless; this is best-effort and non-fatal.
func tailscaleLogout(c *step.Context) (step.Result, error) {
	// The orchestrator-side tailnet node is ephemeral and dies when this pod
	// disconnects; there is no durable logout to perform from the SDK surface.
	// (lib.sh ran `tailscale --socket=... logout`; the SDK owns the daemon
	// lifecycle and does not expose a logout, so this is a no-op that the
	// ephemeral node makes safe.)
	return step.Result{}, nil
}

func cleanupEmit(c *step.Context) (step.Result, error) {
	return step.Result{Outputs: map[string]string{"cleanup_status": "done"}}, nil
}
