<#
.SYNOPSIS
Downloads the latest packaged SpireLens build and installs it fresh into STS2 mods.

.DESCRIPTION
Reads a dotenv-style env file, resolves the local Slay the Spire 2 mods path,
downloads either the latest GitHub release asset or latest successful workflow
artifact, validates the package, removes only mods/SpireLens, and installs the
fresh SpireLens folder.

Useful env vars:
  SPIRELENS_STS2_MODS_DIR=D:\SteamLibrary\steamapps\common\Slay the Spire 2\mods
  SPIRELENS_STS2_GAME_DIR=D:\SteamLibrary\steamapps\common\Slay the Spire 2
  SPIRELENS_RELEASE_REPO=nelsong6/spirelens
  SPIRELENS_DOWNLOAD_SOURCE=LatestRelease
  GITHUB_TOKEN=... # required for LatestWorkflowArtifact

.EXAMPLE
pwsh .github/scripts/install-latest-spirelens.ps1 -EnvFile .env.local -Force

.EXAMPLE
pwsh .github/scripts/install-latest-spirelens.ps1 -EnvFile .env.local -Source LatestWorkflowArtifact -Force
#>

param(
    [Parameter(Mandatory = $true)]
    [string]$EnvFile,

    [string]$Repo = '',

    [string]$AssetName = '',

    [ValidateSet('', 'LatestRelease', 'LatestWorkflowArtifact')]
    [string]$Source = '',

    [switch]$Force
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 3.0

function Import-DotEnvFile {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "Env file was not found: $Path"
    }

    $lineNumber = 0
    foreach ($line in Get-Content -LiteralPath $Path) {
        $lineNumber++
        $trimmed = $line.Trim()
        if ([string]::IsNullOrWhiteSpace($trimmed) -or $trimmed.StartsWith('#')) {
            continue
        }

        if ($trimmed.StartsWith('export ')) {
            $trimmed = $trimmed.Substring(7).TrimStart()
        }

        $equalsIndex = $trimmed.IndexOf('=')
        if ($equalsIndex -lt 1) {
            throw "Invalid env file line $lineNumber. Expected KEY=VALUE."
        }

        $name = $trimmed.Substring(0, $equalsIndex).Trim()
        $value = $trimmed.Substring($equalsIndex + 1).Trim()

        if ($name -notmatch '^[A-Za-z_][A-Za-z0-9_]*$') {
            throw "Invalid env var name '$name' on line $lineNumber."
        }

        if ($value.Length -ge 2) {
            $first = $value[0]
            $last = $value[$value.Length - 1]
            if (($first -eq "'" -and $last -eq "'") -or ($first -eq '"' -and $last -eq '"')) {
                $value = $value.Substring(1, $value.Length - 2)
            }
        }

        [Environment]::SetEnvironmentVariable($name, $value, 'Process')
    }
}

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

function Test-Sts2GameDir {
    param([Parameter(Mandatory = $true)][string]$Path)

    $windowsDll = Join-Path $Path 'data_sts2_windows_x86_64\sts2.dll'
    $linuxDll = Join-Path $Path 'data_sts2_linuxbsd_x86_64\sts2.dll'
    $macDll = Join-Path $Path 'SlayTheSpire2.app\Contents\Resources\data_sts2_macos_x86_64\sts2.dll'

    return (
        (Test-Path -LiteralPath $windowsDll) -or
        (Test-Path -LiteralPath $linuxDll) -or
        (Test-Path -LiteralPath $macDll)
    )
}

