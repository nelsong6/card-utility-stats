// Command glimmung-spirelens is spirelens's single run-harness binary with two
// faces:
//
//   - pod  <slug>     the orchestrator-pod face: builds the harness/step.Registry
//     and calls step.Main, which dispatches GLIMMUNG_STEP_SLUG.
//     Cross-compiles for Linux (the k8s job pods).
//   - host <subcmd>   the gaming-laptop face: run-phase / prepare-scenario /
//     prepare-host / restart-sts2 / probe-* / build-deploy,
//     invoked over ssh by the pod via remotehost.Conn.RunSelf.
//     Cross-compiles for Windows.
//
// Both `go build ./...` (Linux) and `GOOS=windows GOARCH=amd64 go build ./...`
// must pass; the binary built from a run's git_ref IS the harness, so git_ref
// controls grading end to end with no separately-staged shell/pwsh.
package main

import (
	"context"
	"fmt"
	"os"

	"github.com/romaine-life/glimmung/harness/step"
	"github.com/romaine-life/spirelens/internal/host"
	"github.com/romaine-life/spirelens/internal/pod"
)

func main() {
	args := os.Args[1:]

	// Host face: `glimmung-spirelens host <subcmd> ...` OR a bare host subcommand
	// (RunSelf prepends the subcommand directly after the binary path).
	if len(args) >= 1 && args[0] == "host" {
		os.Exit(host.Dispatch(context.Background(), args[1:]))
	}
	if len(args) >= 1 && isHostSubcommand(args[0]) {
		os.Exit(host.Dispatch(context.Background(), args))
	}

	// Pod face: `glimmung-spirelens pod [<slug>]`. An explicit slug arg overrides
	// GLIMMUNG_STEP_SLUG (the runner sets the env; the arg is belt-and-suspenders
	// for the documented `pod <slug>` invocation). step.Main reads the env,
	// dispatches, and os.Exits with the honest code.
	if len(args) >= 1 && args[0] == "pod" {
		if len(args) >= 2 && args[1] != "" {
			_ = os.Setenv("GLIMMUNG_STEP_SLUG", args[1])
		}
		step.Main(pod.Registry())
		return
	}

	// No recognized face: default to the pod face (the runner sets the slug env).
	if len(args) == 0 {
		step.Main(pod.Registry())
		return
	}

	fmt.Fprintf(os.Stderr, "glimmung-spirelens: unknown invocation %q (want `pod <slug>` or `host <subcmd>`)\n", args[0])
	os.Exit(2)
}

func isHostSubcommand(s string) bool {
	switch s {
	case "run-phase", "prepare-scenario", "prepare-host", "restart-sts2",
		"probe-mods", "probe-bridge", "build-deploy":
		return true
	}
	return false
}
