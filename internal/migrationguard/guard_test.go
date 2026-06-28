// Package migrationguard holds the reintroduction guard for the shell+pwsh →
// Go run-harness migration. Per migration-policy.md ("Tests should fail if the
// retired path is reintroduced into live code"), it fails if any retired
// shell/pwsh harness path or its sentinel symbols reappear in the tree.
package migrationguard

import (
	"os"
	"path/filepath"
	"strings"
	"testing"
)

func repoRoot(t *testing.T) string {
	t.Helper()
	root, err := filepath.Abs(filepath.Join("..", ".."))
	if err != nil {
		t.Fatal(err)
	}
	return root
}

// The retired harness directories must stay deleted end to end.
func TestRetiredHarnessDirsAreGone(t *testing.T) {
	root := repoRoot(t)
	for _, dir := range []string{
		filepath.Join(root, "scripts", "glimmung-native"),
		filepath.Join(root, ".github", "scripts"),
	} {
		if _, err := os.Stat(dir); err == nil {
			t.Errorf("retired harness dir reintroduced: %s", dir)
		}
	}
}

// No shell/pwsh harness file may reappear under the retired locations, and the
// repo must carry no .ps1 at all (the only pwsh was the retired harness; the
// external spire-lens-mcp build.ps1 lives in another repo).
func TestNoRetiredHarnessFiles(t *testing.T) {
	root := repoRoot(t)
	_ = filepath.Walk(root, func(path string, info os.FileInfo, err error) error {
		if err != nil || info == nil || info.IsDir() {
			return nil
		}
		rel, _ := filepath.Rel(root, path)
		if strings.HasPrefix(rel, ".git"+string(os.PathSeparator)) || strings.Contains(rel, "node_modules") {
			return nil
		}
		if strings.HasSuffix(path, ".ps1") {
			t.Errorf("retired PowerShell harness file reintroduced: %s", rel)
		}
		if strings.HasPrefix(rel, "scripts"+string(os.PathSeparator)+"glimmung-native") && strings.HasSuffix(path, ".sh") {
			t.Errorf("retired shell harness file reintroduced: %s", rel)
		}
		return nil
	})
}

// The retired shell-producer sentinels must not reappear in EXECUTABLE shell /
// pwsh / CI files (where they would mean the retired producer fork itself came
// back). Go source is intentionally exempt: the Go harness documents what it
// replaced ("ported from run-phases.ps1", "mirrors native_emit_json_output"),
// and naming the retired path in a comment is correct migration documentation,
// not a reintroduction.
func TestNoRetiredSentinelSymbolsInShell(t *testing.T) {
	root := repoRoot(t)
	sentinels := []string{
		"native_emit_output", "native_emit_json_output", "native_emit_abort",
		"native_run_selected_step", "native_connect_host", "native_stage_harness",
		"native_ssh_run", "native_mint_ssh_cert",
	}
	_ = filepath.Walk(root, func(path string, info os.FileInfo, err error) error {
		if err != nil || info == nil || info.IsDir() {
			return nil
		}
		rel, _ := filepath.Rel(root, path)
		if strings.HasPrefix(rel, ".git"+string(os.PathSeparator)) || strings.Contains(rel, "node_modules") {
			return nil
		}
		switch filepath.Ext(path) {
		case ".sh", ".ps1", ".bash", ".yml", ".yaml":
		default:
			return nil
		}
		b, readErr := os.ReadFile(path)
		if readErr != nil {
			return nil
		}
		content := string(b)
		for _, s := range sentinels {
			if strings.Contains(content, s) {
				t.Errorf("retired shell-producer sentinel %q reintroduced in %s", s, rel)
			}
		}
		return nil
	})
}
