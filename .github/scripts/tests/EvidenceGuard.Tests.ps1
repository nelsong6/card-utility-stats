#Requires -Modules @{ ModuleName = 'Pester'; ModuleVersion = '5.0.0' }

# Regression tests for the verification evidence contract.
#
# These exercise the REAL production code, not a mirror re-implementation:
#   - the pure binding rules are dot-sourced straight from
#     lib/EvidenceContract.ps1 (the same file run-phases.ps1 dot-sources), so a
#     change to the matcher or the needle-binding rules is caught here instead of
#     several phases deep into a live verify run;
#   - the run-phases.ps1 helpers that feed those rules (New-ScenarioRefMaps,
#     Read-EvidenceArtifactText) are AST-extracted from the live source via
#     Helpers.psm1, so they stay in lockstep with production.
#
# The two failure modes this contract must never regress:
#   * spirelens#147 — an internal id (CLOAK_CLASP) authored as a screenshot's
#     expected_text false-denied a correct feature whose UI renders the display
#     name "Cloak Clasp". Identity now lives in the live_mcp/JSON channel; the
#     screenshot needle binds to the catalog NAME or a computed VALUE, never an
#     id.
#   * PR #170 — `vigor gained: 8` must never match an observed `vigor gained: 88`.

BeforeAll {
    Set-StrictMode -Version 3.0
    $ErrorActionPreference = 'Stop'

    # Real binding rules — dot-sourced from the production library.
    . (Join-Path $PSScriptRoot '..' 'lib' 'EvidenceContract.ps1')

    # Real run-phases helpers — extracted from the live script via AST.
    Import-Module (Join-Path $PSScriptRoot 'Helpers.psm1') -Force
    $scriptPath = Join-Path $PSScriptRoot '..' 'run-phases.ps1'
    $source = Import-ScriptFunctions -ScriptPath $scriptPath -FunctionNames @(
        'Get-PropertyValue', 'Set-PropertyValue', 'ConvertTo-Array',
        'New-ScenarioRefMaps', 'Read-EvidenceArtifactText'
    )
    . ([scriptblock]::Create($source))

    # Helper: build the ref maps for a realistic Cloak Clasp scenario plan.
    function New-CloakClaspRefMaps {
        $scenarioIdValidation = [pscustomobject]@{
            passed = $true
            cards  = @()
            relics = @(
                [pscustomobject]@{ field = 'add_relics'; input = 'CLOAK_CLASP'; id = 'CLOAK_CLASP'; name = 'Cloak Clasp'; source = 'lookup_relic' }
            )
            encounters = @()
        }
        return (New-ScenarioRefMaps -ScenarioIdValidation $scenarioIdValidation)
    }
}

Describe 'Test-ExpectedTextMatch — numeric exactness (PR #170 guard, unchanged)' {
    It 'passes when observed contains expected at a boundary (case-insensitive)' {
        Test-ExpectedTextMatch -ExpectedText 'vigor gained: 8' -ObservedText 'Akabeko vigor gained: 8' | Should -BeTrue
        Test-ExpectedTextMatch -ExpectedText 'Vigor Gained: 8' -ObservedText 'akabeko vigor gained: 8' | Should -BeTrue
    }
    It 'fails when observed has a longer number — 8 must not match 88/80/800' {
        Test-ExpectedTextMatch -ExpectedText 'vigor gained: 8' -ObservedText 'Akabeko vigor gained: 88' | Should -BeFalse
        Test-ExpectedTextMatch -ExpectedText 'vigor gained: 8' -ObservedText 'vigor gained: 80'  | Should -BeFalse
        Test-ExpectedTextMatch -ExpectedText 'vigor gained: 8' -ObservedText 'vigor gained: 800' | Should -BeFalse
    }
    It 'fails when the text is absent or observed is empty' {
        Test-ExpectedTextMatch -ExpectedText 'vigor gained: 8' -ObservedText 'Akabeko tooltip rendered' | Should -BeFalse
        Test-ExpectedTextMatch -ExpectedText 'vigor gained: 8' -ObservedText ''    | Should -BeFalse
        Test-ExpectedTextMatch -ExpectedText 'vigor gained: 8' -ObservedText $null  | Should -BeFalse
    }
    It 'matches at start, middle, end, and whole-string positions' {
        Test-ExpectedTextMatch -ExpectedText 'vigor gained: 8' -ObservedText 'vigor gained: 8'           | Should -BeTrue
        Test-ExpectedTextMatch -ExpectedText 'vigor gained: 8' -ObservedText 'vigor gained: 8 in tooltip' | Should -BeTrue
        Test-ExpectedTextMatch -ExpectedText 'vigor gained: 8' -ObservedText 'tooltip says vigor gained: 8' | Should -BeTrue
    }
    It 'is a no-op when the needle is empty (presence is enforced upstream)' {
        Test-ExpectedTextMatch -ExpectedText ''    -ObservedText 'whatever' | Should -BeTrue
        Test-ExpectedTextMatch -ExpectedText '   ' -ObservedText 'whatever' | Should -BeTrue
    }
}

