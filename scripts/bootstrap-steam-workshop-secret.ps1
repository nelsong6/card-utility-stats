param(
    [Parameter(Mandatory = $true)]
    [string] $SteamUsername,

    [string] $Repository = "romaine-life/spirelens",

    [string] $EnvironmentName = "steam-workshop"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$ghCommand = Get-Command gh -ErrorAction SilentlyContinue
if (-not $ghCommand) {
    throw "GitHub CLI (gh) is required."
}

& gh auth status
if ($LASTEXITCODE -ne 0) {
    throw "GitHub CLI is not authenticated."
}

$publisherRoot = Join-Path $env:LOCALAPPDATA "SpireLens/steamcmd-publisher"
$steamArchive = Join-Path $publisherRoot "steamcmd.zip"
$steamExecutable = Join-Path $publisherRoot "steamcmd.exe"
$steamConfig = Join-Path $publisherRoot "config/config.vdf"
New-Item -ItemType Directory -Path $publisherRoot -Force | Out-Null

if (-not (Test-Path -LiteralPath $steamExecutable)) {
    Write-Host "Downloading SteamCMD..."
    Invoke-WebRequest `
        -Uri "https://steamcdn-a.akamaihd.net/client/installer/steamcmd.zip" `
        -OutFile $steamArchive
    Expand-Archive `
        -LiteralPath $steamArchive `
        -DestinationPath $publisherRoot `
        -Force
}

Write-Host ""
Write-Host "Complete the one-time password and Steam Guard login below."
Write-Host "No password or Guard code will be sent to GitHub."

& $steamExecutable "+login" $SteamUsername "+quit"
if ($LASTEXITCODE -ne 0) {
    throw "SteamCMD login failed with exit code $LASTEXITCODE."
}
if (-not (Test-Path -LiteralPath $steamConfig)) {
    throw "SteamCMD did not create its authenticated config.vdf."
}

$credentialBytes = [IO.File]::ReadAllBytes($steamConfig)
$credentialBase64 = [Convert]::ToBase64String($credentialBytes)
if ($credentialBase64.Length -ge 48KB) {
    throw "The isolated SteamCMD credential is too large for a GitHub Actions secret."
}

Write-Host "Creating the protected GitHub environment..."
$environmentConfig = @"
{"deployment_branch_policy":{"protected_branches":false,"custom_branch_policies":true}}
"@
$environmentConfigPath = Join-Path $env:TEMP "spirelens-steam-environment.json"
[IO.File]::WriteAllText(
    $environmentConfigPath,
    $environmentConfig,
    [Text.UTF8Encoding]::new($false))
try {
    & gh api `
    --method PUT `
    "repos/$Repository/environments/$EnvironmentName" `
    --input $environmentConfigPath |
        Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Could not create GitHub environment '$EnvironmentName'."
    }
}
finally {
    Remove-Item -LiteralPath $environmentConfigPath -Force -ErrorAction SilentlyContinue
}

$tagPolicy = & gh api `
    "repos/$Repository/environments/$EnvironmentName/deployment-branch-policies" `
    --jq '.branch_policies[] | select(.name == "v*" and .type == "tag") | .id'
if ($LASTEXITCODE -ne 0) {
    throw "Could not read deployment policies for '$EnvironmentName'."
}
if ([string]::IsNullOrWhiteSpace(($tagPolicy -join ""))) {
    & gh api `
        --method POST `
        "repos/$Repository/environments/$EnvironmentName/deployment-branch-policies" `
        -f 'name=v*' `
        -f 'type=tag' |
        Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Could not restrict '$EnvironmentName' to version tags."
    }
}

Write-Host "Saving the isolated SteamCMD credential..."
$secretProcessInfo = [Diagnostics.ProcessStartInfo]::new()
$secretProcessInfo.FileName = $ghCommand.Source
$secretProcessInfo.Arguments =
    "secret set STEAM_CONFIG_VDF_B64 --repo `"$Repository`" --env `"$EnvironmentName`""
$secretProcessInfo.UseShellExecute = $false
$secretProcessInfo.RedirectStandardInput = $true
$secretProcessInfo.RedirectStandardOutput = $true
$secretProcessInfo.RedirectStandardError = $true
$secretProcess = [Diagnostics.Process]::new()
$secretProcess.StartInfo = $secretProcessInfo
try {
    [void] $secretProcess.Start()
    $secretProcess.StandardInput.Write($credentialBase64)
    $secretProcess.StandardInput.Close()
    $secretOutput = $secretProcess.StandardOutput.ReadToEnd()
    $secretError = $secretProcess.StandardError.ReadToEnd()
    $secretProcess.WaitForExit()
    if ($secretProcess.ExitCode -ne 0) {
        throw "Could not save STEAM_CONFIG_VDF_B64. $secretOutput $secretError"
    }
}
finally {
    $secretProcess.Dispose()
}

& gh variable set STEAM_USERNAME `
    --body $SteamUsername `
    --repo $Repository `
    --env $EnvironmentName
if ($LASTEXITCODE -ne 0) {
    throw "Could not save STEAM_USERNAME."
}

Write-Host ""
Write-Host "Steam Workshop publishing credentials are configured."
Write-Host "Rerun this script if Steam invalidates or rotates the saved login."
