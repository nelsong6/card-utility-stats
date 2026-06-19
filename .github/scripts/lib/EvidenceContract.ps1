Set-StrictMode -Version 3.0

# Pure, dot-sourceable evidence-contract resolution for the verification guard.
#
# WHY THIS FILE EXISTS
# --------------------
# In run spirelens#147/runs/1.1 the verification phase FALSELY denied a correct
# implementation. The test plan authored a screenshot evidence item with
# `expected_text: "CLOAK_CLASP"` — the internal relic *id* — while the SpireLens
# UI renders the *display name* "Cloak Clasp". The deterministic guard's exact
# alphanumeric-boundary match (correctly, and deliberately) refused to treat
# `CLOAK_CLASP` as present in `Cloak Clasp`, so the run aborted with
# `claimed_result_not_observed`. The guard did exactly what it was told; the
# contract it was handed was wrong.
#
# The root cause is a degree of freedom: `expected_text` was a free string the
# test-plan author typed, so it could diverge from what the game renders. This
# library REMOVES that degree of freedom by construction. A screenshot evidence
# item's on-screen needle is now bound to an authoritative source:
#
#   * ENTITY NAME on screen  -> `expected_text_ref` referencing a
#     scenario_id_validation entry; the harness DERIVES the needle from that
#     entry's catalog-resolved display `name` (e.g. "Cloak Clasp"), never the id.
#   * COMPUTED VALUE          -> `expected_text` literal (e.g. "Block Gained: 5"),
#     exactly as before. The exact-boundary match is unchanged so a buggy
#     `vigor gained: 88` can never satisfy `vigor gained: 8` (PR #170).
#   * IDENTITY / presence     -> NOT a screenshot concern at all. It is a
#     `live_mcp` evidence item whose `target_id_ref` resolves to the internal id,
#     which the guard confirms LITERALLY appears in the captured get_game_state
#     JSON artifact — not merely that the agent set `passed=true`.
#
# Resulting invariant, true by construction: a screenshot evidence item's
# resolved needle can never be an internal id. Identity lives in the JSON
# channel; the screenshot channel's needle is the resolved catalog name or the
# computed value.
#
# The functions here are intentionally pure — no script-scope closures, no
# dependency on $IssueNumber / $ValidationArtifactDir / any caller state — so the
# Pester suite dot-sources THIS file and exercises the real production code
# rather than a mirror re-implementation (the smell that let prior harness bugs
# ship). run-phases.ps1 dot-sources it too; there is one definition of the
# binding rules, used by both the test-plan contract check and the verification
# guard.

function Test-EvidenceRequiresText {
    <#
    .SYNOPSIS
        True when a screenshot evidence item must prove on-screen text/tooltip
        content (and therefore must carry a resolvable text binding).
    .DESCRIPTION
        Mirrors the guard's historical trigger: an explicit
        `text_visible_required:true`, or a `must_show` that talks about
        tooltip/text/label/wording/string content.
    #>
    param(
        [object]$TextVisibleRequired,
        [AllowNull()][AllowEmptyString()][string]$MustShow
    )
    if ($TextVisibleRequired -eq $true) { return $true }
    if (-not [string]::IsNullOrWhiteSpace($MustShow) -and $MustShow -match '(?i)tooltip|text|label|wording|string') {
        return $true
    }
    return $false
}

function Resolve-ScreenshotNeedle {
    <#
    .SYNOPSIS
        Resolve the exact on-screen needle a text-required screenshot evidence
        item must contain, binding it to an authoritative source. Throws on a
        malformed contract.
    .DESCRIPTION
        Binding rules (exactly one source per text-required screenshot item):
          * `expected_text_ref` set -> the needle is DERIVED from the
            catalog-resolved display name of the matching scenario_id_validation
            entry. Never the internal id.
          * `expected_text` set      -> the needle is the literal computed value.
            A literal that is itself a resolved catalog id is rejected: entity
            references must go through `expected_text_ref` so the needle binds to
            the display name, not the id. (This is exact equality against the
            ids resolved for THIS scenario — not a heuristic id-shape lint.)
        Declaring both, or neither, is a malformed contract.
    .PARAMETER RefToName
        Case-insensitive hashtable mapping a scenario_id_validation input/id to
        its catalog-resolved display name.
    .PARAMETER CatalogIds
        Case-insensitive hashtable whose KEYS are every resolved catalog id in
        the scenario (value ignored); used to reject an id typed as a literal
        expected_text.
    .OUTPUTS
        [string] the resolved needle.
    #>
    param(
        [AllowNull()][AllowEmptyString()][string]$ExpectedText,
        [AllowNull()][AllowEmptyString()][string]$ExpectedTextRef,
        [hashtable]$RefToName,
        [hashtable]$CatalogIds,
        [string]$ItemId
    )

    $hasRef = -not [string]::IsNullOrWhiteSpace($ExpectedTextRef)
    $hasText = -not [string]::IsNullOrWhiteSpace($ExpectedText)

    if ($hasRef -and $hasText) {
        throw "screenshot evidence '$ItemId' declares both expected_text and expected_text_ref; a text assertion must bind to exactly one source — a computed value via expected_text, or a catalog display name via expected_text_ref."
    }
    if (-not $hasRef -and -not $hasText) {
        throw "screenshot evidence '$ItemId' requires on-screen text but binds to no authoritative source. Set expected_text_ref to a scenario_id_validation id to prove a rendered entity NAME, or expected_text to a computed VALUE."
    }

    if ($hasRef) {
        $ref = $ExpectedTextRef.Trim()
        if ($null -eq $RefToName -or -not $RefToName.ContainsKey($ref)) {
            throw "screenshot evidence '$ItemId' has expected_text_ref '$ref' that matches no scenario_id_validation entry. On-screen entity-name assertions must reference a catalog-resolved id so the needle binds to its display name."
        }
        $name = [string]$RefToName[$ref]
        if ([string]::IsNullOrWhiteSpace($name)) {
            throw "screenshot evidence '$ItemId' references '$ref', but that scenario_id_validation entry has no display name to bind the on-screen needle to."
        }
        return $name.Trim()
    }

    $text = $ExpectedText.Trim()
    if ($null -ne $CatalogIds -and $CatalogIds.ContainsKey($text)) {
        throw "screenshot evidence '$ItemId' has literal expected_text '$text', which is a resolved catalog id. A screenshot needle can never be an internal id — use expected_text_ref to bind to the entity's display name, or live_mcp/get_game_state to prove the id is present in the run."
    }
    return $text
}