Describe 'Resolve-ScreenshotNeedle — the spirelens#147 fix (id can never be a screenshot needle)' {
    BeforeEach { $maps = New-CloakClaspRefMaps }

    It 'derives the catalog display NAME from expected_text_ref (ref id -> "Cloak Clasp")' {
        $needle = Resolve-ScreenshotNeedle -ExpectedTextRef 'CLOAK_CLASP' -ExpectedText '' -RefToName $maps.NameByRef -CatalogIds $maps.CatalogIds -ItemId 'name-visible'
        $needle | Should -BeExactly 'Cloak Clasp'
        # And the derived name matches the real on-screen text — the exact case
        # that #147 false-denied.
        Test-ExpectedTextMatch -ExpectedText $needle -ObservedText 'Block Gained: 5 — Cloak Clasp' | Should -BeTrue
    }
    It 'REJECTS an internal id typed as a literal expected_text (the #147 contract)' {
        { Resolve-ScreenshotNeedle -ExpectedText 'CLOAK_CLASP' -ExpectedTextRef '' -RefToName $maps.NameByRef -CatalogIds $maps.CatalogIds -ItemId 'name-visible' } |
            Should -Throw -ExpectedMessage '*resolved catalog id*'
    }
    It 'passes a computed VALUE literal through unchanged' {
        Resolve-ScreenshotNeedle -ExpectedText 'Block Gained: 5' -ExpectedTextRef '' -RefToName $maps.NameByRef -CatalogIds $maps.CatalogIds -ItemId 'value-visible' |
            Should -BeExactly 'Block Gained: 5'
    }
    It 'rejects declaring BOTH expected_text and expected_text_ref' {
        { Resolve-ScreenshotNeedle -ExpectedText 'Block Gained: 5' -ExpectedTextRef 'CLOAK_CLASP' -RefToName $maps.NameByRef -CatalogIds $maps.CatalogIds -ItemId 'x' } |
            Should -Throw -ExpectedMessage '*exactly one source*'
    }
    It 'rejects declaring NEITHER source on a text-required item' {
        { Resolve-ScreenshotNeedle -ExpectedText '' -ExpectedTextRef '' -RefToName $maps.NameByRef -CatalogIds $maps.CatalogIds -ItemId 'x' } |
            Should -Throw -ExpectedMessage '*binds to no authoritative source*'
    }
    It 'rejects an expected_text_ref that resolves to no scenario_id_validation entry' {
        { Resolve-ScreenshotNeedle -ExpectedTextRef 'NOT_A_REAL_ID' -ExpectedText '' -RefToName $maps.NameByRef -CatalogIds $maps.CatalogIds -ItemId 'x' } |
            Should -Throw -ExpectedMessage '*matches no scenario_id_validation entry*'
    }
}

