param(
    [Parameter(Mandatory = $true)][string]$IssueNumber,
    [Parameter(Mandatory = $true)][string]$RepoSlug,
    [Parameter(Mandatory = $true)][string]$RepoRoot,
    [Parameter(Mandatory = $true)][string]$ClaudeCliPath,
    [Parameter(Mandatory = $true)][string]$McpConfigPath,
    [Parameter(Mandatory = $true)][string]$StreamLogPath,
    [Parameter(Mandatory = $true)][string]$DebugLogPath,
    [Parameter(Mandatory = $true)][string]$SummaryLogPath,
    [Parameter(Mandatory = $true)][string]$ScreenshotDir,
    [Parameter(Mandatory = $true)][string]$ValidationArtifactDir
)

$ErrorActionPreference = 'Stop'

$phaseDefinitions = @(
    [ordered]@{
        Name = 'investigation'
        Json = 'issue-agent-investigation.json'
        Markdown = 'issue-agent-investigation.md'
        AllowedAbortReasons = @('card_not_found', 'card_ambiguous', 'character_not_found', 'metadata_unavailable', 'mcp_capability_missing', 'game_state_unreachable', 'validation_plan_impossible')
    },
    [ordered]@{
        Name = 'implementation'
        Json = 'issue-agent-implementation.json'
        Markdown = 'issue-agent-implementation.md'
        AllowedAbortReasons = @('change_too_large', 'requires_new_library', 'requires_architecture_change', 'unsafe_refactor', 'missing_code_context', 'conflicting_requirements', 'cannot_implement_without_guessing')
    },
    [ordered]@{
        Name = 'verification'
        Json = 'issue-agent-verification.json'
        Markdown = 'issue-agent-verification.md'
        AllowedAbortReasons = @('unit_tests_failed', 'live_validation_failed', 'screenshot_missing', 'screenshot_not_relevant', 'mcp_state_mismatch', 'claimed_result_not_observed', 'artifact_contract_missing')
    }
)

function Write-AgentEvent {
    param(
        [Parameter(Mandatory = $true)][string]$Kind,
        [Parameter(Mandatory = $true)][string]$Message,
        [object]$Data = $null
    )

    $record = [ordered]@{
        timestamp = (Get-Date).ToString('o')
        kind = $Kind
        message = $Message
    }
    if ($null -ne $Data) { $record.data = $Data }

    $json = $record | ConvertTo-Json -Compress -Depth 30
    Add-Content -LiteralPath $StreamLogPath -Value $json -Encoding UTF8
    Write-Host "[$Kind] $Message"
}

function Add-JobSummaryMarkdown {
    param(
        [string]$Title,
        [string]$MarkdownPath
    )

    if ([string]::IsNullOrWhiteSpace($env:GITHUB_STEP_SUMMARY)) { return }

    "## $Title" | Out-File -FilePath $env:GITHUB_STEP_SUMMARY -Encoding utf8 -Append
    '' | Out-File -FilePath $env:GITHUB_STEP_SUMMARY -Encoding utf8 -Append
    if (Test-Path -LiteralPath $MarkdownPath) {
        Get-Content -LiteralPath $MarkdownPath -Raw | Out-File -FilePath $env:GITHUB_STEP_SUMMARY -Encoding utf8 -Append
    } else {
        '_No phase markdown was written._' | Out-File -FilePath $env:GITHUB_STEP_SUMMARY -Encoding utf8 -Append
    }
    '' | Out-File -FilePath $env:GITHUB_STEP_SUMMARY -Encoding utf8 -Append
}

function Read-JsonFile {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) { throw "Required JSON file was not written: $Path" }
    try {
        return (Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json -ErrorAction Stop)
    } catch {
        throw "Required JSON file '$Path' is invalid: $($_.Exception.Message)"
    }
}

function Get-PropertyValue {
    param([object]$Object, [string]$Name)
    if ($null -eq $Object) { return $null }
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) { return $null }
    return $property.Value
}

