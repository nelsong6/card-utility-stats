<#
.SYNOPSIS
Glimmung-native wrapper around run-phases.ps1.

.DESCRIPTION
run-phases.ps1 has eleven mandatory parameters. Under the old GitHub Actions
workflow a wrapper step resolved the Claude CLI path, generated a per-job MCP
config, and computed the artifact/screenshot/log paths, then passed all of them
in. The lift-and-shift to Glimmung dropped that layer: the glimmung-native bash
phase scripts were calling run-phases.ps1 with only four of the eleven params,
so the mandatory-parameter prompts hit EOF on the piped stdin, bound to empty,
and the phase did no real work and wrote no artifact (the collect step then
scp'd a file that never existed).

This script is that missing layer, reimplemented glimmung-native. It is
self-contained: every phase pod establishes its own laptop connection and runs
this wrapper, which resolves everything from the run's persistent working dir
(C:\glimmung-runs\<ref>, which survives across phases on the laptop) and the
checked-out repo, then invokes run-phases.ps1 with the full parameter set.

The Claude-CLI and STS2-game-dir candidate lists intentionally mirror
prepare-host.ps1's lists. Keep them in sync; if this duplication grows, extract
a shared module imported by both.
#>
param(
    [Parameter(Mandatory = $true)][ValidateSet('test_plan', 'implementation', 'verification')][string]$PhaseName,
    [Parameter(Mandatory = $true)][string]$IssueNumber,
    [Parameter(Mandatory = $true)][string]$RepoSlug,
    [Parameter(Mandatory = $true)][string]$RepoRoot
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 3.0

$workingDir = $env:GLIMMUNG_WORKING_DIR
if ([string]::IsNullOrWhiteSpace($workingDir)) {
    throw 'GLIMMUNG_WORKING_DIR is not set; run-issue-agent-phase.ps1 cannot derive artifact paths.'
}
if (-not (Test-Path -LiteralPath $RepoRoot)) {
    throw "RepoRoot '$RepoRoot' does not exist on the host."
}

# --- Resolve the Claude Code CLI (mirror prepare-host.ps1's candidate list) ---
$claudeCandidates = [System.Collections.Generic.List[string]]::new()
if (-not [string]::IsNullOrWhiteSpace($env:CONFIGURED_CLAUDE_CLI_PATH)) {
    $claudeCandidates.Add($env:CONFIGURED_CLAUDE_CLI_PATH)
}
foreach ($c in @(
        'D:\automation\claude-code\node_modules\@anthropic-ai\claude-code\bin\claude.exe',
        'C:\automation\claude-code\node_modules\@anthropic-ai\claude-code\bin\claude.exe',
        (Join-Path $env:USERPROFILE 'automation\claude-code\node_modules\@anthropic-ai\claude-code\bin\claude.exe'),
        (Join-Path $env:APPDATA 'npm\node_modules\@anthropic-ai\claude-code\bin\claude.exe')
    )) {
    $claudeCandidates.Add($c)
}
$claudeCliPath = $claudeCandidates |
    Where-Object { -not [string]::IsNullOrWhiteSpace($_) -and (Test-Path -LiteralPath $_) } |
    Select-Object -First 1
if (-not $claudeCliPath) {
    throw 'Claude Code CLI was not found in any known location. Set CONFIGURED_CLAUDE_CLI_PATH or install it under a documented default path.'
}

# --- Resolve the STS2 game dir (mirror prepare-host.ps1's candidate list) ---
$templateMcpPath = Join-Path $RepoRoot '.mcp.json'
if (-not (Test-Path -LiteralPath $templateMcpPath)) {
    throw "MCP config template was not found at '$templateMcpPath'."
}
$mcpConfig = Get-Content -LiteralPath $templateMcpPath -Raw | ConvertFrom-Json
$server = $mcpConfig.mcpServers.'spire-lens-mcp'
if ($null -eq $server) {
    throw "MCP config template '$templateMcpPath' does not define mcpServers.spire-lens-mcp."
}

# Best-effort read of the template's configured game dir (StrictMode-safe).
$templateGameDir = $null
if ($null -ne $server.env -and ($server.env.PSObject.Properties.Name -contains 'STS2_GAME_DIR')) {
    $templateGameDir = [string]$server.env.STS2_GAME_DIR
}

$gameDirCandidates = [System.Collections.Generic.List[string]]::new()
foreach ($c in @(
        $env:ISSUE_AGENT_STS2_GAME_DIR,
        $env:CONFIGURED_STS2_GAME_DIR,
        $templateGameDir,
        'D:\Programs\SteamLibrary\steamapps\common\Slay the Spire 2',
        'D:\SteamLibrary\steamapps\common\Slay the Spire 2',
        'C:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2',
        'C:\Program Files\Steam\steamapps\common\Slay the Spire 2'
    )) {
    if ([string]::IsNullOrWhiteSpace($c)) { continue }
    $trimmed = $c.Trim().Trim('"')
    if (-not $gameDirCandidates.Contains($trimmed)) { $gameDirCandidates.Add($trimmed) }
}
$gameDir = $null
foreach ($candidate in $gameDirCandidates) {
    if (Test-Path -LiteralPath (Join-Path $candidate 'data_sts2_windows_x86_64\sts2.dll')) {
        $gameDir = $candidate
        break
    }
}
if (-not $gameDir) {
    throw "Unable to find sts2.dll under any STS2 game-dir candidate: $($gameDirCandidates -join '; ')"
}
$sts2DataDir = Join-Path $gameDir 'data_sts2_windows_x86_64'

# --- Write a per-phase MCP config with the resolved game dir substituted in ---
if ($null -eq $server.env) {
    $server | Add-Member -NotePropertyName env -NotePropertyValue ([pscustomobject]@{})
}
if ($server.env.PSObject.Properties.Name -contains 'STS2_GAME_DIR') {
    $server.env.STS2_GAME_DIR = $gameDir
} else {
    $server.env | Add-Member -NotePropertyName STS2_GAME_DIR -NotePropertyValue $gameDir
}
$mcpConfigRoot = Join-Path $workingDir 'issue-agent-mcp'
New-Item -ItemType Directory -Force -Path $mcpConfigRoot | Out-Null
$mcpConfigPath = Join-Path $mcpConfigRoot "$PhaseName.mcp.json"
[System.IO.File]::WriteAllText(
    $mcpConfigPath,
    ($mcpConfig | ConvertTo-Json -Depth 20),
    (New-Object System.Text.UTF8Encoding($false))
)

# Surface the resolved STS2 paths the implementation/verification prompts read.
$env:ISSUE_AGENT_STS2_GAME_DIR = $gameDir
$env:ISSUE_AGENT_STS2_DATA_DIR = $sts2DataDir

# --- Derive artifact/screenshot/log paths from the persistent working dir ---
# These MUST match what the glimmung-native bash collect/scp steps pull from:
#   sts2-artifacts/   <- run-phases.ps1 writes issue-agent-*.json|md here
#   sts2-screenshots/ <- verification screenshots
$validationArtifactDir = Join-Path $workingDir 'sts2-artifacts'
$screenshotDir = Join-Path $workingDir 'sts2-screenshots'
$logRoot = Join-Path $workingDir 'issue-agent-logs'
New-Item -ItemType Directory -Force -Path $logRoot | Out-Null
$streamLogPath = Join-Path $logRoot "$PhaseName-stream.jsonl"
$debugLogPath = Join-Path $logRoot "$PhaseName-debug.log"
$summaryLogPath = Join-Path $logRoot "$PhaseName-summary.md"

Write-Host "run-issue-agent-phase: phase=$PhaseName claude='$claudeCliPath' gameDir='$gameDir'"
Write-Host "run-issue-agent-phase: mcpConfig='$mcpConfigPath' artifacts='$validationArtifactDir'"

& (Join-Path $RepoRoot '.github\scripts\run-phases.ps1') `
    -PhaseName $PhaseName `
    -IssueNumber $IssueNumber `
    -RepoSlug $RepoSlug `
    -RepoRoot $RepoRoot `
    -ClaudeCliPath $claudeCliPath `
    -McpConfigPath $mcpConfigPath `
    -StreamLogPath $streamLogPath `
    -DebugLogPath $debugLogPath `
    -SummaryLogPath $summaryLogPath `
    -ScreenshotDir $screenshotDir `
    -ValidationArtifactDir $validationArtifactDir

# run-phases.ps1 signals a phase ABORT through the artifact JSON (status:abort)
# and returns normally; it only throws on hard errors. A throw propagates out of
# the `&` above under ErrorActionPreference=Stop and terminates this process
# non-zero before reaching here. So reaching this line means the phase ran and
# wrote its artifact (whatever the verdict) — exit 0 and let the bash collect
# step pull the artifact for the evidence gate to judge. Do not forward
# $LASTEXITCODE: it may carry a stale, already-handled inner exit code.
exit 0
