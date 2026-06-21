// Package verify holds spirelens's deterministic evidence guard, the per-phase
// contract checks, and the harness-owned unit-test stamping — the load-bearing
// logic ported from run-phases.ps1's Apply-VerificationEvidenceGuard /
// Assert-PhaseContract / Set-AuthoritativeUnitTestResult /
// Write-DeterminedUnitTestFailure.
//
// The verdict is serialized through the SDK: WriteVerdict (write.go) builds a
// harness/verification.Verification and calls verification.WriteFinalizable —
// there is no spirelens-local writer fork. Doc remains only as the in-memory
// working model the guard derives its decision from: it must read-mutate-write
// arbitrary nested producer-domain fields (screenshot_validation, the rich
// per-row evidence_results, retryable, human_action_required, …) that the SDK's
// typed Verification deliberately does not carry, so a lossless map is the right
// in-flight representation. Only at write time does WriteVerdict reduce that
// in-memory verdict onto the finalizer-read shape.
package verify

import "strings"

// Doc is the in-memory verification verdict as a lossless ordered-by-key map,
// mirroring run-phases.ps1's PSCustomObject access (Get-PropertyValue /
// Set-PropertyValue). Modeling it as a map preserves any field the agent wrote
// through the guard's read-mutate-write round-trip, exactly as the PowerShell
// did; WriteVerdict serializes it through the SDK finalizable writer.
type Doc map[string]any

// Str returns a string field, empty when absent or not a string.
func (d Doc) Str(key string) string {
	if v, ok := d[key]; ok {
		if s, ok := v.(string); ok {
			return s
		}
	}
	return ""
}

// Bool returns whether key is present and equal to the boolean true (the
// PowerShell `-eq $true` test: a missing or non-true value is false).
func (d Doc) Bool(key string) bool {
	if v, ok := d[key]; ok {
		if b, ok := v.(bool); ok {
			return b
		}
	}
	return false
}

// Sub returns a nested object as a Doc, nil when absent.
func (d Doc) Sub(key string) Doc {
	if v, ok := d[key]; ok {
		if m, ok := v.(map[string]any); ok {
			return Doc(m)
		}
		if m, ok := v.(Doc); ok {
			return m
		}
	}
	return nil
}

// Rows returns key as a slice of object rows (e.g. evidence_results).
func (d Doc) Rows(key string) []Doc {
	v, ok := d[key]
	if !ok {
		return nil
	}
	arr, ok := v.([]any)
	if !ok {
		return nil
	}
	out := make([]Doc, 0, len(arr))
	for _, e := range arr {
		if m, ok := e.(map[string]any); ok {
			out = append(out, Doc(m))
		} else if m, ok := e.(Doc); ok {
			out = append(out, m)
		}
	}
	return out
}

// Set writes a field.
func (d Doc) Set(key string, value any) { d[key] = value }

// IntField returns an integer field, coercing JSON float64, with a fallback.
func (d Doc) IntField(key string, fallback int) int {
	switch v := d[key].(type) {
	case float64:
		return int(v)
	case int:
		return v
	}
	return fallback
}

// joinNotes appends note to existing, space-separated, skipping blanks — mirrors
// the guard's notes concatenation.
func joinNotes(existing, note string) string {
	parts := []string{}
	for _, p := range []string{strings.TrimSpace(existing), strings.TrimSpace(note)} {
		if p != "" {
			parts = append(parts, p)
		}
	}
	return strings.Join(parts, " ")
}
