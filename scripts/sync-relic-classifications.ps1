param(
    [switch]$BuildAndReload
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$sourcePath = Join-Path ([Environment]::GetFolderPath('ApplicationData')) 'SlayTheSpire2\SpireLens\relic-classifications.json'
$destinationPath = Join-Path $repoRoot 'Core\Config\relic-classifications.json'

if (-not (Test-Path -LiteralPath $sourcePath)) {
    throw "Relic classification output does not exist: $sourcePath"
}

$document = Get-Content -LiteralPath $sourcePath -Raw | ConvertFrom-Json
if ($null -eq $document.combat -or $null -eq $document.non_combat) {
    throw 'Relic classification JSON must contain combat and non_combat lists.'
}

$combat = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$nonCombat = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($id in $document.combat) { [void]$combat.Add([string]$id) }
foreach ($id in $document.non_combat) { [void]$nonCombat.Add([string]$id) }

$duplicates = @($combat | Where-Object { $nonCombat.Contains($_) })
if ($duplicates.Count -gt 0) {
    throw "Relics cannot appear in both lists: $($duplicates -join ', ')"
}

if ($null -ne $document.combat_relevant_until_turn) {
    foreach ($property in $document.combat_relevant_until_turn.PSObject.Properties) {
        if (-not $combat.Contains($property.Name)) {
            throw "Combat relevance duration is assigned to a non-combat relic: $($property.Name)"
        }
        $turn = [int]$property.Value
        if ($turn -lt 1 -or $turn -gt 3) {
            throw "Combat relevance duration must be between turns 1 and 3: $($property.Name)=$turn"
        }
    }
}

Copy-Item -LiteralPath $sourcePath -Destination $destinationPath -Force
Write-Host "Copied classifications to $destinationPath"

if (-not $BuildAndReload) {
    exit 0
}

dotnet build (Join-Path $repoRoot 'Core\SpireLens.Core.csproj') -c Debug
if ($LASTEXITCODE -ne 0) {
    throw "SpireLens Core build failed with exit code $LASTEXITCODE."
}

$body = '{"action":"dev_reload_spirelens_core"}'
Invoke-RestMethod `
    -Uri 'http://localhost:15526/api/v1/singleplayer' `
    -Method Post `
    -ContentType 'application/json' `
    -Body $body | ConvertTo-Json -Depth 8
