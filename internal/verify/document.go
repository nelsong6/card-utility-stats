// Package verify holds spirelens's verification document model, the deterministic
// evidence guard, the per-phase contract checks, and the harness-owned unit-test
// stamping — the load-bearing logic ported from run-phases.ps1's
// Apply-VerificationEvidenceGuard / Assert-PhaseContract /
// Set-AuthoritativeUnitTestResult / Write-DeterminedUnitTestFailure.
//
// The verification.json this package writes is spirelens's rich domain shape
// (layer / unit_tests / live_mcp_validation / screenshot_validation / rich
// evidence_results rows). The glimmung verification_finalize primitive reads
// only .status / .abort_reason / .evidence_results[].kind/.passed / .reasons /
// .notes and PRESERVES every other field, so the rich shape feeds the recycle
// context and human review intact. The SDK's harness/verification.Verification
// struct is intentionally narrow and cannot carry these producer-domain fields,
// so this package writes the document itself while reusing the SDK's status
// enum, artifacts-dir convention, and validation rules (see write.go). That SDK
// limitation is flagged for the hub.
package verify

import "strings"

// Doc is a verification.json document as a lossless ordered-by-key map, mirroring
// run-phases.ps1's PSCustomObject access (Get-PropertyValue / Set-PropertyValue).
// Modeling it as a map preserves any field the agent wrote through the guard's
// read-mutate-write round-trip, exactly as the PowerShell did.
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