function Write-SyntheticRollup {
    param(
        [string]$AbortLayer,
        [string]$AbortReason,
        [string]$Notes
    )

    $rollupPath = Join-Path $ValidationArtifactDir 'issue-agent-result.json'
    $rollup = [ordered]@{
        issue_number = [int]$IssueNumber
        status = 'blocked'
        abort_layer = $AbortLayer
        abort_reason = $AbortReason
        retryable = $false
        human_action_required = $true
        layers = [ordered]@{
            investigation = [ordered]@{ status = 'not_run'; abort_reason = $null }
            implementation = [ordered]@{ status = 'not_run'; abort_reason = $null }
            verification = [ordered]@{ status = 'not_run'; abort_reason = $null }
        }
        unit_tests = [ordered]@{ passed = $null; status = 'blocked'; notes = '' }
        live_mcp_validation = [ordered]@{ passed = $null; status = 'blocked'; notes = '' }
        screenshot_validation = [ordered]@{ passed = $null; status = 'blocked'; count = 0; notes = '' }
        card_metadata_discovery = [ordered]@{ passed = $null; status = 'blocked'; notes = '' }
        used_mcp = $null
        used_raw_bridge_or_queue = $null
        opened_pr = $null
        opened_pr_url = $null
        should_close_issue = $false
        evidence_summary = @($Notes)
    }
    if ($rollup.layers.PSObject.Properties[$AbortLayer]) {
        $rollup.layers.$AbortLayer.status = 'abort'
        $rollup.layers.$AbortLayer.abort_reason = $AbortReason
    }
    $rollup | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $rollupPath -Encoding UTF8
}

function Assert-PhaseContract {
    param(
        [hashtable]$Phase,
        [object]$Result
    )

    $status = [string](Get-PropertyValue -Object $Result -Name 'status')
    if ($status -notin @('pass', 'abort')) {
        throw "Phase '$($Phase.Name)' wrote invalid status '$status'. Expected pass or abort."
    }

    $abortReason = Get-PropertyValue -Object $Result -Name 'abort_reason'
    if ($status -eq 'abort') {
        if ([string]::IsNullOrWhiteSpace([string]$abortReason)) {
            throw "Phase '$($Phase.Name)' aborted without abort_reason."
        }
        if ([string]$abortReason -notin $Phase.AllowedAbortReasons) {
            throw "Phase '$($Phase.Name)' used abort_reason '$abortReason', outside allowed enum."
        }
    }

    return $status
}

