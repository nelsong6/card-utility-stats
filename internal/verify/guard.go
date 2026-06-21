package verify

import (
	"fmt"
	"regexp"
	"strings"

	"github.com/romaine-life/glimmung/harness/evidence"
	"github.com/romaine-life/spirelens/internal/catalog"
)

// unavailableEvidenceRE ports Test-TextMentionsUnavailableEvidence: notes that
// claim required visual evidence was unavailable or only covered by unit tests.
var unavailableEvidenceRE = regexp.MustCompile(`(?i)(not achievable|not available|unavailable|not renderable|cannot render|cannot be made visible|without mouse hover|no hover support|unit tests? (directly |exclusively |fully )?verif|verified exclusively through unit tests)`)

// TextMentionsUnavailableEvidence reports whether text claims evidence was
// unavailable / unit-test-only. Mirrors Test-TextMentionsUnavailableEvidence.
func TextMentionsUnavailableEvidence(text string) bool {
	if strings.TrimSpace(text) == "" {
		return false
	}
	return unavailableEvidenceRE.MatchString(text)
}

// ApplyEvidenceGuard re-derives the verification verdict deterministically from
// the test-plan evidence contract, regardless of what the agent self-reported.
// It is the Go port of Apply-VerificationEvidenceGuard: a non-pass result is
// returned unchanged; a passing result is downgraded to an attributed abort the
// moment any required evidence is missing, unbound, or not observed.
//
// readArtifact reads the concatenated text of evidence artifact files (the
// captured get_game_state JSON), resolving each declared path; it is injected so
// the guard's logic is testable without disk (the production reader lives in the
// host run-phase).
func ApplyEvidenceGuard(result Doc, plan *TestPlan, readArtifact func(paths []string) string) Doc {
	if result.Str("status") != "pass" {
		return result
	}

	if plan == nil {
		return setGuardAbort(result, "artifact_contract_missing", "Verification cannot pass because the test-plan evidence contract is missing.")
	}

	required := plan.RequiredItems()
	if len(required) == 0 {
		return setGuardAbort(result, "artifact_contract_missing", "Verification cannot pass because test planning did not declare required_evidence. The verifier needs an explicit evidence contract before it can pass.")
	}

	evidenceResults := result.Rows("evidence_results")
	if len(evidenceResults) == 0 {
		return setGuardAbort(result, "artifact_contract_missing", "Verification cannot pass because it did not write evidence_results for the test-plan evidence contract.")
	}

	refMaps := catalog.NewScenarioRefMaps(plan.ScenarioIDValidation)
	screenshotValidation := result.Sub("screenshot_validation")
	allNotes := joinBlob(result.Str("notes"), docStr(screenshotValidation, "notes"))

	for _, req := range required {
		id := strings.TrimSpace(req.ID)
		if id == "" {
			return setGuardAbort(result, "artifact_contract_missing", "Verification cannot pass because a required_evidence item has no id.")
		}

		var match Doc
		for _, row := range evidenceResults {
			if row.Str("evidence_id") == id {
				match = row
				break
			}
		}
		if match == nil {
			return setGuardAbort(result, "artifact_contract_missing", fmt.Sprintf("Verification cannot pass because evidence_results does not include required evidence '%s'.", id))
		}
		if !match.Bool("passed") {
			return setGuardAbort(result, "claimed_result_not_observed", fmt.Sprintf("Verification cannot pass because required evidence '%s' was not marked passed.", id))
		}

		switch req.Kind {
		case "screenshot":
			artifactPaths := stringSlice(match, "artifact_paths")
			if len(artifactPaths) == 0 {
				return setGuardAbort(result, "screenshot_missing", fmt.Sprintf("Verification cannot pass because screenshot evidence '%s' has no artifact_paths.", id))
			}
			if !match.Bool("target_visible") {
				return setGuardAbort(result, "screenshot_not_relevant", fmt.Sprintf("Verification cannot pass because screenshot evidence '%s' does not explicitly mark target_visible=true.", id))
			}
			if catalog.RequiresText(req.TextVisible(), req.MustShow) {
				if !match.Bool("text_visible") {
					return setGuardAbort(result, "target_evidence_missing", fmt.Sprintf("Verification cannot pass because screenshot evidence '%s' must show text/tooltip content, but evidence_results did not mark text_visible=true.", id))
				}
				observedText := match.Str("observed_text")
				if strings.TrimSpace(observedText) == "" {
					return setGuardAbort(result, "target_evidence_missing", fmt.Sprintf("Verification cannot pass because screenshot evidence '%s' must show text/tooltip content, but observed_text is empty.", id))
				}
				needle, err := catalog.ResolveScreenshotNeedle(req.ExpectedText, req.ExpectedTextRef, refMaps.NameByRef, refMaps.CatalogIDs, id)
				if err != nil {
					return setGuardAbort(result, "artifact_contract_missing", fmt.Sprintf("Verification cannot pass because the test-plan evidence contract is malformed: %s", err.Error()))
				}
				if !evidence.ExpectedTextMatch(needle, observedText) {
					haystack := strings.TrimSpace(observedText)
					return setGuardAbort(result, "claimed_result_not_observed", fmt.Sprintf("Verification cannot pass because screenshot evidence '%s' requires on-screen text \"%s\" but observed_text=\"%s\" — the exact value the test plan bound this screenshot to was not present at an alphanumeric boundary in the screenshot the verifier captured.", id, needle, haystack))
				}
			}
		case "live_mcp":
			targetID, err := catalog.ResolveLiveMcpTargetID(req.TargetIDRef, refMaps.IDByRef, id)
			if err != nil {
				return setGuardAbort(result, "artifact_contract_missing", fmt.Sprintf("Verification cannot pass because the test-plan evidence contract is malformed: %s", err.Error()))
			}
			artifactPaths := stringSlice(match, "artifact_paths")
			if len(artifactPaths) == 0 {
				return setGuardAbort(result, "target_evidence_missing", fmt.Sprintf("Verification cannot pass because live_mcp evidence '%s' has no artifact_paths; the captured get_game_state JSON proving id '%s' is present in the run is missing.", id, targetID))
			}
			gameStateJSON := ""
			if readArtifact != nil {
				gameStateJSON = readArtifact(artifactPaths)
			}
			if strings.TrimSpace(gameStateJSON) == "" {
				return setGuardAbort(result, "target_evidence_missing", fmt.Sprintf("Verification cannot pass because live_mcp evidence '%s' artifact_paths did not resolve to any readable get_game_state JSON file for id '%s'.", id, targetID))
			}
			if !evidence.GameStateJSONContainsID(gameStateJSON, targetID) {
				return setGuardAbort(result, "mcp_state_mismatch", fmt.Sprintf("Verification cannot pass because the captured get_game_state JSON for live_mcp evidence '%s' does not contain the resolved id '%s' at an id-token boundary — identity/presence could not be confirmed in the live run state.", id, targetID))
			}
		}
	}

	// Aggregate screenshot gate: when any screenshot evidence is required, the
	// screenshot_validation block must show passed=true, target_visible=true,
	// count>=1, and the notes must not claim evidence was unavailable.
	hasScreenshot := false
	for _, req := range required {
		if req.Kind == "screenshot" {
			hasScreenshot = true
			break
		}
	}
	if hasScreenshot {
		if !screenshotValidation.Bool("passed") || !screenshotValidation.Bool("target_visible") || screenshotValidation.IntField("count", 0) < 1 {
			return setGuardAbort(result, "screenshot_not_relevant", "Verification cannot pass because screenshot_validation does not show passed=true, target_visible=true, and count >= 1 for required screenshot evidence.")
		}
		if TextMentionsUnavailableEvidence(allNotes) {
			return setGuardAbort(result, "target_evidence_missing", "Verification cannot pass because its notes say required visual evidence was unavailable or only covered by unit tests.")
		}
	}

	return result
}

