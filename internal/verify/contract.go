package verify

import (
	"fmt"
	"strings"

	"github.com/romaine-life/spirelens/internal/catalog"
)

// AssertCommonStatus validates a phase result's status/abort_reason against the
// phase's allowed abort enum, mirroring the first half of Assert-PhaseContract.
func AssertCommonStatus(status, abortReason string, allowedAbort []string) error {
	if status != "pass" && status != "abort" {
		return fmt.Errorf("phase wrote invalid status %q; expected pass or abort", status)
	}
	if status == "abort" {
		if strings.TrimSpace(abortReason) == "" {
			return fmt.Errorf("phase aborted without abort_reason")
		}
		if !contains(allowedAbort, abortReason) {
			return fmt.Errorf("phase used abort_reason %q, outside allowed enum", abortReason)
		}
	}
	return nil
}

// AssertTestPlanContract validates a passing test-plan result, mirroring the
// test_plan branch of Assert-PhaseContract (scenario_setup, scenario_id_validation,
// per-id coverage, metadata discovery, non-empty required_evidence, and fail-fast
// evidence-needle binding).
func AssertTestPlanContract(plan *TestPlan) error {
	if plan.ScenarioSetup == nil {
		return fmt.Errorf("test planning phase pass result must include scenario_setup")
	}
	if plan.ScenarioIDValidation == nil || !plan.ScenarioIDValidation.Passed {
		return fmt.Errorf("test planning phase pass result must include scenario_id_validation.passed=true for every scenario card/relic/encounter id")
	}
	if err := assertScenarioIDValidationEntries(plan.ScenarioSetup, plan.ScenarioIDValidation); err != nil {
		return err
	}

	if scenarioSetupHasCards(plan.ScenarioSetup) {
		if plan.CardMetadataDiscovery == nil || plan.CardMetadataDiscovery.Passed == nil || !*plan.CardMetadataDiscovery.Passed {
			return fmt.Errorf("test planning phase pass result must include card_metadata_discovery.passed=true when scenario_setup contains card ids")
		}
	}
	if scenarioSetupHasRelics(plan.ScenarioSetup) {
		if plan.RelicMetadataDiscovery == nil || plan.RelicMetadataDiscovery.Passed == nil || !*plan.RelicMetadataDiscovery.Passed {
			return fmt.Errorf("test planning phase pass result must include relic_metadata_discovery.passed=true when scenario_setup contains relic ids")
		}
	}

	required := plan.RequiredItems()
	if len(required) == 0 {
		return fmt.Errorf("test planning phase pass result must include non-empty required_evidence")
	}
	refMaps := catalog.NewScenarioRefMaps(plan.ScenarioIDValidation)
	for _, item := range required {
		if strings.TrimSpace(item.ID) == "" || strings.TrimSpace(item.Kind) == "" || strings.TrimSpace(item.MustShow) == "" {
			return fmt.Errorf("test planning required_evidence items must include id, kind, and must_show")
		}
		switch item.Kind {
		case "screenshot":
			if catalog.RequiresText(item.TextVisible(), item.MustShow) {
				if _, err := catalog.ResolveScreenshotNeedle(item.ExpectedText, item.ExpectedTextRef, refMaps.NameByRef, refMaps.CatalogIDs, item.ID); err != nil {
					return err
				}
			}
		case "live_mcp":
			if _, err := catalog.ResolveLiveMcpTargetID(item.TargetIDRef, refMaps.IDByRef, item.ID); err != nil {
				return err
			}
		}
	}
	return nil
}

// AssertImplementationContract validates a passing implementation result,
// mirroring the implementation branch of Assert-PhaseContract.
func AssertImplementationContract(verificationRequired *bool) error {
	if verificationRequired == nil {
		return fmt.Errorf("implementation phase pass result must include boolean verification_required")
	}
	return nil
}

