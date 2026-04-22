[CmdletBinding()]
param(
    [string]$RepoRoot = "D:\repos\card-utility-stats",
    [string]$TaskName = "Codex Issue Queue Worker",
    [string]$WorkerName = "sts2-side-a",
    [int]$IntervalMinutes = 5
)

$ErrorActionPreference = "Stop"

$scriptPath = Join-Path $RepoRoot "ops\codex-queue\Run-IssueQueueWorker.ps1"
if (-not (Test-Path -LiteralPath $scriptPath)) {
    throw "Worker script not found at $scriptPath"
}

$action = New-ScheduledTaskAction -Execute "powershell.exe" -Argument "-NoLogo -NoProfile -ExecutionPolicy Bypass -File `"$scriptPath`" -RepoRoot `"$RepoRoot`" -WorkerName `"$WorkerName`""
$trigger = New-ScheduledTaskTrigger -Once -At ((Get-Date).AddMinutes(1)) -RepetitionInterval (New-TimeSpan -Minutes $IntervalMinutes)
$settings = New-ScheduledTaskSettingsSet -StartWhenAvailable

$existingTask = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
if ($existingTask) {
    Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false
}

Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger -Settings $settings | Out-Null
Start-ScheduledTask -TaskName $TaskName

Write-Host "Installed scheduled task '$TaskName' to run every $IntervalMinutes minute(s)."
