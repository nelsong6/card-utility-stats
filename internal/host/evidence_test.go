package host

import (
	"encoding/json"
	"os"
	"path/filepath"
	"testing"

	"github.com/romaine-life/spirelens/internal/verify"
)

// The verdict serialized through the SDK finalizable writer plus the host-side
// evidence staging must produce the EXACT finalizer artifact tree the
// verification_finalize primitive reads — verification.json at the artifacts
// root, screenshots/ and evidence/ populated — while preserving spirelens's
// producer-domain fields (unit_tests / live_mcp_validation / screenshot_validation)
// and the finalizer-read evidence_results kind+passed rows. This is the
// finalizer-paths + verification.json-shape coverage formerly asserted via the
// retired pod-side unzip path.
func TestFinalizerTreeAssembly(t *testing.T) {
	workingDir := t.TempDir()

	// Simulate the agent's host outputs: a screenshot and a captured
	// get_game_state JSON, plus a non-evidence artifact that must NOT be staged.
	mustWriteFile(t, filepath.Join(workingDir, "sts2-screenshots", "combat.png"), "PNGDATA")
	mustWriteFile(t, filepath.Join(workingDir, "sts2-artifacts", "live-mcp-target.json"), `{"relics":[{"id":"AKABEKO"}]}`)
	mustWriteFile(t, filepath.Join(workingDir, "sts2-artifacts", "test-plan.json"), `{"status":"pass"}`)

	verdict := verify.Doc{
		"status":                "pass",
		"notes":                 "ok",
		"unit_tests":            map[string]any{"passed": true, "status": "pass", "failed": 0},
		"live_mcp_validation":   map[string]any{"passed": true, "notes": "present"},
		"screenshot_validation": map[string]any{"passed": true, "count": 1},
		"evidence_results": []any{
			map[string]any{"evidence_id": "target-present", "kind": "live_mcp", "passed": true, "artifact_paths": []any{"live-mcp-target.json"}},
			map[string]any{"evidence_id": "value-visible", "kind": "screenshot", "passed": true, "observed_text": "Block Gained: 5"},
		},
		"used_mcp": true,
	}

	if _, err := verify.WriteVerdict(workingDir, verdict); err != nil {
		t.Fatalf("WriteVerdict: %v", err)
	}
	if err := StageFinalizerEvidence(workingDir); err != nil {
		t.Fatalf("StageFinalizerEvidence: %v", err)
	}

	// The exact finalizer paths.
	for _, p := range []string{
		filepath.Join(workingDir, "artifacts", "verification.json"),
		filepath.Join(workingDir, "artifacts", "screenshots", "combat.png"),
		filepath.Join(workingDir, "artifacts", "evidence", "live-mcp-target.json"),
	} {
		if _, err := os.Stat(p); err != nil {
			t.Errorf("missing finalizer artifact %s: %v", p, err)
		}
	}
	// A non-evidence artifact must not be remapped into evidence/.
	if _, err := os.Stat(filepath.Join(workingDir, "artifacts", "evidence", "test-plan.json")); err == nil {
		t.Error("non-evidence artifact leaked into evidence/")
	}

	// verification.json shape: finalizer-read status + preserved domain fields.
	raw, err := os.ReadFile(filepath.Join(workingDir, "artifacts", "verification.json"))
	if err != nil {
		t.Fatal(err)
	}
	var doc map[string]any
	if err := json.Unmarshal(raw, &doc); err != nil {
		t.Fatalf("decode verification.json: %v", err)
	}
	if doc["status"] != "pass" {
		t.Errorf("status = %v, want pass", doc["status"])
	}
	for _, k := range []string{"unit_tests", "live_mcp_validation", "screenshot_validation", "used_mcp"} {
		if _, ok := doc[k]; !ok {
			t.Errorf("producer-domain field %q missing from verification.json", k)
		}
	}
	ut, ok := doc["unit_tests"].(map[string]any)
	if !ok || ut["status"] != "pass" {
		t.Errorf("unit_tests block not preserved as object: %v", doc["unit_tests"])
	}
	rows, ok := doc["evidence_results"].([]any)
	if !ok || len(rows) != 2 {
		t.Fatalf("evidence_results shape: %v", doc["evidence_results"])
	}
	for _, r := range rows {
		row := r.(map[string]any)
		if _, ok := row["kind"]; !ok {
			t.Error("evidence_results row missing finalizer-read kind")
		}
		if _, ok := row["passed"]; !ok {
			t.Error("evidence_results row missing finalizer-read passed")
		}
	}
}

func mustWriteFile(t *testing.T, path, body string) {
	t.Helper()
	if err := os.MkdirAll(filepath.Dir(path), 0o755); err != nil {
		t.Fatal(err)
	}
	if err := os.WriteFile(path, []byte(body), 0o644); err != nil {
		t.Fatal(err)
	}
}
