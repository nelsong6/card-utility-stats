package catalog

import (
	"strings"
	"testing"

	"github.com/romaine-life/glimmung/harness/evidence"
)

// cloakClaspRefMaps mirrors EvidenceGuard.Tests.ps1's New-CloakClaspRefMaps.
func cloakClaspRefMaps() RefMaps {
	return NewScenarioRefMaps(&ScenarioIDValidation{
		Passed: true,
		Relics: []ScenarioEntry{
			{Field: "add_relics", Input: "CLOAK_CLASP", ID: "CLOAK_CLASP", Name: "Cloak Clasp", Source: "lookup_relic"},
		},
	})
}

// Test-ExpectedTextMatch — numeric exactness (PR #170 guard). Exercises the
// SDK matcher spirelens binds its needles to.
func TestExpectedTextMatch_NumericExactness(t *testing.T) {
	if !evidence.ExpectedTextMatch("vigor gained: 8", "Akabeko vigor gained: 8") {
		t.Error("expected boundary match to pass")
	}
	if !evidence.ExpectedTextMatch("Vigor Gained: 8", "akabeko vigor gained: 8") {
		t.Error("expected case-insensitive match to pass")
	}
	for _, observed := range []string{"Akabeko vigor gained: 88", "vigor gained: 80", "vigor gained: 800"} {
		if evidence.ExpectedTextMatch("vigor gained: 8", observed) {
			t.Errorf("8 must not match longer number in %q", observed)
		}
	}
	for _, observed := range []string{"Akabeko tooltip rendered", ""} {
		if evidence.ExpectedTextMatch("vigor gained: 8", observed) {
			t.Errorf("must fail when absent/empty: %q", observed)
		}
	}
	// start/middle/end/whole positions
	for _, observed := range []string{"vigor gained: 8", "vigor gained: 8 in tooltip", "tooltip says vigor gained: 8"} {
		if !evidence.ExpectedTextMatch("vigor gained: 8", observed) {
			t.Errorf("must match at position: %q", observed)
		}
	}
	// empty needle is a no-op pass
	if !evidence.ExpectedTextMatch("", "whatever") || !evidence.ExpectedTextMatch("   ", "whatever") {
		t.Error("empty needle must be a no-op pass")
	}
}

// Test-GameStateJsonContainsId — deterministic JSON identity check.
func TestGameStateJSONContainsID(t *testing.T) {
	if !evidence.GameStateJSONContainsID(`{"player":{"relics":[{"id":"CLOAK_CLASP","block_gained":5}]}}`, "CLOAK_CLASP") {
		t.Error("present id must match")
	}
	if !evidence.GameStateJSONContainsID(`{"id":"cloak_clasp"}`, "CLOAK_CLASP") {
		t.Error("case-insensitive id must match")
	}
	if evidence.GameStateJSONContainsID(`{"id":"CLOAK_CLASP_PLUS"}`, "CLOAK_CLASP") {
		t.Error("must not match longer id token")
	}
	if evidence.GameStateJSONContainsID(`{"relics":[{"id":"AKABEKO"}]}`, "CLOAK_CLASP") {
		t.Error("absent id must not match")
	}
	if evidence.GameStateJSONContainsID("", "CLOAK_CLASP") || evidence.GameStateJSONContainsID(`{"id":"CLOAK_CLASP"}`, "") {
		t.Error("empty input must not match")
	}
}

// Resolve-ScreenshotNeedle — the spirelens#147 fix.
func TestResolveScreenshotNeedle(t *testing.T) {
	maps := cloakClaspRefMaps()

	needle, err := ResolveScreenshotNeedle("", "CLOAK_CLASP", maps.NameByRef, maps.CatalogIDs, "name-visible")
	if err != nil {
		t.Fatalf("ref resolution errored: %v", err)
	}
	if needle != "Cloak Clasp" {
		t.Errorf("ref id should derive display name, got %q", needle)
	}
	if !evidence.ExpectedTextMatch(needle, "Block Gained: 5 — Cloak Clasp") {
		t.Error("derived name must match the rendered text (#147 case)")
	}

	if _, err := ResolveScreenshotNeedle("CLOAK_CLASP", "", maps.NameByRef, maps.CatalogIDs, "name-visible"); err == nil || !strings.Contains(err.Error(), "resolved catalog id") {
		t.Errorf("an internal id as literal expected_text must be rejected, got %v", err)
	}

	got, err := ResolveScreenshotNeedle("Block Gained: 5", "", maps.NameByRef, maps.CatalogIDs, "value-visible")
	if err != nil || got != "Block Gained: 5" {
		t.Errorf("computed value literal must pass through, got %q err %v", got, err)
	}

	if _, err := ResolveScreenshotNeedle("Block Gained: 5", "CLOAK_CLASP", maps.NameByRef, maps.CatalogIDs, "x"); err == nil || !strings.Contains(err.Error(), "exactly one source") {
		t.Errorf("declaring both must be rejected, got %v", err)
	}
	if _, err := ResolveScreenshotNeedle("", "", maps.NameByRef, maps.CatalogIDs, "x"); err == nil || !strings.Contains(err.Error(), "binds to no authoritative source") {
		t.Errorf("declaring neither must be rejected, got %v", err)
	}
	if _, err := ResolveScreenshotNeedle("", "NOT_A_REAL_ID", maps.NameByRef, maps.CatalogIDs, "x"); err == nil || !strings.Contains(err.Error(), "matches no scenario_id_validation entry") {
		t.Errorf("unresolvable ref must be rejected, got %v", err)
	}
}

