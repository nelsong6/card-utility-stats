package pod

import (
	"archive/zip"
	"os"
	"path/filepath"
	"sort"
	"testing"

	"github.com/romaine-life/spirelens/internal/verify"
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

// collect-evidence must assemble the EXACT finalizer artifact tree the
// verification_finalize primitive reads: artifacts/{verification.json,
// screenshots/*, evidence/*}. This exercises the unpack half against a packed zip.
func TestCollectEvidenceFinalizerPaths(t *testing.T) {
	workingDir := t.TempDir()

	// Build an evidence zip the host's pack-evidence would produce.
	zipPath := filepath.Join(t.TempDir(), "evidence.zip")
	f, err := os.Create(zipPath)
	if err != nil {
		t.Fatal(err)
	}
	zw := zip.NewWriter(f)
	for name, body := range map[string]string{
		"verification.json":             `{"status":"pass"}`,
		"screenshots/combat.png":        "PNGDATA",
		"evidence/live-mcp-target.json": `{"relics":[{"id":"AKABEKO"}]}`,
	} {
		w, _ := zw.Create(name)
		_, _ = w.Write([]byte(body))
	}
	zw.Close()
	f.Close()

	artifacts, err := verify.EnsureFinalizerArtifactTree(workingDir)
	if err != nil {
		t.Fatal(err)
	}
	if err := unzipInto(zipPath, artifacts); err != nil {
		t.Fatal(err)
	}

	// The exact paths verification_finalize reads.
	for _, p := range []string{
		filepath.Join(workingDir, "artifacts", "verification.json"),
		filepath.Join(workingDir, "artifacts", "screenshots", "combat.png"),
		filepath.Join(workingDir, "artifacts", "evidence", "live-mcp-target.json"),
	} {
		if _, err := os.Stat(p); err != nil {
			t.Errorf("missing finalizer artifact %s: %v", p, err)
		}
	}
	// The screenshots/ and evidence/ dirs exist even before unpack (empty-tree case).
	for _, d := range []string{"screenshots", "evidence"} {
		if fi, err := os.Stat(filepath.Join(workingDir, "artifacts", d)); err != nil || !fi.IsDir() {
			t.Errorf("finalizer dir %s missing", d)
		}
	}
}
