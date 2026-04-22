[CmdletBinding()]
param(
    [string]$RepoRoot = "D:\repos\card-utility-stats",
    [string]$TaskName = "Codex Issue Queue Worker",
    [string]$WorkerName = "",
    [string]$DashboardEventUrl = "",
    [int]$IntervalMinutes = 30
)

$ErrorActionPreference = "Stop"

$scriptPath = Join-Path $RepoRoot "ops\codex-queue\Run-IssueQueueWorker.ps1"
if (-not (Test-Path -LiteralPath $scriptPath)) {
    throw "Worker script not found at $scriptPath"
}

$arguments = "-NoLogo -NoProfile -ExecutionPolicy Bypass -File `"$scriptPath`" -RepoRoot `"$RepoRoot`""
if (-not [string]::IsNullOrWhiteSpace($WorkerName)) {
    $arguments += " -WorkerName `"$WorkerName`""
}
if (-not [string]::IsNullOrWhiteSpace($DashboardEventUrl)) {
    $arguments += " -DashboardEventUrl `"$DashboardEventUrl`""
}

$action = New-ScheduledTaskAction -Execute "powershell.exe" -Argument $arguments
$trigger = New-ScheduledTaskTrigger -Once -At ((Get-Date).AddMinutes(1)) -RepetitionInterval (New-TimeSpan -Minutes $IntervalMinutes)
$settings = New-ScheduledTaskSettingsSet -StartWhenAvailable

$existingTask = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
if ($existingTask) {
    Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false
}

Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger -Settings $settings | Out-Null
Start-ScheduledTask -TaskName $TaskName

Write-Host "Installed scheduled task '$TaskName' to run every $IntervalMinutes minute(s)."
