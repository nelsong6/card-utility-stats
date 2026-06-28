package pod

import (
	"bytes"
	"encoding/json"

	"github.com/romaine-life/glimmung/harness/step"
)

// prepareHandlers returns the env-prep phase handlers (slugs unchanged from
// scripts/glimmung-native/env-prep.sh).
func prepareHandlers() []step.Handler {
	return []step.Handler{
		fn("mint-credentials", mintCredentials),
		fn("bring-up-tailnet", bringUpTailnet),
		fn("resolve-host-ip", resolveHostIP),
		fn("probe-ssh", probeSSH),
		fn("probe-mod-set", probeModSet),
		fn("install-mcp-start-sts2", installMcpStartSts2),
		fn("probe-bridge-ready", probeBridgeReady),
		fn("emit-env-outputs", emitEnvOutputs),
	}
}

// mintCredentials establishes the venue (keypair, ssh cert, tailscale authkey,
// tailnet bring-up) via MintAndConnect.
func mintCredentials(c *step.Context) (step.Result, error) {
	_, lerr := connect(c.RunContext(), c)
	if lerr != nil {
		return abortOrFail(c, lerr)
	}
	return step.Result{}, nil
}

// bringUpTailnet is satisfied by MintAndConnect (which brings the tailnet up);
// re-establishing here keeps the slug's contract while the SDK folds the
// distinct mint/up operations into one call.
func bringUpTailnet(c *step.Context) (step.Result, error) {
	_, lerr := connect(c.RunContext(), c)
	if lerr != nil {
		return abortOrFail(c, lerr)
	}
	return step.Result{}, nil
}

// resolveHostIP resolves the laptop's tailnet IP and emits it.
func resolveHostIP(c *step.Context) (step.Result, error) {
	conn, lerr := connect(c.RunContext(), c)
	if lerr != nil {
		return abortOrFail(c, lerr)
	}
	if err := c.EmitOutput("tailnet_ip", conn.HostIP()); err != nil {
		return step.Result{}, err
	}
	return step.Result{}, nil
}

// probeSSH confirms the laptop is reachable over ssh; an unreachable host aborts
// host_unavailable (the laptop is asleep / pre-logon).
func probeSSH(c *step.Context) (step.Result, error) {
	conn, lerr := connect(c.RunContext(), c)
	if lerr != nil {
		return abortOrFail(c, lerr)
	}
	if err := conn.RunCommand(c.RunContext(), "cmd", "/c", "echo ok"); err != nil {
		return step.Result{}, c.Abort("host_unavailable")
	}
	return step.Result{}, nil
}

// probeModSet enforces the AGENTS.md mod allowlist + the BaseLib version floor.
// The host probe-mods subcommand prints its verdict JSON to stdout, which RunSelf
// streams back into the buffer here (SDK v0.2.0) — no write-to-file-then-scp-pull.
func probeModSet(c *step.Context) (step.Result, error) {
	ctx := c.RunContext()
	var verdict bytes.Buffer
	conn, lerr := connectCapturing(ctx, c, &verdict)
	if lerr != nil {
		return abortOrFail(c, lerr)
	}
	if lerr := stageHostBinary(ctx, conn, c); lerr != nil {
		return step.Result{}, lerr
	}
	if lerr := conn.RunSelf(ctx, "probe-mods"); lerr != nil {
		return step.Result{}, lerr
	}
	var res struct {
		BaseLibVersion string `json:"baselib_version"`
		AbortReason    string `json:"abort_reason"`
	}
	if err := json.Unmarshal(verdict.Bytes(), &res); err != nil {
		return step.Result{}, step.HarnessError("mod_probe_invalid", "parse streamed probe-mods verdict", err)
	}
	if res.AbortReason != "" {
		return step.Result{}, c.Abort(res.AbortReason)
	}
	if err := c.EmitOutput("baselib_version", res.BaseLibVersion); err != nil {
		return step.Result{}, err
	}
	return step.Result{}, nil
}

// installMcpStartSts2 syncs the laptop checkout to the run's exact commit, stages
// the host binary, and runs prepare-host --install-mcp --start-sts2.
func installMcpStartSts2(c *step.Context) (step.Result, error) {
	ctx := c.RunContext()
	conn, lerr := connect(ctx, c)
	if lerr != nil {
		return abortOrFail(c, lerr)
	}
	if lerr := syncHostCheckout(ctx, c, conn); lerr != nil {
		return step.Result{}, lerr
	}
	if lerr := stageHostBinary(ctx, conn, c); lerr != nil {
		return step.Result{}, lerr
	}
	if lerr := conn.RunSelf(ctx, "prepare-host", "--install-mcp", "--start-sts2"); lerr != nil {
		return step.Result{}, lerr
	}
	return step.Result{}, nil
}

// probeBridgeReady polls the SpireLensMcp bridge on the laptop; a miss aborts
// bridge_not_ready.
func probeBridgeReady(c *step.Context) (step.Result, error) {
	ctx := c.RunContext()
	conn, lerr := connect(ctx, c)
	if lerr != nil {
		return abortOrFail(c, lerr)
	}
	if lerr := stageHostBinary(ctx, conn, c); lerr != nil {
		return step.Result{}, lerr
	}
	if lerr := conn.RunSelf(ctx, "probe-bridge"); lerr != nil {
		// Non-zero remote exit (streamed through RunSelf) means the bridge never
		// came up.
		return step.Result{}, c.Abort("bridge_not_ready")
	}
	if err := c.EmitOutput("bridge_ready", "true"); err != nil {
		return step.Result{}, err
	}
	return step.Result{}, nil
}

// emitEnvOutputs publishes the ssh endpoint and working dir for later phases.
func emitEnvOutputs(c *step.Context) (step.Result, error) {
	conn, lerr := connect(c.RunContext(), c)
	if lerr != nil {
		return abortOrFail(c, lerr)
	}
	return step.Result{Outputs: map[string]string{
		"ssh_endpoint": sshUser() + "@" + conn.HostIP(),
		"working_dir":  c.WorkingDir(),
	}}, nil
}