Describe 'Resolve-LiveMcpTargetId — identity binds to a catalog id' {
    BeforeEach { $maps = New-CloakClaspRefMaps }

    It 'resolves target_id_ref to the catalog id' {
        Resolve-LiveMcpTargetId -TargetIdRef 'CLOAK_CLASP' -RefToId $maps.IdByRef -ItemId 'target-present' |
            Should -BeExactly 'CLOAK_CLASP'
    }
    It 'rejects a missing target_id_ref' {
        { Resolve-LiveMcpTargetId -TargetIdRef '' -RefToId $maps.IdByRef -ItemId 'target-present' } |
            Should -Throw -ExpectedMessage '*must declare target_id_ref*'
    }
    It 'rejects an unresolvable target_id_ref' {
        { Resolve-LiveMcpTargetId -TargetIdRef 'NOPE' -RefToId $maps.IdByRef -ItemId 'target-present' } |
            Should -Throw -ExpectedMessage '*matches no scenario_id_validation entry*'
    }
}

Describe 'Test-GameStateJsonContainsId — deterministic JSON identity check' {
    It 'confirms the id is present verbatim in the get_game_state JSON' {
        $json = '{"player":{"relics":[{"id":"CLOAK_CLASP","block_gained":5}]}}'
        Test-GameStateJsonContainsId -Json $json -Id 'CLOAK_CLASP' | Should -BeTrue
    }
    It 'is case-insensitive for canonical id casing' {
        Test-GameStateJsonContainsId -Json '{"id":"cloak_clasp"}' -Id 'CLOAK_CLASP' | Should -BeTrue
    }
    It 'does NOT match a longer id token (boundary-strict)' {
        Test-GameStateJsonContainsId -Json '{"id":"CLOAK_CLASP_PLUS"}' -Id 'CLOAK_CLASP' | Should -BeFalse
    }
    It 'fails when the id is absent' {
        Test-GameStateJsonContainsId -Json '{"relics":[{"id":"AKABEKO"}]}' -Id 'CLOAK_CLASP' | Should -BeFalse
    }
    It 'fails on empty input' {
        Test-GameStateJsonContainsId -Json '' -Id 'CLOAK_CLASP' | Should -BeFalse
        Test-GameStateJsonContainsId -Json '{"id":"CLOAK_CLASP"}' -Id '' | Should -BeFalse
    }
}

Describe 'New-ScenarioRefMaps — flattens scenario_id_validation (real run-phases helper)' {
    It 'maps both input and id to the display name, and collects catalog ids' {
        $maps = New-CloakClaspRefMaps
        $maps.NameByRef['CLOAK_CLASP']  | Should -BeExactly 'Cloak Clasp'
        $maps.IdByRef['CLOAK_CLASP']    | Should -BeExactly 'CLOAK_CLASP'
        $maps.CatalogIds.ContainsKey('CLOAK_CLASP') | Should -BeTrue
        # Lookups are case-insensitive.
        $maps.NameByRef['cloak_clasp']  | Should -BeExactly 'Cloak Clasp'
    }
    It 'spans cards, relics, and encounters' {
        $v = [pscustomobject]@{
            cards = @([pscustomobject]@{ field='deck'; input='STRIKE'; id='STRIKE'; name='Strike'; source='lookup_card' })
            relics = @([pscustomobject]@{ field='add_relics'; input='AKABEKO'; id='AKABEKO'; name='Akabeko'; source='lookup_relic' })
            encounters = @([pscustomobject]@{ field='next_normal_encounter'; input='JAW_WORM'; id='JAW_WORM'; name='Jaw Worm'; source='list_encounters' })
        }
        $maps = New-ScenarioRefMaps -ScenarioIdValidation $v
        $maps.NameByRef['STRIKE']   | Should -BeExactly 'Strike'
        $maps.NameByRef['AKABEKO']  | Should -BeExactly 'Akabeko'
        $maps.NameByRef['JAW_WORM'] | Should -BeExactly 'Jaw Worm'
    }
    It 'returns empty maps for null scenario_id_validation without throwing' {
        $maps = New-ScenarioRefMaps -ScenarioIdValidation $null
        $maps.NameByRef.Count   | Should -Be 0
        $maps.CatalogIds.Count  | Should -Be 0
    }
}

