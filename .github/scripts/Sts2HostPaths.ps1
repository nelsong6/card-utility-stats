# Shared STS2 game-dir + MCP-config resolution for the glimmung-native laptop
# phases. Dot-source it:
#
#     . (Join-Path $PSScriptRoot 'Sts2HostPaths.ps1')
#
# This is the single source of truth for two things every laptop-side phase
# needs to agree on:
#
#   1. Resolve-Sts2GameDir  - the ordered STS2 game-directory candidate list,
#      probed by testing for data_sts2_windows_x86_64\sts2.dll.
#   2. Write-ResolvedMcpConfig - clone the repo's .mcp.json template and
#      substitute the resolved STS2_GAME_DIR into it.
#
# The .mcp.json template ships a placeholder STS2_GAME_DIR that need not match
# any particular laptop, so EVERY consumer must resolve + substitute before
# handing the config to restart-sts2.ps1 or the spire-lens MCP server. Three
# call sites rely on this module so discovery is identical everywhere:
# prepare-host.ps1 (env-prep), run-issue-agent-phase.ps1 (the phase wrapper),
# and verify.sh's prepare_scenario step. Previously each had its own copy (or,
# in prepare_scenario's case, no resolution at all and a missing -McpConfigPath).

Set-StrictMode -Version 3.0

function Add-Sts2PathCandidate {
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

function Resolve-Sts2GameDir {
    param([Parameter(Mandatory = $true)][string]$RepoRoot)

    $gameDirCandidates = [System.Collections.Generic.List[string]]::new()
    Add-Sts2PathCandidate $gameDirCandidates $env:ISSUE_AGENT_STS2_GAME_DIR
    Add-Sts2PathCandidate $gameDirCandidates $env:CONFIGURED_STS2_GAME_DIR

    $mcpConfigPath = Join-Path $RepoRoot '.mcp.json'
    if (Test-Path -LiteralPath $mcpConfigPath) {
        try {
            $mcpConfig = Get-Content -LiteralPath $mcpConfigPath -Raw | ConvertFrom-Json
            $configuredGameDir = [string]$mcpConfig.mcpServers.'spire-lens-mcp'.env.STS2_GAME_DIR
            Add-Sts2PathCandidate $gameDirCandidates $configuredGameDir
        } catch {
            Write-Warning "Unable to read STS2_GAME_DIR from '$mcpConfigPath': $_"
        }
    }

    Add-Sts2PathCandidate $gameDirCandidates 'D:\Programs\SteamLibrary\steamapps\common\Slay the Spire 2'
    Add-Sts2PathCandidate $gameDirCandidates 'D:\SteamLibrary\steamapps\common\Slay the Spire 2'
    Add-Sts2PathCandidate $gameDirCandidates 'C:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2'
    Add-Sts2PathCandidate $gameDirCandidates 'C:\Program Files\Steam\steamapps\common\Slay the Spire 2'

    foreach ($candidate in $gameDirCandidates) {
        $sts2Dll = Join-Path $candidate 'data_sts2_windows_x86_64\sts2.dll'
        if (Test-Path -LiteralPath $sts2Dll) {
            $item = Get-Item -LiteralPath $sts2Dll
            Write-Host "Using STS2 game directory: $candidate"
            Write-Host "Using STS2 assembly: $sts2Dll"
            Write-Host "STS2 product version: $($item.VersionInfo.ProductVersion)"
            return $candidate
        }

        Write-Host "Skipping STS2 candidate without sts2.dll: $candidate"
    }

    throw "Unable to find sts2.dll in any configured STS2 game directory candidate: $($gameDirCandidates -join '; ')"
}

function Write-ResolvedMcpConfig {
    param(
        [Parameter(Mandatory = $true)][string]$RepoRoot,
        [Parameter(Mandatory = $true)][string]$GameDir,
        [Parameter(Mandatory = $true)][string]$OutPath
    )

    $sourceMcpConfigPath = Join-Path $RepoRoot '.mcp.json'
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

    $outDir = Split-Path -Parent $OutPath
    if (-not [string]::IsNullOrWhiteSpace($outDir)) {
        New-Item -ItemType Directory -Force -Path $outDir | Out-Null
    }
    [System.IO.File]::WriteAllText(
        $OutPath,
        ($mcpConfig | ConvertTo-Json -Depth 20),
        (New-Object System.Text.UTF8Encoding($false))
    )

    Write-Host "Generated MCP config: $OutPath"
    Write-Host "MCP config STS2_GAME_DIR: $GameDir"
    return $OutPath
}
