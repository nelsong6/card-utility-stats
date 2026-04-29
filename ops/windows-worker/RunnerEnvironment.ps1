function Write-IssueAgentRunnerEnvironmentStep {
    param([Parameter(Mandatory = $true)][string]$Message)

    $writeStep = Get-Command Write-Step -ErrorAction SilentlyContinue
    if ($writeStep) {
        Write-Step $Message
        return
    }

    Write-Host "==> $Message"
}

function Add-IssueAgentUniquePathEntry {
    param(
        [System.Collections.Generic.List[string]]$Entries,
        [string]$Entry
    )

    if ([string]::IsNullOrWhiteSpace($Entry)) {
        return
    }

    $trimmed = $Entry.Trim()
    if ([string]::IsNullOrWhiteSpace($trimmed)) {
        return
    }

    $normalized = $trimmed.TrimEnd('\')
    foreach ($existing in $Entries) {
        if ([string]::Equals($existing.TrimEnd('\'), $normalized, [System.StringComparison]::OrdinalIgnoreCase)) {
            return
        }
    }

    $Entries.Add($trimmed)
}

function Add-IssueAgentPathEntries {
    param(
        [System.Collections.Generic.List[string]]$Entries,
        [string]$PathValue
    )

    if ([string]::IsNullOrWhiteSpace($PathValue)) {
        return
    }

    foreach ($entry in ($PathValue -split ';')) {
        Add-IssueAgentUniquePathEntry -Entries $Entries -Entry $entry
    }
}

function Get-IssueAgentRunnerPathValue {
    param([string]$ExistingPathValue)

    $entries = [System.Collections.Generic.List[string]]::new()

    Add-IssueAgentPathEntries -Entries $entries -PathValue $ExistingPathValue
    Add-IssueAgentPathEntries -Entries $entries -PathValue ([Environment]::GetEnvironmentVariable('Path', 'Machine'))
    Add-IssueAgentPathEntries -Entries $entries -PathValue ([Environment]::GetEnvironmentVariable('Path', 'User'))
    Add-IssueAgentPathEntries -Entries $entries -PathValue $env:Path

    if (-not [string]::IsNullOrWhiteSpace($env:LOCALAPPDATA)) {
        Add-IssueAgentUniquePathEntry -Entries $entries -Entry (Join-Path $env:LOCALAPPDATA 'Microsoft\WindowsApps')
    }

    if (-not [string]::IsNullOrWhiteSpace(${env:ProgramFiles})) {
        $commonToolPaths = @(
            (Join-Path ${env:ProgramFiles} 'PowerShell\7'),
            (Join-Path ${env:ProgramFiles} 'Git\cmd'),
            (Join-Path ${env:ProgramFiles} 'GitHub CLI'),
            (Join-Path ${env:ProgramFiles} 'dotnet'),
            (Join-Path ${env:ProgramFiles} 'nodejs')
        )

        foreach ($toolPath in $commonToolPaths) {
            Add-IssueAgentUniquePathEntry -Entries $entries -Entry $toolPath
        }
    }

    return [string]::Join(';', $entries)
}

function Update-IssueAgentRunnerEnvironmentFile {
    param([Parameter(Mandatory = $true)][string]$RunnerRootPath)

    $runnerEnvPath = Join-Path $RunnerRootPath '.env'
    $lines = @()
    if (Test-Path -LiteralPath $runnerEnvPath) {
        $lines = @(Get-Content -LiteralPath $runnerEnvPath)
    }

    $updatedLines = [System.Collections.Generic.List[string]]::new()
    $pathWritten = $false
    foreach ($line in $lines) {
        if ($line -match '(?i)^Path=(.*)$') {
            if (-not $pathWritten) {
                $updatedLines.Add("Path=$(Get-IssueAgentRunnerPathValue -ExistingPathValue $Matches[1])")
                $pathWritten = $true
            }

            continue
        }

        $updatedLines.Add($line)
    }

    if (-not $pathWritten) {
        $updatedLines.Add("Path=$(Get-IssueAgentRunnerPathValue -ExistingPathValue '')")
    }

    $utf8NoBom = [System.Text.UTF8Encoding]::new($false)
    [System.IO.File]::WriteAllLines($runnerEnvPath, [string[]]$updatedLines, $utf8NoBom)
    Write-IssueAgentRunnerEnvironmentStep "Updated runner environment file: $runnerEnvPath"
}