// assertScenarioIDValidationEntries mirrors Assert-ScenarioIdValidationEntries:
// every scenario_setup id must have a matching scenario_id_validation entry whose
// resolved id equals the input (normalized by stripping the CARD./RELIC./ENCOUNTER.
// prefix and uppercasing).
func assertScenarioIDValidationEntries(setup *ScenarioSetup, v *catalog.ScenarioIDValidation) error {
	check := func(expected []string, kind, field, prefix string, entries []catalog.ScenarioEntry) error {
		if len(expected) == 0 {
			return nil
		}
		for _, value := range expected {
			text := strings.TrimSpace(value)
			if text == "" {
				continue
			}
			var matching *catalog.ScenarioEntry
			for i := range entries {
				e := &entries[i]
				if e.Field == field && e.Input == value && strings.TrimSpace(e.ID) != "" && strings.TrimSpace(e.Source) != "" {
					matching = e
					break
				}
			}
			if matching == nil {
				return fmt.Errorf("scenario_id_validation.%s is missing validated entry for scenario_setup.%s value '%s'.", kind, field, value)
			}
			if normalizeCatalogID(value, prefix) != normalizeCatalogID(matching.ID, prefix) {
				return fmt.Errorf("scenario_setup.%s value '%s' must already be the exact resolved catalog id, but scenario_id_validation.%s resolved it to '%s'.", field, value, kind, matching.ID)
			}
		}
		return nil
	}

	for _, c := range []struct {
		expected []string
		field    string
	}{
		{setup.Deck, "deck"},
		{setup.AddCards, "add_cards"},
		{setup.RemoveCards, "remove_cards"},
	} {
		if err := check(c.expected, "cards", c.field, "CARD", v.Cards); err != nil {
			return err
		}
	}
	for _, c := range []struct {
		expected []string
		field    string
	}{
		{setup.Relics, "relics"},
		{setup.AddRelics, "add_relics"},
		{setup.RemoveRelics, "remove_relics"},
	} {
		if err := check(c.expected, "relics", c.field, "RELIC", v.Relics); err != nil {
			return err
		}
	}

	encounter := strings.TrimSpace(setup.NextNormalEncounter)
	if encounter != "" {
		var matching *catalog.ScenarioEntry
		for i := range v.Encounters {
			e := &v.Encounters[i]
			if e.Field == "next_normal_encounter" && e.Input == setup.NextNormalEncounter && strings.TrimSpace(e.ID) != "" && strings.TrimSpace(e.Source) != "" {
				matching = e
				break
			}
		}
		if matching == nil {
			return fmt.Errorf("scenario_id_validation.encounters is missing validated entry for scenario_setup.next_normal_encounter value '%s'.", setup.NextNormalEncounter)
		}
		if normalizeCatalogID(setup.NextNormalEncounter, "ENCOUNTER") != normalizeCatalogID(matching.ID, "ENCOUNTER") {
			return fmt.Errorf("scenario_setup.next_normal_encounter value '%s' must already be the exact resolved catalog id, but scenario_id_validation.encounters resolved it to '%s'.", setup.NextNormalEncounter, matching.ID)
		}
	}
	return nil
}

func normalizeCatalogID(value, prefix string) string {
	text := strings.ToUpper(strings.TrimSpace(value))
	marker := strings.ToUpper(prefix) + "."
	if strings.HasPrefix(text, marker) {
		return text[len(marker):]
	}
	return text
}

func scenarioSetupHasCards(s *ScenarioSetup) bool {
	return hasNonEmpty(s.Deck) || hasNonEmpty(s.AddCards) || hasNonEmpty(s.RemoveCards)
}

func scenarioSetupHasRelics(s *ScenarioSetup) bool {
	return hasNonEmpty(s.Relics) || hasNonEmpty(s.AddRelics) || hasNonEmpty(s.RemoveRelics)
}

func hasNonEmpty(values []string) bool {
	for _, v := range values {
		if strings.TrimSpace(v) != "" {
			return true
		}
	}
	return false
}

func contains(values []string, target string) bool {
	for _, v := range values {
		if v == target {
			return true
		}
	}
	return false
}
