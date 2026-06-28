package pod

import (
	"sort"
	"testing"
)

// The registry must carry exactly the retired shell dispatch slugs across all
// four phases, so the registered workflow's phase shape is untouched.
func TestRegistryHasAllSlugs(t *testing.T) {
	want := []string{
		// env-prep
		"mint-credentials", "bring-up-tailnet", "resolve-host-ip", "probe-ssh",
		"probe-mod-set", "install-mcp-start-sts2", "probe-bridge-ready", "emit-env-outputs",
		// llm-work
		"run-test-plan", "collect-test-plan", "run-implementation", "push-branch", "collect-implementation",
		// llm-verify
		"build-and-deploy", "prepare-scenario", "run-verification", "collect-evidence",
		// cleanup
		"stop-laptop-processes", "remove-laptop-working-dir", "tailscale-logout", "emit",
	}
	got := Slugs()
	sort.Strings(want)
	sort.Strings(got)
	if len(got) != len(want) {
		t.Fatalf("slug count: got %d (%v), want %d", len(got), got, len(want))
	}
	for i := range want {
		if got[i] != want[i] {
			t.Errorf("slug mismatch at %d: got %q want %q", i, got[i], want[i])
		}
	}
}
