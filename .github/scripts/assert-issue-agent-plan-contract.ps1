param(
    [Parameter(Mandatory = $true)][string]$TestPlanPath,
    [Parameter(Mandatory = $true)][string]$ValidationArtifactDir
)

$ErrorActionPreference = 'Stop'

function ConvertTo-FlatText {
    param([object]$Value)
    if ($null -eq $Value) { return '' }
    if ($Value -is [string]) { return $Value }
    try { return ($Value | ConvertTo-Json -Compress -Depth 50) } catch { return [string]$Value }
}

New-Item -ItemType Directory -Force -Path $ValidationArtifactDir | Out-Null
$resultPath = Join-Path $ValidationArtifactDir 'issue-agent-plan-contract.json'
$markdownPath = Join-Path $ValidationArtifactDir 'issue-agent-plan-contract.md'
$errors = New-Object System.Collections.Generic.List[string]
$warnings = New-Object System.Collections.Generic.List[string]

if (-not (Test-Path -LiteralPath $TestPlanPath)) { throw "Test plan not found: $TestPlanPath" }
$plan = Get-Content -LiteralPath $TestPlanPath -Raw | ConvertFrom-Json

$forbiddenPatterns = @(
    @{ Pattern = '(?i)\brun_scenario_command\b'; Reason = 'run_scenario_command is not available to verification.' },
    @{ Pattern = '(?i)\blist_scenario_commands\b'; Reason = 'scenario-command discovery is not available to verification.' },
    @{ Pattern = '(?i)\bdev[- ]?console\b'; Reason = 'dev-console setup is not an allowed validation-plan action.' },
    @{ Pattern = '(?i)\braw localhost\b|\blocalhost bridge\b|\bfilesystem queue\b'; Reason = 'raw bridge/queue surfaces are forbidden.' },
    @{ Pattern = '(?i)\bgame input\b|\bmouse/hover\b|\barbitrary indices\b'; Reason = 'validation must name explicit MCP tools/surfaces rather than generic game input.' }
)

$steps = @($plan.validation_plan)
for ($i = 0; $i -lt $steps.Count; $i++) {
    $step = $steps[$i]
    $text = ConvertTo-FlatText $step
    foreach ($forbidden in $forbiddenPatterns) {
        if ($text -match $forbidden.Pattern) {
            $label = if ($step.step) { "step $($step.step)" } else { "step index $i" }
            $errors.Add("${label}: $($forbidden.Reason) Text: $text") | Out-Null
        }
    }
}

$requiredEvidence = @($plan.required_evidence)
if ($requiredEvidence.Count -eq 0) { $errors.Add('required_evidence must contain at least one evidence item.') | Out-Null }
foreach ($evidence in $requiredEvidence) {
    foreach ($field in @('id', 'kind', 'required', 'must_show')) {
        if ($null -eq $evidence.PSObject.Properties[$field]) { $errors.Add("required_evidence item is missing '$field'.") | Out-Null }
    }
}

$status = if ($errors.Count -eq 0) { 'pass' } else { 'fail' }
$result = [ordered]@{ layer='plan_contract'; status=$status; errors=@($errors); warnings=@($warnings); checked_validation_steps=$steps.Count; checked_required_evidence=$requiredEvidence.Count }
$result | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $resultPath -Encoding UTF8

$lines = @('# Issue Agent Plan Contract', '', "Status: **$status**", '')
if ($errors.Count -gt 0) { $lines += '## Errors'; foreach ($e in $errors) { $lines += "- $e" }; $lines += '' }
if ($warnings.Count -gt 0) { $lines += '## Warnings'; foreach ($w in $warnings) { $lines += "- $w" }; $lines += '' }
$lines -join [Environment]::NewLine | Set-Content -LiteralPath $markdownPath -Encoding UTF8
if (-not [string]::IsNullOrWhiteSpace($env:GITHUB_STEP_SUMMARY)) { Get-Content -LiteralPath $markdownPath -Raw | Out-File -FilePath $env:GITHUB_STEP_SUMMARY -Encoding utf8 -Append }
if ($status -ne 'pass') { throw "Issue-agent test plan failed deterministic contract checks. See $resultPath" }