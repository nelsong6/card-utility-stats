#Requires -Modules @{ ModuleName = 'Pester'; ModuleVersion = '5.0.0' }

# Tests for the helpers defined in run-issue-agent-phases.ps1. We extract
# function definitions via AST so the tests run against the live source.

BeforeAll {
    Set-StrictMode -Version 3.0
    $ErrorActionPreference = 'Stop'

    Import-Module (Join-Path $PSScriptRoot 'Helpers.psm1') -Force

    $scriptPath = Join-Path $PSScriptRoot '..' 'run-issue-agent-phases.ps1'
    $source = Import-ScriptFunctions -ScriptPath $scriptPath -FunctionNames @(
        'Get-PropertyValue', 'Set-PropertyValue', 'ConvertTo-Array',
        'Get-TextBlob', 'Test-TextMentionsUnavailableEvidence',
        'Test-TextMentionsFailedTests', 'Get-ToolFailureCategory'
    )
    . ([scriptblock]::Create($source))
}

Describe 'ConvertTo-Array (function-return-unwrap fix)' {
    It 'wraps null as an empty array preserved through the function return' {
        # Note: piping the result to Should -BeOfType unrolls arrays. Test the
        # type with -is instead so the assertion runs against the array itself.
        $r = ConvertTo-Array -Value $null
        ($r -is [array]) | Should -BeTrue
        $r.Count         | Should -Be 0
    }
    It 'preserves a single scalar as a 1-elem array (no unwrap to scalar)' {
        $r = ConvertTo-Array -Value 'lonely'
        ($r -is [array]) | Should -BeTrue
        $r.Count         | Should -Be 1
        $r[0]            | Should -Be 'lonely'
    }
    It 'preserves a many-elem input array' {
        $r = ConvertTo-Array -Value @(1, 2, 3)
        ($r -is [array]) | Should -BeTrue
        $r.Count         | Should -Be 3
    }
}

Describe 'Get-PropertyValue / Set-PropertyValue (IDictionary + PSCustomObject)' {
    It 'reads / writes on an [ordered] dict' {
        $d = [ordered]@{ status = 'pass' }
        Get-PropertyValue -Object $d -Name 'status' | Should -Be 'pass'
        Set-PropertyValue -Object $d -Name 'status' -Value 'abort'
        Set-PropertyValue -Object $d -Name 'new'    -Value 7
        $d['status'] | Should -Be 'abort'
        $d['new']    | Should -Be 7
    }
    It 'reads / writes on a PSCustomObject' {
        $o = [pscustomobject]@{ status = 'pass' }
        Get-PropertyValue -Object $o -Name 'status' | Should -Be 'pass'
        Set-PropertyValue -Object $o -Name 'status' -Value 'abort'
        Set-PropertyValue -Object $o -Name 'new'    -Value 7
        $o.status | Should -Be 'abort'
        $o.new    | Should -Be 7
    }
    It 'returns null for missing keys / does nothing on null receivers' {
        Get-PropertyValue -Object $null -Name 'foo' | Should -BeNullOrEmpty
        $d = [ordered]@{ a = 1 }
        Get-PropertyValue -Object $d -Name 'missing' | Should -BeNullOrEmpty
        Set-PropertyValue -Object $null -Name 'foo' -Value 1   # should not throw
    }
}

Describe 'Get-TextBlob' {
    It 'joins non-empty values with newlines, skipping null/whitespace' {
        Get-TextBlob -Values @('one', '', $null, '  ', 'two') | Should -Be "one`ntwo"
    }
    It 'returns empty when all values are null/whitespace' {
        Get-TextBlob -Values @($null, '', '   ') | Should -Be ''
    }
}

Describe 'Test-TextMentionsUnavailableEvidence' {
    It 'detects unavailable / not achievable phrasings' {
        Test-TextMentionsUnavailableEvidence -Text 'tooltip not achievable on this surface' | Should -BeTrue
        Test-TextMentionsUnavailableEvidence -Text 'verified exclusively through unit tests' | Should -BeTrue
        Test-TextMentionsUnavailableEvidence -Text 'cannot be made visible without mouse hover' | Should -BeTrue
    }
    It 'returns false on benign text' {
        Test-TextMentionsUnavailableEvidence -Text 'all evidence captured' | Should -BeFalse
        Test-TextMentionsUnavailableEvidence -Text $null | Should -BeFalse
        Test-TextMentionsUnavailableEvidence -Text ''    | Should -BeFalse
    }
}

Describe 'Test-TextMentionsFailedTests' {
    It 'detects failure phrasing' {
        Test-TextMentionsFailedTests -Text 'partial pass' | Should -BeTrue
        Test-TextMentionsFailedTests -Text '3 tests failed' | Should -BeTrue
        Test-TextMentionsFailedTests -Text '2 regressions in legacy/parser' | Should -BeTrue
    }
    It 'ignores benign mentions' {
        Test-TextMentionsFailedTests -Text 'all green' | Should -BeFalse
        Test-TextMentionsFailedTests -Text $null      | Should -BeFalse
        Test-TextMentionsFailedTests -Text ''         | Should -BeFalse
    }
}
