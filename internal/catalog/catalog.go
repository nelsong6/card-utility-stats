// Package catalog holds spirelens-domain evidence-contract resolution: the
// binding rules that turn a test plan's scenario_id_validation entries into the
// authoritative screenshot needle / live_mcp identity id a verification verdict
// is checked against.
//
// These are the spirelens-specific halves of the retired
// .github/scripts/lib/EvidenceContract.ps1 (Test-EvidenceRequiresText,
// Resolve-ScreenshotNeedle, Resolve-LiveMcpTargetId) plus run-phases.ps1's
// New-ScenarioRefMaps. The generic boundary matchers they feed
// (ExpectedTextMatch / GameStateJSONContainsID) live in the SDK's
// github.com/romaine-life/glimmung/harness/evidence package; this package binds
// the spirelens degree of freedom (a free-typed expected_text) to the catalog
// so an internal id can never become a screenshot needle (spirelens#147).
package catalog

import (
	"fmt"
	"strings"
)

// CIMap is a case-insensitive string map mirroring the
// [System.StringComparer]::OrdinalIgnoreCase hashtables New-ScenarioRefMaps
// built. Keys are stored lowercased; values keep their original casing.
type CIMap struct{ m map[string]string }

// NewCIMap returns an empty case-insensitive map.
func NewCIMap() *CIMap { return &CIMap{m: map[string]string{}} }

// Set stores value under a case-insensitive key.
func (c *CIMap) Set(key, value string) { c.m[strings.ToLower(key)] = value }

// Get returns the value and whether the key was present (case-insensitive).
func (c *CIMap) Get(key string) (string, bool) { v, ok := c.m[strings.ToLower(key)]; return v, ok }

// Contains reports whether the key is present (case-insensitive).
func (c *CIMap) Contains(key string) bool { _, ok := c.m[strings.ToLower(key)]; return ok }

// Len returns the number of entries.
func (c *CIMap) Len() int { return len(c.m) }

// ScenarioEntry is one scenario_id_validation row.
type ScenarioEntry struct {
	Field  string `json:"field"`
	Input  string `json:"input"`
	ID     string `json:"id"`
	Name   string `json:"name"`
	Source string `json:"source"`
}

// ScenarioIDValidation is the test plan's scenario_id_validation block.
type ScenarioIDValidation struct {
	Passed     bool            `json:"passed"`
	Cards      []ScenarioEntry `json:"cards"`
	Relics     []ScenarioEntry `json:"relics"`
	Encounters []ScenarioEntry `json:"encounters"`
}

// RefMaps are the flattened lookup maps the evidence-binding rules need:
//
//	NameByRef : input/id -> catalog-resolved display name (screenshot needles
//	            via expected_text_ref)
//	IDByRef   : input/id -> resolved catalog id (live_mcp identity via
//	            target_id_ref)
//	CatalogIDs: every resolved id (to reject an id typed as a literal screenshot
//	            expected_text)
//
// Each entry is keyed by BOTH its input and its resolved id so a ref can name
// either; mirrors run-phases.ps1's New-ScenarioRefMaps exactly.
type RefMaps struct {
	NameByRef  *CIMap
	IDByRef    *CIMap
	CatalogIDs *CIMap
}

// NewScenarioRefMaps flattens scenario_id_validation (cards + relics +
// encounters) into the case-insensitive lookup maps. A nil validation yields
// empty maps without error, matching the PowerShell helper.
func NewScenarioRefMaps(v *ScenarioIDValidation) RefMaps {
	maps := RefMaps{NameByRef: NewCIMap(), IDByRef: NewCIMap(), CatalogIDs: NewCIMap()}
	if v == nil {
		return maps
	}
	for _, group := range [][]ScenarioEntry{v.Cards, v.Relics, v.Encounters} {
		for _, entry := range group {
			id := strings.TrimSpace(entry.ID)
			name := strings.TrimSpace(entry.Name)
			for _, key := range []string{entry.Input, entry.ID} {
				k := strings.TrimSpace(key)
				if k == "" {
					continue
				}
				if name != "" {
					maps.NameByRef.Set(k, name)
				}
				if id != "" {
					maps.IDByRef.Set(k, id)
				}
			}
			if id != "" {
				maps.CatalogIDs.Set(id, id)
			}
		}
	}
	return maps
}