function Invoke-ClaudePhase {
    param(
        [hashtable]$Phase,
        [string]$Prompt
    )

    $phaseName = $Phase.Name
    $promptPath = Join-Path $env:RUNNER_TEMP "claude-issue-agent-$phaseName-prompt.md"
    $phaseJsonPath = Join-Path $ValidationArtifactDir $Phase.Json
    $phaseMarkdownPath = Join-Path $ValidationArtifactDir $Phase.Markdown

    $Prompt | Set-Content -LiteralPath $promptPath -Encoding UTF8
    $promptText = Get-Content -LiteralPath $promptPath -Raw

    Write-Host "::group::Claude issue-agent phase: $phaseName"
    Write-AgentEvent 'phase_start' "Starting $phaseName phase." @{ phase = $phaseName }

    & $ClaudeCliPath `
        -p $promptText `
        --bare `
        --model sonnet `
        --permission-mode bypassPermissions `
        --output-format stream-json `
        --verbose `
        --debug-file $DebugLogPath `
        --strict-mcp-config `
        "--mcp-config=$McpConfigPath" `
        --max-budget-usd '15.00' `
        --add-dir $RepoRoot 2>&1 |
        ForEach-Object {
            $line = [string]$_
            try {
                $event = $line | ConvertFrom-Json -ErrorAction Stop
                if ($event.type -eq 'assistant' -and $event.message.content) {
                    foreach ($block in $event.message.content) {
                        if ($block.type -eq 'tool_use') {
                            $inputJson = if ($null -ne $block.input) { $block.input | ConvertTo-Json -Compress -Depth 20 } else { '{}' }
                            $message = "$($block.name) $inputJson"
                            Write-AgentEvent 'tool_use' $message @{ phase = $phaseName }
                            Add-Content -LiteralPath $SummaryLogPath -Value "${phaseName} tool_use: $message" -Encoding UTF8
                        } elseif ($block.type -eq 'text' -and -not [string]::IsNullOrWhiteSpace([string]$block.text)) {
                            $text = [string]$block.text
                            if ($text.Length -gt 1000) { $text = $text.Substring(0, 1000) + '... [truncated]' }
                            Write-AgentEvent 'assistant_text' $text @{ phase = $phaseName }
                            Add-Content -LiteralPath $SummaryLogPath -Value "${phaseName} assistant_text: $text" -Encoding UTF8
                        }
                    }
                } elseif ($event.type -eq 'user' -and $event.tool_use_result) {
                    $result = [string]$event.tool_use_result
                    if ($result.Length -gt 600) { $result = $result.Substring(0, 600) + '... [truncated]' }
                    Write-AgentEvent 'tool_result' $result @{ phase = $phaseName }
                    Add-Content -LiteralPath $SummaryLogPath -Value "${phaseName} tool_result: $result" -Encoding UTF8
                } elseif ($event.type -eq 'result') {
                    $resultJson = $event | ConvertTo-Json -Compress -Depth 30
                    Write-AgentEvent 'result' $resultJson @{ phase = $phaseName }
                    Add-Content -LiteralPath $SummaryLogPath -Value "${phaseName} result: $resultJson" -Encoding UTF8
                }
            } catch {
                Write-AgentEvent 'raw' $line @{ phase = $phaseName }
            }
        }

    $exitCode = $LASTEXITCODE
    Write-AgentEvent 'phase_exit' "${phaseName} Claude exit code: $exitCode" @{ phase = $phaseName; exit_code = $exitCode }
    Write-Host "::endgroup::"
    if ($exitCode -ne 0) { throw "Claude phase '$phaseName' failed with exit code $exitCode." }

    $result = Read-JsonFile -Path $phaseJsonPath
    $status = Assert-PhaseContract -Phase $Phase -Result $result
    Add-JobSummaryMarkdown -Title "Issue Agent $phaseName" -MarkdownPath $phaseMarkdownPath

    return [ordered]@{
        Status = $status
        JsonPath = $phaseJsonPath
        MarkdownPath = $phaseMarkdownPath
        Result = $result
    }
}

function Get-CommonPromptPrefix {
    param([string]$PhaseName)
@"
You are Claude Code running the $PhaseName phase for $RepoSlug issue #$IssueNumber.

GitHub Actions triggered this job for exactly issue #$IssueNumber. Do not process any other issue.
Use the local checkout at `$env:ISSUE_AGENT_REPO_ROOT` and read the issue with:

``````
gh issue view $IssueNumber --repo $RepoSlug --comments
``````

All stateful Slay the Spire 2 work must go through MCP tools from the project MCP config at `$McpConfigPath`.
Do not use raw localhost bridge calls, filesystem queues, `LiveScenarios/`, `ops/live-worker/`, or `D:\automation\spirelens-live-bridge`.
Write your JSON and Markdown artifacts to `$ValidationArtifactDir`.
Keep Markdown concise and human-readable; it will be appended to the GitHub job summary.
"@
}

Remove-Item -LiteralPath $StreamLogPath, $DebugLogPath, $SummaryLogPath -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $ScreenshotDir, $ValidationArtifactDir -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $ScreenshotDir, $ValidationArtifactDir | Out-Null

$env:SCREENSHOT_DIR = $ScreenshotDir
$env:VALIDATION_ARTIFACT_DIR = $ValidationArtifactDir
Set-Location -LiteralPath $RepoRoot

"Claude phased issue-agent stream for $RepoSlug#$IssueNumber" | Set-Content -LiteralPath $SummaryLogPath -Encoding UTF8
"Sanitized event stream: $StreamLogPath" | Add-Content -LiteralPath $SummaryLogPath -Encoding UTF8
"Debug log: $DebugLogPath" | Add-Content -LiteralPath $SummaryLogPath -Encoding UTF8
"Screenshot dir: $ScreenshotDir" | Add-Content -LiteralPath $SummaryLogPath -Encoding UTF8
"Validation artifact dir: $ValidationArtifactDir" | Add-Content -LiteralPath $SummaryLogPath -Encoding UTF8

$investigationPrompt = (Get-CommonPromptPrefix -PhaseName 'investigation') + @"

INVESTIGATION RULES:
- Do not edit files, commit, push, open PRs, or run implementation tests.
- Focus only on issue interpretation, card identity, character identity, MCP/game-state facts, and validation plan.
- If a card is specified but ambiguous or cannot be found, abort.
- If MCP or repo metadata cannot support the needed validation plan, abort.
- Write `issue-agent-investigation.json` with:
  `{ "layer":"investigation", "status":"pass|abort", "abort_reason":null, "retryable":false, "human_action_required":false, "notes":"", "card":{}, "character":{}, "validation_plan":[] }`
- Allowed abort reasons: card_not_found, card_ambiguous, character_not_found, metadata_unavailable, mcp_capability_missing, game_state_unreachable, validation_plan_impossible.
- Write `issue-agent-investigation.md` summarizing facts found, missing facts, and the validation plan.
"@

$implementationPrompt = (Get-CommonPromptPrefix -PhaseName 'implementation') + @"

IMPLEMENTATION RULES:
- Read `$ValidationArtifactDir\issue-agent-investigation.json` first and implement only that plan.
- Own code changes only. Do not claim verification success.
- If the viable solve requires dramatic changes, a new library, architecture changes, or unsafe refactors, abort.
- If you make code changes, create a branch, commit, push, and open a PR.
- Write `issue-agent-implementation.json` with:
  `{ "layer":"implementation", "status":"pass|abort", "abort_reason":null, "retryable":false, "human_action_required":false, "notes":"", "changed_files":[], "opened_pr":null, "opened_pr_url":null }`
- Allowed abort reasons: change_too_large, requires_new_library, requires_architecture_change, unsafe_refactor, missing_code_context, conflicting_requirements, cannot_implement_without_guessing.
- Write `issue-agent-implementation.md` summarizing changes, branch, commit, PR link, or abort reason.
"@

$verificationPrompt = (Get-CommonPromptPrefix -PhaseName 'verification') + @"

VERIFICATION RULES:
- Read `issue-agent-investigation.json` and `issue-agent-implementation.json` first.
- Own tests, live MCP validation, screenshot capture, and final evidence only.
- Use this Windows validation sequence unless investigation says it is not applicable:

``````powershell
`$sts2DataDir = "D:\SteamLibrary\steamapps\common\Slay the Spire 2\data_sts2_windows_x86_64"
dotnet build "Tests\SpireLens.Core.Tests\SpireLens.Core.Tests.csproj" -c Debug "-p:Sts2DataDir=`$sts2DataDir"
dotnet test "Tests\SpireLens.Core.Tests\SpireLens.Core.Tests.csproj" -c Debug --no-build "-p:Sts2DataDir=`$sts2DataDir"
``````

- Save screenshots to `$ScreenshotDir`.
- If you cannot obtain meaningful screenshot evidence, abort with screenshot_missing or screenshot_not_relevant.
- Write `issue-agent-verification.json` with:
  `{ "layer":"verification", "status":"pass|abort", "abort_reason":null, "retryable":false, "human_action_required":false, "notes":"", "unit_tests":{"passed":null,"status":"not_run","notes":""}, "live_mcp_validation":{"passed":null,"status":"not_run","notes":""}, "screenshot_validation":{"passed":null,"status":"not_run","count":0,"notes":""}, "used_mcp":null, "used_raw_bridge_or_queue":false }`
- Allowed abort reasons: unit_tests_failed, live_validation_failed, screenshot_missing, screenshot_not_relevant, mcp_state_mismatch, claimed_result_not_observed, artifact_contract_missing.
- Also write rollup `issue-agent-result.json` with issue_number, status, abort_layer, abort_reason, retryable, human_action_required, layers, unit_tests, live_mcp_validation, screenshot_validation, card_metadata_discovery, used_mcp, used_raw_bridge_or_queue, opened_pr, opened_pr_url, should_close_issue, and evidence_summary.
- Write `issue-agent-verification.md` summarizing pass/fail evidence.
- Write `issue-agent-result.md` as a compact final rollup including any PR URL from implementation.
- If complete, remove `issue-agent` and add `issue-agent-complete`. If blocked, remove `issue-agent` and add `issue-agent-blocked`.
"@

$phaseResults = @{}
foreach ($phase in $phaseDefinitions) {
    $prompt = switch ($phase.Name) {
        'investigation' { $investigationPrompt }
        'implementation' { $implementationPrompt }
        'verification' { $verificationPrompt }
    }

    $phaseResult = Invoke-ClaudePhase -Phase $phase -Prompt $prompt
    $phaseResults[$phase.Name] = $phaseResult

    if ($phaseResult.Status -eq 'abort') {
        $abortReason = [string](Get-PropertyValue -Object $phaseResult.Result -Name 'abort_reason')
        $notes = [string](Get-PropertyValue -Object $phaseResult.Result -Name 'notes')
        Write-SyntheticRollup -AbortLayer $phase.Name -AbortReason $abortReason -Notes $notes
        break
    }
}

$resultMarkdown = Join-Path $ValidationArtifactDir 'issue-agent-result.md'
if (Test-Path -LiteralPath $resultMarkdown) {
    Add-JobSummaryMarkdown -Title 'Issue Agent final result' -MarkdownPath $resultMarkdown
}

Write-AgentEvent 'exit' 'Phased issue-agent script completed.'