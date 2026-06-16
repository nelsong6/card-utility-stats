param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('run_phase', 'prepare_scenario')]
    [string]$Mode,
    [ValidateSet('test_plan', 'implementation', 'verification')]
    [string]$PhaseName,
    [Parameter(Mandatory = $true)]
    [string]$IssueNumber,
    [Parameter(Mandatory = $true)]
    [string]$RepoSlug,
    # Staged copy of the git_ref harness (.github\scripts\* + .mcp.json). The
    # grader scripts and MCP template are read from HERE, never from RepoRoot,
    # so git_ref controls the harness independently of the feature branch.
    [Parameter(Mandatory = $true)]
    [string]$HarnessRoot,
    # Feature-branch checkout: the CODE UNDER TEST only (Core/, Tests/, ...).
    # Passed onward to run-phases.ps1 / prepare-scenario.ps1 as their RepoRoot.
    [Parameter(Mandatory = $true)]
    [string]$RepoRoot,
    [string]$GitHubToken
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 3.0

# Pure resolver for the harness file layout (scripts + .mcp.json) under
# HarnessRoot. Sourced as a sibling from $PSScriptRoot so the path logic has a
# single, Pester-tested definition rather than being inlined here.
. (Join-Path $PSScriptRoot 'lib' 'HarnessPaths.ps1')

function Add-PathCandidate {
    param(
        [System.Collections.Generic.List[string]]$Candidates,
        [string]$Path
    )

    if ([string]::IsNullOrWhiteSpace($Path)) { return }
    $trimmed = $Path.Trim().Trim('"')
    if ([string]::IsNullOrWhiteSpace($trimmed)) { return }
    if (-not $Candidates.Contains($trimmed)) {
        $Candidates.Add($trimmed)
    }
}

function Resolve-ClaudeCliPath {
    $candidates = [System.Collections.Generic.List[string]]::new()
    Add-PathCandidate $candidates $env:CLAUDE_CLI_PATH
    Add-PathCandidate $candidates $env:CONFIGURED_CLAUDE_CLI_PATH
    Add-PathCandidate $candidates 'D:\automation\claude-code\node_modules\@anthropic-ai\claude-code\bin\claude.exe'
    Add-PathCandidate $candidates 'C:\automation\claude-code\node_modules\@anthropic-ai\claude-code\bin\claude.exe'
    Add-PathCandidate $candidates (Join-Path $env:USERPROFILE 'automation\claude-code\node_modules\@anthropic-ai\claude-code\bin\claude.exe')
    Add-PathCandidate $candidates (Join-Path $env:APPDATA 'npm\node_modules\@anthropic-ai\claude-code\bin\claude.exe')

    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate) {
            return $candidate
        }
    }

    throw "Claude Code CLI was not found. Set CONFIGURED_CLAUDE_CLI_PATH or install Claude under a documented default location."
}

function Resolve-Sts2GameDir {
    # .mcp.json is HARNESS content (git_ref), so the STS2_GAME_DIR default is
    # read from the staged harness, not the feature-branch checkout.
    param([string]$HarnessRoot)

    $gameDirCandidates = [System.Collections.Generic.List[string]]::new()
    Add-PathCandidate $gameDirCandidates $env:SPIRELENS_HOST_STS2_GAME_DIR
    Add-PathCandidate $gameDirCandidates $env:CONFIGURED_STS2_GAME_DIR

    $mcpConfigPath = (Resolve-HarnessPaths -HarnessRoot $HarnessRoot).McpConfigPath
    if (Test-Path -LiteralPath $mcpConfigPath) {
        try {
            $mcpConfig = Get-Content -LiteralPath $mcpConfigPath -Raw | ConvertFrom-Json
            $configuredGameDir = [string]$mcpConfig.mcpServers.'spire-lens-mcp'.env.STS2_GAME_DIR
            Add-PathCandidate $gameDirCandidates $configuredGameDir
        } catch {
            Write-Warning "Unable to read STS2_GAME_DIR from '$mcpConfigPath': $_"
        }
    }

    Add-PathCandidate $gameDirCandidates 'D:\Programs\SteamLibrary\steamapps\common\Slay the Spire 2'
    Add-PathCandidate $gameDirCandidates 'D:\SteamLibrary\steamapps\common\Slay the Spire 2'
    Add-PathCandidate $gameDirCandidates 'C:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2'
    Add-PathCandidate $gameDirCandidates 'C:\Program Files\Steam\steamapps\common\Slay the Spire 2'

    foreach ($candidate in $gameDirCandidates) {
        $sts2Dll = Join-Path $candidate 'data_sts2_windows_x86_64\sts2.dll'
        if (Test-Path -LiteralPath $sts2Dll) {
            return $candidate
        }
    }

    throw "Unable to find sts2.dll in any configured STS2 game directory candidate: $($gameDirCandidates -join '; ')"
}