// setGuardAbort downgrades the verdict to an attributed abort, mirroring
// Set-VerificationGuardAbort (status/abort_reason/retryable/human_action_required,
// notes concatenation, and the screenshot_validation mutation for the relevant
// abort reasons).
func setGuardAbort(result Doc, abortReason, guardNote string) Doc {
	result.Set("status", "abort")
	result.Set("abort_reason", abortReason)
	result.Set("retryable", true)
	result.Set("human_action_required", false)
	result.Set("notes", joinNotes(result.Str("notes"), guardNote))

	if sv := result.Sub("screenshot_validation"); sv != nil {
		switch abortReason {
		case "screenshot_not_relevant", "target_evidence_missing", "artifact_contract_missing":
			sv.Set("passed", false)
			sv.Set("status", "abort")
			sv.Set("notes", joinNotes(sv.Str("notes"), guardNote))
			if abortReason != "artifact_contract_missing" {
				sv.Set("target_visible", false)
			}
		}
	}
	return result
}

// GuardMarkdown builds the verification.md body Set-VerificationGuardAbort wrote.
func GuardMarkdown(abortReason, guardNote string) string {
	return fmt.Sprintf("Status: abort\n\nAbort reason: %s\n\n%s\n", abortReason, guardNote)
}

func docStr(d Doc, key string) string {
	if d == nil {
		return ""
	}
	return d.Str(key)
}

// joinBlob joins non-empty values with newlines, mirroring Get-TextBlob.
func joinBlob(values ...string) string {
	parts := []string{}
	for _, v := range values {
		if strings.TrimSpace(v) != "" {
			parts = append(parts, v)
		}
	}
	return strings.Join(parts, "\n")
}

func stringSlice(d Doc, key string) []string {
	v, ok := d[key]
	if !ok {
		return nil
	}
	arr, ok := v.([]any)
	if !ok {
		return nil
	}
	out := make([]string, 0, len(arr))
	for _, e := range arr {
		if s, ok := e.(string); ok {
			out = append(out, s)
		}
	}
	return out
}
