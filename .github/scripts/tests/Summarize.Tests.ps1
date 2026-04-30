#Requires -Modules @{ ModuleName = 'Pester'; ModuleVersion = '5.0.0' }

# Tests for the JSONL-parsing helpers in summarize-issue-agent.ps1. We extract
# the function definitions via AST so the tests run against the live source
# without re-implementing anything.

BeforeAll {
    Set-StrictMode -Version 3.0
    $ErrorActionPreference = 'Stop'

    Import-Module (Join-Path $PSScriptRoot 'Helpers.psm1') -Force

    $scriptPath = Join-Path $PSScriptRoot '..' 'summarize-issue-agent.ps1'
    $source = Import-ScriptFunctions -ScriptPath $scriptPath -FunctionNames @(
        'Get-PropertyValue', 'Get-NestedPropertyValue',
        'Get-ToolFailureCategory', 'New-ToolMetricBucket', 'Add-ToolMetricFailure',
        'Get-ClaudeToolCallSummary', 'Format-FailureCategories',
        'Get-ClaudeCostSummary', 'Format-Usd', 'Read-JsonOrNull',
        'Get-ExitCodeText'
    )
    . ([scriptblock]::Create($source))

    $script:fixtureDir = Join-Path $PSScriptRoot 'fixtures'
    $script:tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("summarize-tests-" + [guid]::NewGuid().Guid)
    New-Item -ItemType Directory -Force -Path $script:tempRoot | Out-Null
}

