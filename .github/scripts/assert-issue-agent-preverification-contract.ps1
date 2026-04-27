param(
    [Parameter(Mandatory = $true)][string]$TestPlanPath,
    [Parameter(Mandatory = $true)][string]$ValidationArtifactDir
)

$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force -Path $ValidationArtifactDir | Out-Null
$jsonPath = Join-Path $ValidationArtifactDir 'issue-agent-preverification-contract.json'
$mdPath = Join-Path $ValidationArtifactDir 'issue-agent-preverification-contract.md'
$errors = New-Object System.Collections.Generic.List[string]
$warnings = New-Object System.Collections.Generic.List[string]

$plan = Get-Content -LiteralPath $TestPlanPath -Raw | ConvertFrom-Json
$setup = $plan.scenario_setup
if ($null -eq $setup) { $errors.Add('scenario_setup is required.') | Out-Null }
else {
    $allowedBaseSaves = @('base_regent','base_ironclad','base_silent','base_defect','base_necrobinder')
    if ($allowedBaseSaves -notcontains [string]$setup.base_save_name) { $errors.Add("Unknown base_save_name '$($setup.base_save_name)'.") | Out-Null }
    if ([string]::IsNullOrWhiteSpace([string]$setup.scenario_name)) { $errors.Add('scenario_name is required.') | Out-Null }
    $deck = @($setup.deck)
    if ($deck.Count -eq 0) { $errors.Add('deck must contain at least one card id.') | Out-Null }
    if ($deck.Count -gt 5) { $warnings.Add("Deck has $($deck.Count) cards; verify larger size is justified in scenario_setup.notes.") | Out-Null }
    foreach ($card in $deck) {
        if ([string]::IsNullOrWhiteSpace([string]$card)) { $errors.Add('deck contains a blank card id.') | Out-Null }
        elseif ([string]$card -notmatch '^[A-Z0-9_]+$') { $warnings.Add("Card id '$card' does not look canonical.") | Out-Null }
    }
}

$validEvidenceKinds = @('unit_test','screenshot','live_mcp','manual_blocker')
foreach ($evidence in @($plan.required_evidence)) {
    if ($validEvidenceKinds -notcontains [string]$evidence.kind) { $errors.Add("Unknown evidence kind '$($evidence.kind)' for '$($evidence.id)'.") | Out-Null }
    if ([string]::IsNullOrWhiteSpace([string]$evidence.must_show)) { $errors.Add("Evidence '$($evidence.id)' must include must_show.") | Out-Null }
    if ([string]$evidence.kind -eq 'screenshot' -and $null -eq $evidence.PSObject.Properties['target_visible_required']) { $warnings.Add("Screenshot evidence '$($evidence.id)' should declare target_visible_required.") | Out-Null }
}

$planText = $plan.validation_plan | ConvertTo-Json -Compress -Depth 50
foreach ($forbidden in @('run_scenario_command','list_scenario_commands','dev-console','raw localhost','filesystem queue')) {
    if ($planText -match [regex]::Escape($forbidden)) { $errors.Add("validation_plan references forbidden surface '$forbidden'.") | Out-Null }
}

$status = if ($errors.Count -eq 0) { 'pass' } else { 'fail' }
[ordered]@{ layer='preverification_contract'; status=$status; errors=@($errors); warnings=@($warnings) } | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $jsonPath -Encoding UTF8
$lines = @('# Pre-Verification Contract', '', "Status: **$status**", '')
if ($errors.Count) { $lines += '## Errors'; foreach($e in $errors){$lines += "- $e"}; $lines += '' }
if ($warnings.Count) { $lines += '## Warnings'; foreach($w in $warnings){$lines += "- $w"}; $lines += '' }
$lines -join [Environment]::NewLine | Set-Content -LiteralPath $mdPath -Encoding UTF8
if (-not [string]::IsNullOrWhiteSpace($env:GITHUB_STEP_SUMMARY)) { Get-Content -LiteralPath $mdPath -Raw | Out-File -FilePath $env:GITHUB_STEP_SUMMARY -Encoding utf8 -Append }
if ($status -ne 'pass') { throw "Pre-verification contract failed. See $jsonPath" }