function Resolve-LiveMcpTargetId {
    <#
    .SYNOPSIS
        Resolve the internal id a live_mcp identity/presence evidence item must
        prove is present in captured get_game_state JSON. Throws on a malformed
        contract.
    .DESCRIPTION
        Identity is verified through the JSON channel where the internal id
        appears verbatim, never through screenshot text. The item must carry a
        `target_id_ref` referencing a scenario_id_validation entry; the needle is
        DERIVED from that entry's resolved id.
    .PARAMETER RefToId
        Case-insensitive hashtable mapping a scenario_id_validation input/id to
        its resolved catalog id.
    .OUTPUTS
        [string] the resolved internal id.
    #>
    param(
        [AllowNull()][AllowEmptyString()][string]$TargetIdRef,
        [hashtable]$RefToId,
        [string]$ItemId
    )

    if ([string]::IsNullOrWhiteSpace($TargetIdRef)) {
        throw "live_mcp evidence '$ItemId' must declare target_id_ref referencing a scenario_id_validation entry. Identity/presence is verified by the internal id appearing in get_game_state JSON, never by free text."
    }
    $ref = $TargetIdRef.Trim()
    if ($null -eq $RefToId -or -not $RefToId.ContainsKey($ref)) {
        throw "live_mcp evidence '$ItemId' has target_id_ref '$ref' that matches no scenario_id_validation entry. The identity check must bind to a catalog-resolved id."
    }
    $id = [string]$RefToId[$ref]
    if ([string]::IsNullOrWhiteSpace($id)) {
        throw "live_mcp evidence '$ItemId' references '$ref', but that scenario_id_validation entry has no resolved id to confirm in get_game_state JSON."
    }
    return $id.Trim()
}

function Test-ExpectedTextMatch {
    <#
    .SYNOPSIS
        The load-bearing exact-boundary needle match (case-insensitive).
    .DESCRIPTION
        The needle must appear in the observed text wrapped in non-alphanumeric
        boundaries, so a needle ending in a digit cannot match a longer number:
        `vigor gained: 8` must NOT match `vigor gained: 88` (PR #170 failure
        mode). An empty/whitespace needle is a no-op pass — callers that require
        a needle enforce its presence upstream (Resolve-ScreenshotNeedle).
    #>
    param(
        [AllowNull()][AllowEmptyString()][string]$ExpectedText,
        [AllowNull()][AllowEmptyString()][string]$ObservedText
    )
    if ([string]::IsNullOrWhiteSpace($ExpectedText)) { return $true }
    $needle = $ExpectedText.Trim()
    $haystack = ([string]$ObservedText).Trim()
    $pattern = '(?:^|[^A-Za-z0-9])' + [regex]::Escape($needle) + '(?:[^A-Za-z0-9]|$)'
    return [regex]::IsMatch($haystack, $pattern, [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
}

function Test-GameStateJsonContainsId {
    <#
    .SYNOPSIS
        True when an internal id appears verbatim in captured get_game_state
        JSON, at id-token boundaries.
    .DESCRIPTION
        Ids are alphanumeric plus underscore. The id must appear bounded by a
        non-[A-Za-z0-9_] character (or string ends) so `CLOAK_CLASP` does not
        match a longer token such as `CLOAK_CLASP_PLUS`. Case-insensitive so a
        canonical-id casing difference does not cause a false denial, but
        boundary-strict so presence is real, not a prefix coincidence.
    #>
    param(
        [AllowNull()][AllowEmptyString()][string]$Json,
        [string]$Id
    )
    if ([string]::IsNullOrWhiteSpace($Json) -or [string]::IsNullOrWhiteSpace($Id)) { return $false }
    $needle = $Id.Trim()
    $pattern = '(?<![A-Za-z0-9_])' + [regex]::Escape($needle) + '(?![A-Za-z0-9_])'
    return [regex]::IsMatch($Json, $pattern, [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
}
