[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $Repository,

    [Parameter(Mandatory = $true)]
    [string] $CurrentTag,

    [Parameter(Mandatory = $true)]
    [string] $OutputPath,

    [string] $GitHubToken = $env:GITHUB_TOKEN,

    [string] $GitHubApiUrl = "https://api.github.com"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

if ([string]::IsNullOrWhiteSpace($GitHubToken)) {
    throw "A GitHub token is required to resolve pull requests for release notes."
}

function Invoke-Git {
    param(
        [Parameter(Mandatory = $true)]
        [string[]] $Arguments,

        [switch] $AllowFailure
    )

    $output = @(& git @Arguments 2>$null)
    if ($LASTEXITCODE -ne 0 -and -not $AllowFailure) {
        throw "git $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }

    return $output
}

function Invoke-GitHubGet {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Path
    )

    $headers = @{
        Accept = "application/vnd.github+json"
        Authorization = "Bearer $GitHubToken"
        "X-GitHub-Api-Version" = "2022-11-28"
        "User-Agent" = "SpireLens-release-notes"
    }

    return Invoke-RestMethod `
        -Method Get `
        -Uri "$($GitHubApiUrl.TrimEnd('/'))/$($Path.TrimStart('/'))" `
        -Headers $headers
}

function Get-WorkshopSection {
    param(
        [AllowNull()]
        [string] $Body
    )

    if ([string]::IsNullOrWhiteSpace($Body)) {
        return $null
    }

    $match = [regex]::Match(
        $Body,
        "(?ims)^\s*##\s+Workshop notes\s*\r?\n(?<content>.*?)(?=^\s*##\s+|\z)")
    if (-not $match.Success) {
        return $null
    }

    $content = [regex]::Replace(
        $match.Groups["content"].Value,
        "(?s)<!--.*?-->",
        "")
    return $content.Trim()
}

function Test-NoPlayerFacingChanges {
    param(
        [AllowEmptyString()]
        [string] $Content
    )

    $normalized = $Content.Trim()
    $normalized = [regex]::Replace($normalized, "^\s*[-*+]\s*", "")
    $normalized = $normalized.Trim().TrimEnd(".")
    return (
        $normalized -ieq "No player-facing changes" -or
        $normalized -ieq "None" -or
        $normalized -ieq "N/A")
}

function ConvertTo-ReleaseNoteLines {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Content
    )

    $notes = [System.Collections.Generic.List[string]]::new()
    foreach ($rawLine in ($Content -split "\r?\n")) {
        $line = $rawLine.Trim()
        if ([string]::IsNullOrWhiteSpace($line)) {
            continue
        }

        $line = [regex]::Replace($line, "^[-*+]\s+", "")
        $line = [regex]::Replace($line, "^\d+[.)]\s+", "")
        $line = [regex]::Replace($line, "^\[[ xX]\]\s*", "")
        if (-not [string]::IsNullOrWhiteSpace($line)) {
            $notes.Add("- $line")
        }
    }

    return $notes.ToArray()
}

$previousTagOutput = @(
    Invoke-Git `
        -Arguments @("describe", "--tags", "--abbrev=0", "$CurrentTag^") `
        -AllowFailure
)
$previousTag = if ($previousTagOutput.Count -gt 0) {
    $previousTagOutput[0].Trim()
}
else {
    $null
}

$revisionRange = if ([string]::IsNullOrWhiteSpace($previousTag)) {
    $CurrentTag
}
else {
    "$previousTag..$CurrentTag"
}

$repositoryDetails = Invoke-GitHubGet -Path "repos/$Repository"
$defaultBranch = [string] $repositoryDetails.default_branch
if ([string]::IsNullOrWhiteSpace($defaultBranch)) {
    throw "GitHub did not report a default branch for '$Repository'."
}

$commitShas = @(
    Invoke-Git -Arguments @("rev-list", "--reverse", $revisionRange)
)
if ($commitShas.Count -eq 0) {
    throw "No commits were found for release range '$revisionRange'."
}

$pullRequestsByNumber = @{}
$directCommitSubjects = [System.Collections.Generic.List[string]]::new()

foreach ($commitSha in $commitShas) {
    $associatedPullRequests = @(
        Invoke-GitHubGet -Path "repos/$Repository/commits/$commitSha/pulls"
    )
    $mergedPullRequests = @(
        $associatedPullRequests |
            Where-Object {
                $null -ne $_.merged_at -and
                $_.base.ref -eq $defaultBranch
            }
    )

    if ($mergedPullRequests.Count -eq 0) {
        $subject = @(
            Invoke-Git -Arguments @("show", "-s", "--format=%s", $commitSha)
        )[0].Trim()
        if (-not [string]::IsNullOrWhiteSpace($subject)) {
            $directCommitSubjects.Add($subject)
        }
        continue
    }

    foreach ($pullRequest in $mergedPullRequests) {
        $pullRequestsByNumber[[int] $pullRequest.number] = $pullRequest
    }
}

$releaseNoteLines = [System.Collections.Generic.List[string]]::new()
$seenReleaseNoteLines = [System.Collections.Generic.HashSet[string]]::new(
    [System.StringComparer]::OrdinalIgnoreCase)

$orderedPullRequests = @(
    $pullRequestsByNumber.Values |
        Sort-Object `
            @{ Expression = { [DateTimeOffset] $_.merged_at } },
            @{ Expression = { [int] $_.number } }
)

foreach ($pullRequest in $orderedPullRequests) {
    $workshopSection = Get-WorkshopSection -Body $pullRequest.body
    if ($null -ne $workshopSection -and
        (Test-NoPlayerFacingChanges -Content $workshopSection)) {
        continue
    }

    $candidateLines = if ([string]::IsNullOrWhiteSpace($workshopSection)) {
        @("- $($pullRequest.title) (#$($pullRequest.number))")
    }
    else {
        @(ConvertTo-ReleaseNoteLines -Content $workshopSection)
    }

    foreach ($candidateLine in $candidateLines) {
        if ($seenReleaseNoteLines.Add($candidateLine)) {
            $releaseNoteLines.Add($candidateLine)
        }
    }
}

foreach ($directCommitSubject in $directCommitSubjects) {
    $candidateLine = "- $directCommitSubject"
    if ($seenReleaseNoteLines.Add($candidateLine)) {
        $releaseNoteLines.Add($candidateLine)
    }
}

if ($releaseNoteLines.Count -eq 0) {
    $releaseNoteLines.Add("- No player-facing changes.")
}

$resolvedOutputPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath(
    $OutputPath)
$outputDirectory = Split-Path -Parent $resolvedOutputPath
if (-not [string]::IsNullOrWhiteSpace($outputDirectory)) {
    New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
}

$releaseNoteText = ($releaseNoteLines -join "`n") + "`n"
[IO.File]::WriteAllText(
    $resolvedOutputPath,
    $releaseNoteText,
    [Text.UTF8Encoding]::new($false))

Write-Host "Release notes written for '$revisionRange':"
Write-Host $releaseNoteText