Describe 'Read-EvidenceArtifactText — reads the captured get_game_state artifact (real run-phases helper)' {
    BeforeAll {
        # The helper closes over these script-scope path vars in production; set
        # them so the AST-extracted copy resolves relative paths the same way.
        $script:ValidationArtifactDir = Join-Path ([System.IO.Path]::GetTempPath()) ("evid-art-" + [guid]::NewGuid().ToString('N'))
        $script:ScreenshotDir = $script:ValidationArtifactDir
        $script:RepoRoot = $script:ValidationArtifactDir
        New-Item -ItemType Directory -Force -Path $script:ValidationArtifactDir | Out-Null
        $script:jsonName = 'live-mcp-target-present.json'
        Set-Content -LiteralPath (Join-Path $script:ValidationArtifactDir $script:jsonName) -Value '{"relics":[{"id":"CLOAK_CLASP"}]}' -Encoding UTF8
    }
    AfterAll {
        Remove-Item -LiteralPath $script:ValidationArtifactDir -Recurse -Force -ErrorAction SilentlyContinue
    }
    It 'resolves a path relative to the validation artifact dir and returns its content' {
        $text = Read-EvidenceArtifactText -Paths @($script:jsonName)
        Test-GameStateJsonContainsId -Json $text -Id 'CLOAK_CLASP' | Should -BeTrue
    }
    It 'returns empty when no path resolves' {
        Read-EvidenceArtifactText -Paths @('does-not-exist.json') | Should -BeNullOrEmpty
    }
}

Describe 'End-to-end: the corrected #147 contract passes; the broken one is rejected' {
    It 'corrected contract — identity via JSON + name derived from catalog — passes a correct feature' {
        $maps = New-CloakClaspRefMaps

        # Identity: the resolved id is confirmed present in captured JSON.
        $targetId = Resolve-LiveMcpTargetId -TargetIdRef 'CLOAK_CLASP' -RefToId $maps.IdByRef -ItemId 'target-present'
        $gameState = '{"player":{"relics":[{"id":"CLOAK_CLASP","block_gained":5}]}}'
        Test-GameStateJsonContainsId -Json $gameState -Id $targetId | Should -BeTrue

        # Stat/value: computed literal matches the rendered tooltip.
        $valueNeedle = Resolve-ScreenshotNeedle -ExpectedText 'Block Gained: 5' -ExpectedTextRef '' -RefToName $maps.NameByRef -CatalogIds $maps.CatalogIds -ItemId 'value-visible'
        Test-ExpectedTextMatch -ExpectedText $valueNeedle -ObservedText 'Cloak Clasp tooltip — Block Gained: 5' | Should -BeTrue

        # Name rendering: derived name matches the rendered display name.
        $nameNeedle = Resolve-ScreenshotNeedle -ExpectedTextRef 'CLOAK_CLASP' -ExpectedText '' -RefToName $maps.NameByRef -CatalogIds $maps.CatalogIds -ItemId 'name-visible'
        Test-ExpectedTextMatch -ExpectedText $nameNeedle -ObservedText 'Cloak Clasp tooltip — Block Gained: 5' | Should -BeTrue
    }
    It 'broken contract — id as on-screen text — is structurally rejected, not false-denied' {
        $maps = New-CloakClaspRefMaps
        # Authoring an internal id as a screenshot needle is impossible: it is a
        # malformed contract, caught before any feature judgement happens.
        { Resolve-ScreenshotNeedle -ExpectedText 'CLOAK_CLASP' -ExpectedTextRef '' -RefToName $maps.NameByRef -CatalogIds $maps.CatalogIds -ItemId 'name-visible' } |
            Should -Throw -ExpectedMessage '*resolved catalog id*'
    }
}
