#Requires -Modules @{ ModuleName = 'Pester'; ModuleVersion = '5.0.0' }

# Reintroduction guard (migration-policy: "Tests should fail if the retired
# path is reintroduced into live code").
#
# The retired path decided whether unit tests passed by regex-scanning the
# agent's English prose ("failed/partial/regressed") instead of reading the
# deterministic `dotnet test` exit code + TRX. It was deleted end to end and
# replaced by Get-ObservedUnitTestResult + Invoke-DeterministicUnitTests. These
# tests read the live run-phases.ps1 source and FAIL if any prose/substring
# scan of test results comes back, or if the deterministic gate goes missing.

BeforeAll {
    Set-StrictMode -Version 3.0
    $ErrorActionPreference = 'Stop'

    $script:ScriptPath = Join-Path $PSScriptRoot '..' 'run-phases.ps1'
    if (-not (Test-Path -LiteralPath $script:ScriptPath)) {
        throw "run-phases.ps1 not found at $script:ScriptPath"
    }
    $script:Source = Get-Content -LiteralPath $script:ScriptPath -Raw

    # AST so we can reason structurally, not just by string search.
    $errors = $null
    $tokens = $null
    $script:Ast = [System.Management.Automation.Language.Parser]::ParseInput($script:Source, [ref]$tokens, [ref]$errors)
    if ($errors.Count -gt 0) {
        throw "run-phases.ps1 failed to parse:`n$(($errors | ForEach-Object { '  L' + $_.Extent.StartLineNumber + ': ' + $_.Message }) -join "`n")"
    }
}

Describe 'retired prose-based unit-test gate stays deleted' {
    It 'defines no Test-TextMentionsFailedTests function' {
        $functions = $script:Ast.FindAll(
            { param($n) $n -is [System.Management.Automation.Language.FunctionDefinitionAst] },
            $true
        )
        @($functions | Where-Object { $_.Name -eq 'Test-TextMentionsFailedTests' }).Count |
            Should -Be 0 -Because 'the prose-scan unit-test gate was migrated to a deterministic exit-code/TRX gate'
    }

    It 'does not reference Test-TextMentionsFailedTests anywhere in the script' {
        ($script:Source -match 'Test-TextMentionsFailedTests') |
            Should -BeFalse -Because 'no live call site may resurrect the prose gate'
    }

    It 'does not regex-scan unit-test result text to gate (no "failed|partial|regressed"-style scan on unit notes)' {
        # The original gate matched failure WORDS in narration. If any such
        # alternation pattern returns in the script, fail. (Get-ToolFailureCategory
        # legitimately inspects raw tool output for live-MCP categorization, but
        # it must not be (re)wired as the unit-test verdict — that is asserted by
        # the deterministic-gate presence checks below and by the verification
        # guard no longer reading unit_tests.notes.)
        $proseFailurePattern = "fail(?:ed)?\s*\|\s*partial|partial\s*\|\s*regress|regress(?:ed|ions?)?\s*\|\s*fail"
        ($script:Source -match $proseFailurePattern) |
            Should -BeFalse -Because 'unit-test pass/fail must come from the observed exit code + TRX, never a prose word-scan'
    }

    It 'the verification evidence guard no longer reads unit_tests.notes to decide the verdict' {
        $guard = $script:Ast.FindAll(
            { param($n) $n -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq 'Apply-VerificationEvidenceGuard' },
            $true
        ) | Select-Object -First 1
        $guard | Should -Not -BeNullOrEmpty
        $guardText = $guard.Extent.Text
        # The guard may still touch screenshot/evidence notes, but it must not
        # pull the unit-test result back into a text gate. The whole unit_tests
        # block (and the $unitTests / $unitEvidenceText locals it scanned) was
        # removed; assert none of it returns.
        $guardText.Contains('$unitTests')        | Should -BeFalse -Because 'the guard must not re-read the unit_tests block to gate'
        $guardText.Contains('$unitEvidenceText') | Should -BeFalse -Because 'the guard must not scan unit-test evidence notes to gate'
        $guardText.Contains('$unitNotes')        | Should -BeFalse -Because 'the guard must not derive the unit-test verdict from prose notes'
    }
}

Describe 'deterministic unit-test gate is present (the replacement path exists)' {
    It 'dot-sources the pure UnitTestResult library' {
        $script:Source.Contains('UnitTestResult.ps1') |
            Should -BeTrue -Because 'the observed-result parser must be loaded'
    }

    It 'defines Invoke-DeterministicUnitTests and calls Get-ObservedUnitTestResult' {
        $functions = $script:Ast.FindAll(
            { param($n) $n -is [System.Management.Automation.Language.FunctionDefinitionAst] },
            $true
        )
        @($functions | Where-Object { $_.Name -eq 'Invoke-DeterministicUnitTests' }).Count |
            Should -Be 1 -Because 'the harness must run unit tests deterministically itself'
        ($script:Source -match 'Get-ObservedUnitTestResult') |
            Should -BeTrue -Because 'the deterministic runner must consume the observed exit-code/TRX verdict'
    }

    It 'runs dotnet test with a trx logger so the observed result is structured' {
        ($script:Source -match 'dotnet test') |
            Should -BeTrue -Because 'the harness owns the dotnet test invocation'
        ($script:Source -match 'trx;LogFileName') |
            Should -BeTrue -Because 'a structured TRX is the ground truth the parser reads'
    }

    It 'keeps dotnet stdout out of Invoke-DeterministicUnitTests return stream' {
        $function = $script:Ast.FindAll(
            { param($n) $n -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq 'Invoke-DeterministicUnitTests' },
            $true
        ) | Select-Object -First 1
        $function | Should -Not -BeNullOrEmpty
        $functionText = $function.Extent.Text

        ($functionText -match '(?s)&\s+dotnet\s+test.+?\|\s*ForEach-Object\s*\{\s*Write-Host\s+\$_\s*\}') |
            Should -BeTrue -Because 'external-command stdout is otherwise captured alongside the verdict object when the caller assigns the function output'
    }

    It 'stamps the observed unit-test verdict authoritatively into the result' {
        ($script:Source -match 'Set-AuthoritativeUnitTestResult') |
            Should -BeTrue -Because 'the agent self-report must not own the unit-test verdict'
    }
}