function New-NativeMcpConfig {
    param(
        # .mcp.json template is HARNESS content (git_ref), not feature-branch.
        [Parameter(Mandatory = $true)][string]$HarnessRoot,
        [Parameter(Mandatory = $true)][string]$GameDir,
        [Parameter(Mandatory = $true)][string]$WorkingDir
    )

    $sourceMcpConfigPath = (Resolve-HarnessPaths -HarnessRoot $HarnessRoot).McpConfigPath
    if (-not (Test-Path -LiteralPath $sourceMcpConfigPath)) {
        throw "MCP config template was not found at '$sourceMcpConfigPath'."
    }

    $mcpConfig = Get-Content -LiteralPath $sourceMcpConfigPath -Raw | ConvertFrom-Json
    $server = $mcpConfig.mcpServers.'spire-lens-mcp'
    if ($null -eq $server) {
        throw "MCP config template '$sourceMcpConfigPath' does not define mcpServers.spire-lens-mcp."
    }
    if ($null -eq $server.env) {
        $server | Add-Member -NotePropertyName env -NotePropertyValue ([pscustomobject]@{})
    }
    if ($server.env.PSObject.Properties.Name -contains 'STS2_GAME_DIR') {
        $server.env.STS2_GAME_DIR = $GameDir
    } else {
        $server.env | Add-Member -NotePropertyName STS2_GAME_DIR -NotePropertyValue $GameDir
    }

    $mcpConfigRoot = Join-Path $WorkingDir 'mcp'
    New-Item -ItemType Directory -Force -Path $mcpConfigRoot | Out-Null
    $path = Join-Path $mcpConfigRoot "$($env:GLIMMUNG_RUN_ID)-$($env:GLIMMUNG_ATTEMPT_INDEX)-SpireLens.mcp.json"
    [System.IO.File]::WriteAllText(
        $path,
        ($mcpConfig | ConvertTo-Json -Depth 20),
        (New-Object System.Text.UTF8Encoding($false))
    )
    return $path
}

function Invoke-CheckedPwshFile {
    param(
        [Parameter(Mandatory = $true)][string]$ScriptPath,
        [Parameter(Mandatory = $true)][string[]]$Arguments
    )

    & pwsh -NoProfile -File $ScriptPath @Arguments
    $exitCode = if ($null -eq $LASTEXITCODE) { 0 } else { [int]$LASTEXITCODE }
    if ($exitCode -ne 0) {
        exit $exitCode
    }
}

if (-not (Test-Path -LiteralPath $RepoRoot)) {
    throw "Repository checkout was not found at '$RepoRoot'."
}
if (-not (Test-Path -LiteralPath $HarnessRoot)) {
    throw "Staged harness was not found at '$HarnessRoot'."
}
if ([string]::IsNullOrWhiteSpace($env:GLIMMUNG_WORKING_DIR)) {
    throw 'GLIMMUNG_WORKING_DIR is not set.'
}

$harness = Resolve-HarnessPaths -HarnessRoot $HarnessRoot

$workingDir = $env:GLIMMUNG_WORKING_DIR
$artifactDir = Join-Path $workingDir 'sts2-artifacts'
$screenshotDir = Join-Path $workingDir 'sts2-screenshots'
$logDir = Join-Path $workingDir 'logs'
New-Item -ItemType Directory -Force -Path $artifactDir, $screenshotDir, $logDir | Out-Null

$gameDir = Resolve-Sts2GameDir -HarnessRoot $HarnessRoot
$env:SPIRELENS_HOST_STS2_GAME_DIR = $gameDir
$env:SPIRELENS_HOST_STS2_DATA_DIR = Join-Path $gameDir 'data_sts2_windows_x86_64'
$mcpConfigPath = New-NativeMcpConfig -HarnessRoot $HarnessRoot -GameDir $gameDir -WorkingDir $workingDir
$env:SPIRELENS_HOST_MCP_CONFIG_PATH = $mcpConfigPath

if (-not [string]::IsNullOrWhiteSpace($GitHubToken)) {
    $env:GH_TOKEN = $GitHubToken
}

switch ($Mode) {
    'run_phase' {
        if ([string]::IsNullOrWhiteSpace($PhaseName)) {
            throw 'PhaseName is required when Mode=run_phase.'
        }
        # run-phases.ps1 is HARNESS — run the git_ref-staged copy. It internally
        # splits $PSScriptRoot (its own lib/, i.e. harness) from -RepoRoot (the
        # code under test: Tests build, agent --add-dir / working dir).
        $runPhasesPath = $harness.RunPhasesPath
        $claudePath = Resolve-ClaudeCliPath
        Invoke-CheckedPwshFile -ScriptPath $runPhasesPath -Arguments @(
            '-PhaseName', $PhaseName,
            '-IssueNumber', $IssueNumber,
            '-RepoSlug', $RepoSlug,
            '-RepoRoot', $RepoRoot,
            '-ClaudeCliPath', $claudePath,
            '-McpConfigPath', $mcpConfigPath,
            '-StreamLogPath', (Join-Path $logDir "$PhaseName-stream.jsonl"),
            '-DebugLogPath', (Join-Path $logDir "$PhaseName-debug.log"),
            '-SummaryLogPath', (Join-Path $logDir "$PhaseName-summary.md"),
            '-ScreenshotDir', $screenshotDir,
            '-ValidationArtifactDir', $artifactDir
        )
    }
    'prepare_scenario' {
        # prepare-scenario.ps1 is HARNESS — run the git_ref-staged copy, and pass
        # it -HarnessRoot so it sources its sibling restart-sts2.ps1 from the
        # harness too. -RepoRoot stays the code-under-test checkout.
        $prepareScenarioPath = $harness.PrepareScenarioPath
        Invoke-CheckedPwshFile -ScriptPath $prepareScenarioPath -Arguments @(
            '-TestPlanPath', (Join-Path $artifactDir 'test-plan.json'),
            '-McpConfigPath', $mcpConfigPath,
            '-HarnessRoot', $HarnessRoot,
            '-RepoRoot', $RepoRoot,
            '-ValidationArtifactDir', $artifactDir,
            '-IssueNumber', $IssueNumber
        )
    }
}