// Resolve-LiveMcpTargetId — identity binds to a catalog id.
func TestResolveLiveMcpTargetID(t *testing.T) {
	maps := cloakClaspRefMaps()

	id, err := ResolveLiveMcpTargetID("CLOAK_CLASP", maps.IDByRef, "target-present")
	if err != nil || id != "CLOAK_CLASP" {
		t.Errorf("target_id_ref must resolve to catalog id, got %q err %v", id, err)
	}
	if _, err := ResolveLiveMcpTargetID("", maps.IDByRef, "target-present"); err == nil || !strings.Contains(err.Error(), "must declare target_id_ref") {
		t.Errorf("missing target_id_ref must be rejected, got %v", err)
	}
	if _, err := ResolveLiveMcpTargetID("NOPE", maps.IDByRef, "target-present"); err == nil || !strings.Contains(err.Error(), "matches no scenario_id_validation entry") {
		t.Errorf("unresolvable target_id_ref must be rejected, got %v", err)
	}
}

// New-ScenarioRefMaps — flattens scenario_id_validation.
func TestNewScenarioRefMaps(t *testing.T) {
	maps := cloakClaspRefMaps()
	if v, _ := maps.NameByRef.Get("CLOAK_CLASP"); v != "Cloak Clasp" {
		t.Errorf("NameByRef[CLOAK_CLASP]=%q", v)
	}
	if v, _ := maps.IDByRef.Get("CLOAK_CLASP"); v != "CLOAK_CLASP" {
		t.Errorf("IDByRef[CLOAK_CLASP]=%q", v)
	}
	if !maps.CatalogIDs.Contains("CLOAK_CLASP") {
		t.Error("CatalogIDs must contain CLOAK_CLASP")
	}
	if v, _ := maps.NameByRef.Get("cloak_clasp"); v != "Cloak Clasp" {
		t.Errorf("lookups must be case-insensitive, got %q", v)
	}

	spanning := NewScenarioRefMaps(&ScenarioIDValidation{
		Cards:      []ScenarioEntry{{Field: "deck", Input: "STRIKE", ID: "STRIKE", Name: "Strike", Source: "lookup_card"}},
		Relics:     []ScenarioEntry{{Field: "add_relics", Input: "AKABEKO", ID: "AKABEKO", Name: "Akabeko", Source: "lookup_relic"}},
		Encounters: []ScenarioEntry{{Field: "next_normal_encounter", Input: "JAW_WORM", ID: "JAW_WORM", Name: "Jaw Worm", Source: "list_encounters"}},
	})
	for ref, want := range map[string]string{"STRIKE": "Strike", "AKABEKO": "Akabeko", "JAW_WORM": "Jaw Worm"} {
		if v, _ := spanning.NameByRef.Get(ref); v != want {
			t.Errorf("NameByRef[%s]=%q want %q", ref, v, want)
		}
	}

	empty := NewScenarioRefMaps(nil)
	if empty.NameByRef.Len() != 0 || empty.CatalogIDs.Len() != 0 {
		t.Error("nil validation must yield empty maps")
	}
}

func TestRequiresText(t *testing.T) {
	if !RequiresText(true, "") {
		t.Error("text_visible_required:true must require text")
	}
	for _, ms := range []string{"shows the tooltip", "the label text", "exact wording", "rendered string"} {
		if !RequiresText(false, ms) {
			t.Errorf("must_show %q must require text", ms)
		}
	}
	if RequiresText(false, "the relic is present in the run") {
		t.Error("benign must_show must not require text")
	}
	if RequiresText(false, "") {
		t.Error("empty must_show without flag must not require text")
	}
}

// End-to-end: the corrected #147 contract passes; the broken one is rejected.
func TestEndToEnd147Contract(t *testing.T) {
	maps := cloakClaspRefMaps()

	targetID, err := ResolveLiveMcpTargetID("CLOAK_CLASP", maps.IDByRef, "target-present")
	if err != nil {
		t.Fatal(err)
	}
	if !evidence.GameStateJSONContainsID(`{"player":{"relics":[{"id":"CLOAK_CLASP","block_gained":5}]}}`, targetID) {
		t.Error("identity via JSON must confirm")
	}
	valueNeedle, err := ResolveScreenshotNeedle("Block Gained: 5", "", maps.NameByRef, maps.CatalogIDs, "value-visible")
	if err != nil {
		t.Fatal(err)
	}
	if !evidence.ExpectedTextMatch(valueNeedle, "Cloak Clasp tooltip — Block Gained: 5") {
		t.Error("computed value must match")
	}
	nameNeedle, err := ResolveScreenshotNeedle("", "CLOAK_CLASP", maps.NameByRef, maps.CatalogIDs, "name-visible")
	if err != nil {
		t.Fatal(err)
	}
	if !evidence.ExpectedTextMatch(nameNeedle, "Cloak Clasp tooltip — Block Gained: 5") {
		t.Error("derived name must match")
	}

	if _, err := ResolveScreenshotNeedle("CLOAK_CLASP", "", maps.NameByRef, maps.CatalogIDs, "name-visible"); err == nil {
		t.Error("broken contract (id as on-screen text) must be structurally rejected")
	}
}
