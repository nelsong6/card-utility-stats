package verify

import (
	"github.com/romaine-life/spirelens/internal/catalog"
)

// RequiredEvidence is one test-plan required_evidence item.
type RequiredEvidence struct {
	ID                  string `json:"id"`
	Kind                string `json:"kind"`
	Required            *bool  `json:"required"`
	MustShow            string `json:"must_show"`
	TextVisibleRequired *bool  `json:"text_visible_required"`
	ExpectedText        string `json:"expected_text"`
	ExpectedTextRef     string `json:"expected_text_ref"`
	TargetIDRef         string `json:"target_id_ref"`
}

// IsRequired reports whether the item counts toward the contract. Mirrors the
// guard's `required -ne $false`: a missing or true value is required; only an
// explicit false excludes.
func (r RequiredEvidence) IsRequired() bool { return r.Required == nil || *r.Required }

// TextVisible reports the text_visible_required flag (default false).
func (r RequiredEvidence) TextVisible() bool {
	return r.TextVisibleRequired != nil && *r.TextVisibleRequired
}

// Discovery is a card/relic metadata discovery block.
type Discovery struct {
	Passed *bool  `json:"passed"`
	Status string `json:"status"`
	Notes  string `json:"notes"`
}

// ScenarioSetup is the deterministic save recipe.
type ScenarioSetup struct {
	BaseSaveName        string   `json:"base_save_name"`
	ScenarioName        string   `json:"scenario_name"`
	Deck                []string `json:"deck"`
	AddCards            []string `json:"add_cards"`
	RemoveCards         []string `json:"remove_cards"`
	Relics              []string `json:"relics"`
	AddRelics           []string `json:"add_relics"`
	RemoveRelics        []string `json:"remove_relics"`
	Gold                *int     `json:"gold"`
	CurrentHP           *int     `json:"current_hp"`
	MaxHP               *int     `json:"max_hp"`
	MaxEnergy           *int     `json:"max_energy"`
	NextNormalEncounter string   `json:"next_normal_encounter"`
	RequiredRoomType    string   `json:"required_room_type"`
	Notes               string   `json:"notes"`
}

// TestPlan is the read-only view of test-plan.json the contract check and the
// evidence guard need.
type TestPlan struct {
	Layer                  string                        `json:"layer"`
	Status                 string                        `json:"status"`
	AbortReason            string                        `json:"abort_reason"`
	Notes                  string                        `json:"notes"`
	TargetKind             string                        `json:"target_kind"`
	CardMetadataDiscovery  *Discovery                    `json:"card_metadata_discovery"`
	RelicMetadataDiscovery *Discovery                    `json:"relic_metadata_discovery"`
	ScenarioSetup          *ScenarioSetup                `json:"scenario_setup"`
	ScenarioIDValidation   *catalog.ScenarioIDValidation `json:"scenario_id_validation"`
	RequiredEvidence       []RequiredEvidence            `json:"required_evidence"`
	// Implementation-phase field.
	VerificationRequired *bool `json:"verification_required"`
}

// RequiredItems returns the required_evidence items that count (required != false).
func (tp TestPlan) RequiredItems() []RequiredEvidence {
	out := []RequiredEvidence{}
	for _, e := range tp.RequiredEvidence {
		if e.IsRequired() {
			out = append(out, e)
		}
	}
	return out
}