function Resolve-Sts2ModsDir {
    $modsDirCandidates = [System.Collections.Generic.List[string]]::new()
    Add-PathCandidate $modsDirCandidates $env:SPIRELENS_STS2_MODS_DIR
    Add-PathCandidate $modsDirCandidates $env:STS2_MODS_DIR
    Add-PathCandidate $modsDirCandidates $env:MODS_DIR

    foreach ($candidate in $modsDirCandidates) {
        $leaf = Split-Path -Leaf $candidate.TrimEnd('\', '/')
        if ($leaf -ne 'mods') {
            throw "Configured mods directory does not end in 'mods': $candidate"
        }
        return $candidate
    }

    $gameDirCandidates = [System.Collections.Generic.List[string]]::new()
    Add-PathCandidate $gameDirCandidates $env:SPIRELENS_STS2_GAME_DIR
    Add-PathCandidate $gameDirCandidates $env:STS2_GAME_DIR
    Add-PathCandidate $gameDirCandidates $env:CONFIGURED_STS2_GAME_DIR

    $repoRoot = Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')
    $mcpConfigPath = Join-Path $repoRoot '.mcp.json'
    if (Test-Path -LiteralPath $mcpConfigPath) {
        try {
            $mcpConfig = Get-Content -LiteralPath $mcpConfigPath -Raw | ConvertFrom-Json
            Add-PathCandidate $gameDirCandidates ([string]$mcpConfig.mcpServers.'spire-lens-mcp'.env.STS2_GAME_DIR)
        } catch {
            Write-Warning "Unable to read STS2_GAME_DIR from '$mcpConfigPath': $($_.Exception.Message)"
        }
    }

    Add-PathCandidate $gameDirCandidates 'D:\Programs\SteamLibrary\steamapps\common\Slay the Spire 2'
    Add-PathCandidate $gameDirCandidates 'D:\SteamLibrary\steamapps\common\Slay the Spire 2'
    Add-PathCandidate $gameDirCandidates 'C:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2'
    Add-PathCandidate $gameDirCandidates 'C:\Program Files\Steam\steamapps\common\Slay the Spire 2'

    foreach ($candidate in $gameDirCandidates) {
        if (Test-Sts2GameDir -Path $candidate) {
            return (Join-Path $candidate 'mods')
        }
    }

    throw "Unable to resolve the Slay the Spire 2 mods directory. Set SPIRELENS_STS2_MODS_DIR or SPIRELENS_STS2_GAME_DIR in '$EnvFile'."
}

function Get-AuthHeaders {
    $headers = @{
        Accept               = 'application/vnd.github+json'
        'X-GitHub-Api-Version' = '2022-11-28'
        'User-Agent'         = 'spirelens-installer'
    }

    $token = $env:GITHUB_TOKEN
    if ([string]::IsNullOrWhiteSpace($token)) {
        $token = $env:GH_TOKEN
    }
    if (-not [string]::IsNullOrWhiteSpace($token)) {
        $headers.Authorization = "Bearer $token"
    }

    return $headers
}

function Get-LatestReleaseAsset {
    param(
        [Parameter(Mandatory = $true)][string]$RepoSlug,
        [Parameter(Mandatory = $true)][string]$RequiredAssetName
    )

    $uri = "https://api.github.com/repos/$RepoSlug/releases/latest"
    Write-Host "Resolving latest GitHub release from $uri"
    $release = Invoke-RestMethod -Uri $uri -Headers (Get-AuthHeaders)
    $asset = @($release.assets | Where-Object { $_.name -eq $RequiredAssetName } | Select-Object -First 1)
    if ($asset.Count -eq 0) {
        $available = @($release.assets | ForEach-Object { $_.name }) -join ', '
        throw "Latest release '$($release.tag_name)' does not contain '$RequiredAssetName'. Available assets: $available"
    }

    [pscustomobject]@{
        TagName     = [string]$release.tag_name
        Name        = [string]$asset[0].name
        DownloadUrl = [string]$asset[0].browser_download_url
    }
}

function Get-LatestWorkflowArtifact {
    param(
        [Parameter(Mandatory = $true)][string]$RepoSlug,
        [Parameter(Mandatory = $true)][string]$ArtifactName
    )

    if ([string]::IsNullOrWhiteSpace($env:GITHUB_TOKEN) -and [string]::IsNullOrWhiteSpace($env:GH_TOKEN)) {
        throw 'LatestWorkflowArtifact downloads require GITHUB_TOKEN or GH_TOKEN in the env file.'
    }

    $workflow = $env:SPIRELENS_WORKFLOW_FILE
    if ([string]::IsNullOrWhiteSpace($workflow)) {
        $workflow = 'release.yml'
    }

    $branch = $env:SPIRELENS_BUILD_BRANCH
    $branchQuery = ''
    if (-not [string]::IsNullOrWhiteSpace($branch)) {
        $branchQuery = "&branch=$([System.Uri]::EscapeDataString($branch))"
    }

    $runsUri = "https://api.github.com/repos/$RepoSlug/actions/workflows/$workflow/runs?status=success&per_page=10$branchQuery"
    Write-Host "Resolving latest successful workflow run from $runsUri"
    $runs = Invoke-RestMethod -Uri $runsUri -Headers (Get-AuthHeaders)
    $run = @($runs.workflow_runs | Select-Object -First 1)
    if ($run.Count -eq 0) {
        throw "No successful workflow runs found for $RepoSlug workflow '$workflow'."
    }

    $artifactsUri = "https://api.github.com/repos/$RepoSlug/actions/runs/$($run[0].id)/artifacts?per_page=100"
    Write-Host "Resolving artifact '$ArtifactName' from run $($run[0].id)"
    $artifacts = Invoke-RestMethod -Uri $artifactsUri -Headers (Get-AuthHeaders)
    $artifact = @($artifacts.artifacts | Where-Object { $_.name -eq $ArtifactName -and -not $_.expired } | Select-Object -First 1)
    if ($artifact.Count -eq 0) {
        $available = @($artifacts.artifacts | ForEach-Object { "$($_.name) (expired=$($_.expired))" }) -join ', '
        throw "Workflow run $($run[0].id) does not contain a non-expired '$ArtifactName' artifact. Available artifacts: $available"
    }

    [pscustomobject]@{
        Label       = "workflow run $($run[0].id)"
        Name        = "$ArtifactName.zip"
        DownloadUrl = [string]$artifact[0].archive_download_url
    }
}

function Test-ZipContainsEntry {
    param(
        [Parameter(Mandatory = $true)][System.IO.Compression.ZipArchive]$Zip,
        [Parameter(Mandatory = $true)][string]$EntryName
    )

    $normalized = $EntryName -replace '\\', '/'
    return $null -ne ($Zip.Entries | Where-Object { ($_.FullName -replace '\\', '/') -eq $normalized } | Select-Object -First 1)
}

function Test-SpireLensPackageZip {
    param([Parameter(Mandatory = $true)][string]$Path)

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [System.IO.Compression.ZipFile]::OpenRead($Path)
    try {
        foreach ($requiredEntry in @('SpireLens/SpireLens.dll', 'SpireLens/SpireLens.json', 'SpireLens/SpireLens.Core.dll')) {
            if (-not (Test-ZipContainsEntry -Zip $zip -EntryName $requiredEntry)) {
                return $false
            }
        }
        return $true
    } finally {
        $zip.Dispose()
    }
}

function Resolve-SpireLensPackageZip {
    param(
        [Parameter(Mandatory = $true)][string]$DownloadedZipPath,
        [Parameter(Mandatory = $true)][string]$WorkRoot,
        [Parameter(Mandatory = $true)][string]$NestedAssetName
    )

    if (Test-SpireLensPackageZip -Path $DownloadedZipPath) {
        return $DownloadedZipPath
    }

    $artifactExtractRoot = Join-Path $WorkRoot 'artifact'
    New-Item -ItemType Directory -Force -Path $artifactExtractRoot | Out-Null
    Expand-Archive -LiteralPath $DownloadedZipPath -DestinationPath $artifactExtractRoot -Force

    $nestedPackage = Get-ChildItem -LiteralPath $artifactExtractRoot -Recurse -File |
        Where-Object { $_.Name -eq $NestedAssetName } |
        Select-Object -First 1
    if ($null -eq $nestedPackage) {
        throw "Downloaded archive is not a SpireLens package and does not contain nested '$NestedAssetName'."
    }

    if (-not (Test-SpireLensPackageZip -Path $nestedPackage.FullName)) {
        throw "Nested archive '$($nestedPackage.FullName)' is missing required SpireLens entries."
    }

    return $nestedPackage.FullName
}

Import-DotEnvFile -Path $EnvFile

if ([string]::IsNullOrWhiteSpace($Repo)) {
    $Repo = $env:SPIRELENS_RELEASE_REPO
}
if ([string]::IsNullOrWhiteSpace($Repo)) {
    $Repo = 'nelsong6/spirelens'
}
if ($Repo -notmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$') {
    throw "Invalid GitHub repo slug '$Repo'. Expected owner/repo."
}

if ([string]::IsNullOrWhiteSpace($AssetName)) {
    $AssetName = $env:SPIRELENS_RELEASE_ASSET
}
if ([string]::IsNullOrWhiteSpace($AssetName)) {
    $AssetName = 'SpireLens.zip'
}

if ([string]::IsNullOrWhiteSpace($Source)) {
    $Source = $env:SPIRELENS_DOWNLOAD_SOURCE
}
if ([string]::IsNullOrWhiteSpace($Source)) {
    $Source = 'LatestRelease'
}
if ($Source -notin @('LatestRelease', 'LatestWorkflowArtifact')) {
    throw "Invalid SPIRELENS_DOWNLOAD_SOURCE '$Source'. Expected LatestRelease or LatestWorkflowArtifact."
}

$modsDir = Resolve-Sts2ModsDir
$modsDirFull = [System.IO.Path]::GetFullPath($modsDir)
New-Item -ItemType Directory -Force -Path $modsDirFull | Out-Null

$installDir = Join-Path $modsDirFull 'SpireLens'
$installDirFull = [System.IO.Path]::GetFullPath($installDir)
if (-not $installDirFull.StartsWith(($modsDirFull.TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar), [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Resolved install directory escaped the mods directory: $installDirFull"
}
if ((Split-Path -Leaf $installDirFull) -ne 'SpireLens') {
    throw "Refusing to install to unexpected directory: $installDirFull"
}

if ($Source -eq 'LatestWorkflowArtifact') {
    $workflowArtifactName = $env:SPIRELENS_WORKFLOW_ARTIFACT
    if ([string]::IsNullOrWhiteSpace($workflowArtifactName)) {
        $workflowArtifactName = 'SpireLens-Package'
    }
    $download = Get-LatestWorkflowArtifact -RepoSlug $Repo -ArtifactName $workflowArtifactName
} else {
    $releaseAsset = Get-LatestReleaseAsset -RepoSlug $Repo -RequiredAssetName $AssetName
    $download = [pscustomobject]@{
        Label       = "release $($releaseAsset.TagName)"
        Name        = $releaseAsset.Name
        DownloadUrl = $releaseAsset.DownloadUrl
    }
}

$workRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("spirelens-install-" + [guid]::NewGuid().ToString('N'))
$zipPath = Join-Path $workRoot $download.Name
$extractRoot = Join-Path $workRoot 'extract'
New-Item -ItemType Directory -Force -Path $workRoot, $extractRoot | Out-Null

try {
    Write-Host "Downloading $($download.Name) from $Repo $($download.Label)"
    Invoke-WebRequest -Uri $download.DownloadUrl -Headers (Get-AuthHeaders) -OutFile $zipPath

    $packageZipPath = Resolve-SpireLensPackageZip -DownloadedZipPath $zipPath -WorkRoot $workRoot -NestedAssetName $AssetName

    Expand-Archive -LiteralPath $packageZipPath -DestinationPath $extractRoot -Force
    $packageRoot = Join-Path $extractRoot 'SpireLens'
    if (-not (Test-Path -LiteralPath $packageRoot -PathType Container)) {
        throw "Downloaded archive did not extract a top-level SpireLens directory."
    }

    if ((Test-Path -LiteralPath $installDirFull) -and -not $Force) {
        throw "SpireLens is already installed at '$installDirFull'. Re-run with -Force to replace it."
    }

    if (Test-Path -LiteralPath $installDirFull) {
        Write-Host "Removing existing SpireLens install: $installDirFull"
        Remove-Item -LiteralPath $installDirFull -Recurse -Force -ErrorAction Stop
    }

    Write-Host "Installing fresh SpireLens build into $installDirFull"
    Copy-Item -LiteralPath $packageRoot -Destination $modsDirFull -Recurse -Force

    Write-Host "Installed SpireLens from $Repo $($download.Label)."
} finally {
    if (Test-Path -LiteralPath $workRoot) {
        Remove-Item -LiteralPath $workRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
