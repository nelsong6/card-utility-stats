param(
    [string]$EventLogPath,
    [string]$SummaryLogPath,
    [string]$DebugLogPath,
    [string]$ScreenshotDir,
    [string]$ValidationArtifactDir,
    [string]$ArtifactName,
    [string]$RepoSlug,
    [string]$RunId,
    [string]$IssueNumber,
    [string]$HeadSha,
    [string]$RefName
)

$ErrorActionPreference = 'Continue'

function Copy-IfExists {
    param(
        [string]$LiteralPath,
        [string]$Destination
    )

    if ([string]::IsNullOrWhiteSpace($LiteralPath) -or -not (Test-Path -LiteralPath $LiteralPath)) {
        return
    }

    Copy-Item -LiteralPath $LiteralPath -Destination $Destination -Force -ErrorAction SilentlyContinue
}

function Copy-DirectoryIfExists {
    param(
        [string]$LiteralPath,
        [string]$Destination
    )

    if ([string]::IsNullOrWhiteSpace($LiteralPath) -or -not (Test-Path -LiteralPath $LiteralPath)) {
        return
    }

    $target = Join-Path $Destination (Split-Path -Leaf $LiteralPath)
    Copy-Item -LiteralPath $LiteralPath -Destination $target -Recurse -Force -ErrorAction SilentlyContinue
}

function Count-Files {
    param(
        [string]$LiteralPath,
        [string]$Filter = '*'
    )

    if ([string]::IsNullOrWhiteSpace($LiteralPath) -or -not (Test-Path -LiteralPath $LiteralPath)) {
        return 0
    }

    return @(Get-ChildItem -LiteralPath $LiteralPath -Recurse -File -Filter $Filter -ErrorAction SilentlyContinue).Count
}

function Get-ExitCodeText {
    param([string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path) -or -not (Test-Path -LiteralPath $Path)) {
        return '_Unknown_'
    }

    $exitLine = Get-Content -LiteralPath $Path -ErrorAction SilentlyContinue |
        Where-Object { $_ -match '"kind"\s*:\s*"exit"' } |
        Select-Object -Last 1

    if ([string]::IsNullOrWhiteSpace($exitLine)) {
        return '_Unknown_'
    }

    try {
        $record = $exitLine | ConvertFrom-Json -ErrorAction Stop
        $match = [regex]::Match([string]$record.message, '(-?\d+)\s*$')
        if ($match.Success) {
            return $match.Groups[1].Value
        }
        return [string]$record.message
    } catch {
        return '_Unknown_'
    }
}

$runKey = if ([string]::IsNullOrWhiteSpace($RunId)) { (Get-Date).ToString('yyyyMMdd-HHmmss') } else { $RunId }
$persistentRoot = Join-Path 'C:\ProgramData\SpireLens\issue-agent-runs' $runKey
New-Item -ItemType Directory -Force -Path $persistentRoot | Out-Null

Copy-IfExists -LiteralPath $EventLogPath -Destination $persistentRoot
Copy-IfExists -LiteralPath $SummaryLogPath -Destination $persistentRoot
Copy-IfExists -LiteralPath $DebugLogPath -Destination $persistentRoot
Copy-DirectoryIfExists -LiteralPath $ScreenshotDir -Destination $persistentRoot
Copy-DirectoryIfExists -LiteralPath $ValidationArtifactDir -Destination $persistentRoot

Write-Host "Persistent issue-agent logs copied to: $persistentRoot"

$screenshotCount = Count-Files -LiteralPath $ScreenshotDir -Filter '*.png'
$validationArtifactCount = Count-Files -LiteralPath $ValidationArtifactDir
$exitCodeText = Get-ExitCodeText -Path $EventLogPath
$runUrl = if (-not [string]::IsNullOrWhiteSpace($RepoSlug) -and -not [string]::IsNullOrWhiteSpace($RunId)) { "https://github.com/$RepoSlug/actions/runs/$RunId" } else { '_Unavailable_' }
$issueUrl = if (-not [string]::IsNullOrWhiteSpace($RepoSlug) -and -not [string]::IsNullOrWhiteSpace($IssueNumber)) { "https://github.com/$RepoSlug/issues/$IssueNumber" } else { '_Unavailable_' }
$artifactText = if (-not [string]::IsNullOrWhiteSpace($ArtifactName)) { $ArtifactName } else { '_Unavailable_' }

$lines = New-Object System.Collections.Generic.List[string]
$lines.Add('## Issue Agent Summary')
$lines.Add('')
$lines.Add('| Field | Value |')
$lines.Add('| --- | --- |')
$lines.Add("| Issue | [$IssueNumber]($issueUrl) |")
$lines.Add("| Run | [$RunId]($runUrl) |")
$lines.Add("| Artifact | $artifactText |")
$lines.Add("| Claude exit code | $exitCodeText |")
$lines.Add("| Head SHA | $HeadSha |")
$lines.Add("| Ref | $RefName |")
$lines.Add("| Persistent local logs | `$persistentRoot` |")
$lines.Add('')
$lines.Add('### Validation Artifacts')
$lines.Add('')
$lines.Add('| Metric | Value |')
$lines.Add('| --- | ---: |')
$lines.Add("| Screenshot artifacts | $screenshotCount |")
$lines.Add("| Validation artifact files | $validationArtifactCount |")
$lines.Add('')
$lines.Add('### Log Pointers')
$lines.Add('')
$lines.Add('- Event log: `' + $EventLogPath + '`')
$lines.Add('- Summary log: `' + $SummaryLogPath + '`')
$lines.Add('- Debug log: `' + $DebugLogPath + '`')
$lines.Add('- Persistent local directory: `' + $persistentRoot + '`')

$markdown = ($lines -join [Environment]::NewLine) + [Environment]::NewLine

if (-not [string]::IsNullOrWhiteSpace($env:GITHUB_STEP_SUMMARY)) {
    $markdown | Out-File -FilePath $env:GITHUB_STEP_SUMMARY -Encoding utf8 -Append
} else {
    Write-Host $markdown
}