AfterAll {
    if ($script:tempRoot -and (Test-Path -LiteralPath $script:tempRoot)) {
        Remove-Item -LiteralPath $script:tempRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}

Describe 'Get-PropertyValue' {
    It 'returns null on null input' {
        Get-PropertyValue -Object $null -Name 'foo' | Should -BeNullOrEmpty
    }
    It 'reads a key from an [ordered] dict' {
        $d = [ordered]@{ status = 'pass'; count = 7 }
        Get-PropertyValue -Object $d -Name 'status' | Should -Be 'pass'
        Get-PropertyValue -Object $d -Name 'count'  | Should -Be 7
    }
    It 'returns null for a missing key on an [ordered] dict' {
        $d = [ordered]@{ status = 'pass' }
        Get-PropertyValue -Object $d -Name 'nope' | Should -BeNullOrEmpty
    }
    It 'reads a property from a PSCustomObject' {
        $o = [pscustomobject]@{ kind = 'event'; level = 3 }
        Get-PropertyValue -Object $o -Name 'kind'  | Should -Be 'event'
        Get-PropertyValue -Object $o -Name 'level' | Should -Be 3
    }
    It 'returns null for a missing PSCustomObject property' {
        $o = [pscustomobject]@{ kind = 'event' }
        Get-PropertyValue -Object $o -Name 'absent' | Should -BeNullOrEmpty
    }
}

Describe 'Get-NestedPropertyValue' {
    It 'walks a nested PSCustomObject path' {
        $o = [pscustomobject]@{ data = [pscustomobject]@{ phase = 'verification' } }
        Get-NestedPropertyValue -Object $o -Path @('data', 'phase') | Should -Be 'verification'
    }
    It 'returns null when an intermediate segment is missing' {
        $o = [pscustomobject]@{ data = [pscustomobject]@{} }
        Get-NestedPropertyValue -Object $o -Path @('data', 'phase') | Should -BeNullOrEmpty
    }
    It 'returns null when the receiver is null' {
        Get-NestedPropertyValue -Object $null -Path @('a', 'b') | Should -BeNullOrEmpty
    }
}

Describe 'Get-ToolFailureCategory' {
    It 'classifies permission denied' {
        Get-ToolFailureCategory -Text 'permission_denied' | Should -Be 'permission_denied'
        Get-ToolFailureCategory -Text 'permission to use Bash has been denied' | Should -Be 'permission_denied'
    }
    It 'classifies server errors' {
        Get-ToolFailureCategory -Text 'HTTP 500 internal server error' | Should -Be 'server_error'
    }
    It 'classifies timeouts' {
        Get-ToolFailureCategory -Text 'request timed out after 30s' | Should -Be 'timeout'
    }
    It 'classifies tool errors and exceptions' {
        Get-ToolFailureCategory -Text 'Error: File not found' | Should -Be 'tool_error'
        Get-ToolFailureCategory -Text 'Traceback: foo' | Should -Be 'tool_error'
        Get-ToolFailureCategory -Text 'exit code 2' | Should -Be 'tool_error'
        Get-ToolFailureCategory -Text 'Error CS1234 in line 3' | Should -Be 'tool_error'
    }
    It 'returns null for empty / unrelated text' {
        Get-ToolFailureCategory -Text $null   | Should -BeNullOrEmpty
        Get-ToolFailureCategory -Text ''      | Should -BeNullOrEmpty
        Get-ToolFailureCategory -Text 'all good' | Should -BeNullOrEmpty
    }
}

Describe 'Format-FailureCategories' {
    It 'returns empty string for null' {
        Format-FailureCategories -Categories $null | Should -Be ''
    }
    It 'iterates an [ordered]@{} (the actual line-154 bug shape)' {
        $cats = [ordered]@{ permission_denied = 3; tool_error = 1; timeout = 0 }
        Format-FailureCategories -Categories $cats | Should -Be 'permission_denied: 3, tool_error: 1'
    }
    It 'iterates a PSCustomObject (defensive branch)' {
        $cats = [pscustomobject]@{ permission_denied = 2; server_error = 1 }
        Format-FailureCategories -Categories $cats | Should -Be 'permission_denied: 2, server_error: 1'
    }
    It 'returns empty string when all counts are zero' {
        $cats = [ordered]@{ permission_denied = 0 }
        Format-FailureCategories -Categories $cats | Should -Be ''
    }
}

Describe 'New-ToolMetricBucket / Add-ToolMetricFailure' {
    It 'creates a bucket with the expected zero-initialized shape' {
        $b = New-ToolMetricBucket
        $b.tool_uses            | Should -Be 0
        $b.tool_results         | Should -Be 0
        $b.failed_tool_results  | Should -Be 0
        $b.permission_denials   | Should -Be 0
        $b.failure_categories   | Should -BeOfType ([System.Collections.Specialized.OrderedDictionary])
        $b.failure_categories.Count | Should -Be 0
    }
    It 'records categorized failures in failure_categories' {
        $b = New-ToolMetricBucket
        Add-ToolMetricFailure -Bucket $b -Category 'tool_error'
        Add-ToolMetricFailure -Bucket $b -Category 'tool_error'
        Add-ToolMetricFailure -Bucket $b -Category 'permission_denied'
        $b.failed_tool_results        | Should -Be 3
        $b.failure_categories['tool_error']        | Should -Be 2
        $b.failure_categories['permission_denied'] | Should -Be 1
    }
    It 'ignores empty-category records' {
        $b = New-ToolMetricBucket
        Add-ToolMetricFailure -Bucket $b -Category $null
        Add-ToolMetricFailure -Bucket $b -Category '   '
        $b.failed_tool_results | Should -Be 0
        $b.failure_categories.Count | Should -Be 0
    }
}

Describe 'Get-ClaudeCostSummary' {
    It 'returns a zeroed summary when path is empty/missing' {
        $r = Get-ClaudeCostSummary -Path ''
        $r.TotalCostUsd | Should -Be 0.0
        $r.Results      | Should -Be 0
    }
    It 'aggregates cost across result records' {
        $tmp = Join-Path $script:tempRoot 'cost.jsonl'
        @(
            '{"kind":"phase_exit","data":{"phase":"test_plan","exit_code":0}}'
            '{"kind":"result","message":"{\"total_cost_usd\":0.10,\"num_turns\":3,\"usage\":{\"input_tokens\":100,\"output_tokens\":50,\"cache_creation_input_tokens\":10,\"cache_read_input_tokens\":20}}"}'
            '{"kind":"result","message":"{\"total_cost_usd\":0.20,\"num_turns\":2,\"usage\":{\"input_tokens\":200,\"output_tokens\":40}}"}'
            'not valid json — should be skipped'
            '{"kind":"result","message":""}'
        ) -join "`n" | Set-Content -LiteralPath $tmp

        $r = Get-ClaudeCostSummary -Path $tmp
        $r.Results          | Should -Be 2
        [math]::Round($r.TotalCostUsd, 4) | Should -Be 0.30
        $r.Turns            | Should -Be 5
        $r.InputTokens      | Should -Be 300
        $r.OutputTokens     | Should -Be 90
        $r.CacheCreationInputTokens | Should -Be 10
        $r.CacheReadInputTokens     | Should -Be 20
    }
}

Describe 'Get-ClaudeToolCallSummary' {
    It 'returns an empty summary when the path is missing' {
        $r = Get-ClaudeToolCallSummary -Path '' -PhaseNames @('test_plan', 'verification')
        $r.total.tool_uses | Should -Be 0
        $r.phases['test_plan']    | Should -Not -BeNullOrEmpty
        $r.phases['verification'] | Should -Not -BeNullOrEmpty
    }
    It 'aggregates tool_use, tool_result, and permission_denials' {
        $tmp = Join-Path $script:tempRoot 'tools.jsonl'
        @(
            '{"kind":"tool_use","data":{"phase":"test_plan"},"message":""}'
            '{"kind":"tool_use","data":{"phase":"test_plan"},"message":""}'
            '{"kind":"tool_use","data":{"phase":"implementation"},"message":""}'
            '{"kind":"tool_result","data":{"phase":"test_plan","failed":true,"failure_category":"tool_error"},"message":"Error: foo"}'
            '{"kind":"tool_result","data":{"phase":"test_plan","failed":false},"message":""}'
            '{"kind":"result","message":"{\"permission_denials\":[{\"tool\":\"Bash\"},{\"tool\":\"Edit\"}]}"}'
        ) -join "`n" | Set-Content -LiteralPath $tmp

        # Each permission_denial in a result record bumps failed_tool_results
        # via Add-ToolMetricFailure, so total fails = 1 tool_error + 2 denials.
        # The result record has no data.phase, so it defaults to phase='unknown'
        # — denials land in total + the auto-created 'unknown' phase bucket,
        # not in any of the named phases.
        $r = Get-ClaudeToolCallSummary -Path $tmp -PhaseNames @('test_plan', 'implementation', 'verification')
        $r.total.tool_uses                          | Should -Be 3
        $r.total.tool_results                       | Should -Be 2
        $r.total.failed_tool_results                | Should -Be 3
        $r.total.permission_denials                 | Should -Be 2
        $r.phases['test_plan'].tool_uses            | Should -Be 2
        $r.phases['test_plan'].failed_tool_results  | Should -Be 1
        $r.phases['test_plan'].permission_denials   | Should -Be 0
        $r.phases['implementation'].tool_uses       | Should -Be 1
        $r.phases['verification'].tool_uses         | Should -Be 0
        $r.phases['unknown'].failed_tool_results    | Should -Be 2
        $r.phases['unknown'].permission_denials     | Should -Be 2
    }
}

Describe 'Get-ExitCodeText' {
    It 'returns _None_ for empty/missing paths' {
        Get-ExitCodeText -Path '' | Should -Be '_None_'
        Get-ExitCodeText -Path (Join-Path $script:tempRoot 'nope.jsonl') | Should -Be '_None_'
    }
    It 'aggregates phase_exit events into a phase=code summary' {
        $tmp = Join-Path $script:tempRoot 'exits.jsonl'
        @(
            '{"kind":"phase_exit","data":{"phase":"test_plan","exit_code":0}}'
            '{"kind":"phase_exit","data":{"phase":"implementation","exit_code":0}}'
            '{"kind":"phase_exit","data":{"phase":"verification","exit_code":1}}'
            'garbage line should be ignored'
            '{"kind":"other","data":{}}'
        ) -join "`n" | Set-Content -LiteralPath $tmp

        Get-ExitCodeText -Path $tmp | Should -Be 'test_plan=0, implementation=0, verification=1'
    }
}

Describe 'Read-JsonOrNull' {
    It 'returns null for empty/missing path' {
        Read-JsonOrNull -Path '' | Should -BeNullOrEmpty
        Read-JsonOrNull -Path (Join-Path $script:tempRoot 'nope.json') | Should -BeNullOrEmpty
    }
    It 'parses valid JSON' {
        $tmp = Join-Path $script:tempRoot 'good.json'
        '{"status":"pass","count":42}' | Set-Content -LiteralPath $tmp
        $r = Read-JsonOrNull -Path $tmp
        $r.status | Should -Be 'pass'
        $r.count  | Should -Be 42
    }
    It 'returns null for invalid JSON instead of throwing' {
        $tmp = Join-Path $script:tempRoot 'bad.json'
        'not json' | Set-Content -LiteralPath $tmp
        Read-JsonOrNull -Path $tmp | Should -BeNullOrEmpty
    }
}

Describe 'Format-Usd' {
    It 'formats with four decimals and a $ prefix' {
        Format-Usd -Value 1.23456 | Should -Be '$1.2346'
        Format-Usd -Value 0       | Should -Be '$0.0000'
        Format-Usd -Value 1234.5  | Should -Be '$1234.5000'
    }
}