// RequiresText reports whether a screenshot evidence item must prove on-screen
// text/tooltip content (and therefore must carry a resolvable text binding).
// Mirrors Test-EvidenceRequiresText: an explicit text_visible_required:true, or
// a must_show that talks about tooltip/text/label/wording/string content.
func RequiresText(textVisibleRequired bool, mustShow string) bool {
	if textVisibleRequired {
		return true
	}
	if strings.TrimSpace(mustShow) == "" {
		return false
	}
	lower := strings.ToLower(mustShow)
	for _, kw := range []string{"tooltip", "text", "label", "wording", "string"} {
		if strings.Contains(lower, kw) {
			return true
		}
	}
	return false
}

// ResolveScreenshotNeedle resolves the exact on-screen needle a text-required
// screenshot evidence item must contain, binding it to an authoritative source.
// It returns an error on a malformed contract, mirroring Resolve-ScreenshotNeedle.
//
// Binding rules (exactly one source per text-required screenshot item):
//   - expectedTextRef set -> needle DERIVED from the catalog-resolved display
//     name of the matching scenario_id_validation entry. Never the internal id.
//   - expectedText set    -> needle is the literal computed value. A literal
//     that is itself a resolved catalog id is rejected (use expected_text_ref).
//
// Declaring both, or neither, is a malformed contract.
func ResolveScreenshotNeedle(expectedText, expectedTextRef string, refToName, catalogIDs *CIMap, itemID string) (string, error) {
	hasRef := strings.TrimSpace(expectedTextRef) != ""
	hasText := strings.TrimSpace(expectedText) != ""

	if hasRef && hasText {
		return "", fmt.Errorf("screenshot evidence '%s' declares both expected_text and expected_text_ref; a text assertion must bind to exactly one source — a computed value via expected_text, or a catalog display name via expected_text_ref.", itemID)
	}
	if !hasRef && !hasText {
		return "", fmt.Errorf("screenshot evidence '%s' requires on-screen text but binds to no authoritative source. Set expected_text_ref to a scenario_id_validation id to prove a rendered entity NAME, or expected_text to a computed VALUE.", itemID)
	}

	if hasRef {
		ref := strings.TrimSpace(expectedTextRef)
		name, ok := mapGet(refToName, ref)
		if !ok {
			return "", fmt.Errorf("screenshot evidence '%s' has expected_text_ref '%s' that matches no scenario_id_validation entry. On-screen entity-name assertions must reference a catalog-resolved id so the needle binds to its display name.", itemID, ref)
		}
		if strings.TrimSpace(name) == "" {
			return "", fmt.Errorf("screenshot evidence '%s' references '%s', but that scenario_id_validation entry has no display name to bind the on-screen needle to.", itemID, ref)
		}
		return strings.TrimSpace(name), nil
	}

	text := strings.TrimSpace(expectedText)
	if catalogIDs != nil && catalogIDs.Contains(text) {
		return "", fmt.Errorf("screenshot evidence '%s' has literal expected_text '%s', which is a resolved catalog id. A screenshot needle can never be an internal id — use expected_text_ref to bind to the entity's display name, or live_mcp/get_game_state to prove the id is present in the run.", itemID, text)
	}
	return text, nil
}

// ResolveLiveMcpTargetID resolves the internal id a live_mcp identity/presence
// evidence item must prove is present in captured get_game_state JSON. It
// returns an error on a malformed contract, mirroring Resolve-LiveMcpTargetId.
func ResolveLiveMcpTargetID(targetIDRef string, refToID *CIMap, itemID string) (string, error) {
	if strings.TrimSpace(targetIDRef) == "" {
		return "", fmt.Errorf("live_mcp evidence '%s' must declare target_id_ref referencing a scenario_id_validation entry. Identity/presence is verified by the internal id appearing in get_game_state JSON, never by free text.", itemID)
	}
	ref := strings.TrimSpace(targetIDRef)
	id, ok := mapGet(refToID, ref)
	if !ok {
		return "", fmt.Errorf("live_mcp evidence '%s' has target_id_ref '%s' that matches no scenario_id_validation entry. The identity check must bind to a catalog-resolved id.", itemID, ref)
	}
	if strings.TrimSpace(id) == "" {
		return "", fmt.Errorf("live_mcp evidence '%s' references '%s', but that scenario_id_validation entry has no resolved id to confirm in get_game_state JSON.", itemID, ref)
	}
	return strings.TrimSpace(id), nil
}

func mapGet(m *CIMap, key string) (string, bool) {
	if m == nil {
		return "", false
	}
	return m.Get(key)
}
