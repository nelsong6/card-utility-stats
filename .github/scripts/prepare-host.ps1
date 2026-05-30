param(
    [Parameter(Mandatory = $true)][string]$CheckoutPath,
    [switch]$InstallMcp,
    [switch]$StartSts2
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 3.0  # V2 already catches missing PSCustomObject/hashtable properties; V3 adds out-of-bounds array indexing (verified empirically on PS 7.4)

# Shared STS2 game-dir + MCP-config resolution (also used by the phase wrapper
# and verify.sh's prepare_scenario step). Keep discovery identical everywhere.
. (Join-Path $PSScriptRoot 'Sts2HostPaths.ps1')

function Invoke-LoggedStep {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][scriptblock]$Body
    )
    # Wrap an unbounded sub-op with a BEGIN/END timestamped log line so a hang
    # always names itself in the GH Actions log instead of stalling silently.
    $start = Get-Date
    Write-Host ("::group::{0}" -f $Name)
    Write-Host ("[{0}] BEGIN: {1}" -f $start.ToString('o'), $Name)
    try {
        & $Body
        $secs = ((Get-Date) - $start).TotalSeconds
        Write-Host ("[{0}] END:   {1} ({2:N1}s)" -f (Get-Date).ToString('o'), $Name, $secs)
    } finally {
        Write-Host '::endgroup::'
    }
}

$repoRoot = Join-Path $env:GLIMMUNG_REPO_ROOT $CheckoutPath
if (-not (Test-Path -LiteralPath $repoRoot)) {
    throw "Issue-agent checkout was not found at '$repoRoot'."
}

$candidates = @()
if (-not [string]::IsNullOrWhiteSpace($env:CONFIGURED_CLAUDE_CLI_PATH)) {
    $candidates += $env:CONFIGURED_CLAUDE_CLI_PATH
}

$candidates += @(
    'D:\automation\claude-code\node_modules\@anthropic-ai\claude-code\bin\claude.exe',
    'C:\automation\claude-code\node_modules\@anthropic-ai\claude-code\bin\claude.exe',
    (Join-Path $env:USERPROFILE 'automation\claude-code\node_modules\@anthropic-ai\claude-code\bin\claude.exe'),
    (Join-Path $env:APPDATA 'npm\node_modules\@anthropic-ai\claude-code\bin\claude.exe')
)

$claudePath = $candidates |
    Where-Object { -not [string]::IsNullOrWhiteSpace($_) -and (Test-Path -LiteralPath $_) } |
    Select-Object -First 1

if (-not $claudePath) {
    throw "Claude Code CLI was not found. Set ISSUE_AGENT_CLAUDE_CLI_PATH or install Claude under a documented default location."
}

$buildRoot = Join-Path $env:GLIMMUNG_WORKING_DIR ("issue-agent-build\$($env:GLIMMUNG_RUN_ID)-$($env:GLIMMUNG_ATTEMPT_INDEX)-$([IO.Path]::GetFileName($CheckoutPath))")
New-Item -ItemType Directory -Force -Path $buildRoot | Out-Null

"CLAUDE_CLI_PATH=$claudePath" | Out-File -FilePath $env:GITHUB_ENV -Encoding utf8NoBOM -Append
"ISSUE_AGENT_REPO_ROOT=$repoRoot" | Out-File -FilePath $env:GITHUB_ENV -Encoding utf8NoBOM -Append
"ISSUE_AGENT_BUILD_ROOT=$buildRoot" | Out-File -FilePath $env:GITHUB_ENV -Encoding utf8NoBOM -Append

$gameDir = Resolve-Sts2GameDir -RepoRoot $repoRoot
$sts2DataDir = Join-Path $gameDir 'data_sts2_windows_x86_64'
$mcpConfigRoot = Join-Path $env:GLIMMUNG_WORKING_DIR 'issue-agent-mcp'
$safeCheckoutName = ([IO.Path]::GetFileName($CheckoutPath) -replace '[^A-Za-z0-9._-]', '-')
$jobMcpConfigPath = Join-Path $mcpConfigRoot "$($env:GLIMMUNG_RUN_ID)-$($env:GLIMMUNG_ATTEMPT_INDEX)-$safeCheckoutName.mcp.json"
$jobMcpConfigPath = Write-ResolvedMcpConfig -RepoRoot $repoRoot -GameDir $gameDir -OutPath $jobMcpConfigPath

"ISSUE_AGENT_STS2_GAME_DIR=$gameDir" | Out-File -FilePath $env:GITHUB_ENV -Encoding utf8NoBOM -Append
"ISSUE_AGENT_STS2_DATA_DIR=$sts2DataDir" | Out-File -FilePath $env:GITHUB_ENV -Encoding utf8NoBOM -Append
"ISSUE_AGENT_MCP_CONFIG_PATH=$jobMcpConfigPath" | Out-File -FilePath $env:GITHUB_ENV -Encoding utf8NoBOM -Append

if (-not $InstallMcp) { return }

$mcpRoot = 'D:\repos\spire-lens-mcp'
$mcpRepo = 'https://github.com/nelsong6/spire-lens-mcp.git'

if (Test-Path -LiteralPath (Join-Path $mcpRoot '.git')) {
    Invoke-LoggedStep -Name 'git fetch spire-lens-mcp' -Body {
        git -C $mcpRoot fetch --prune origin main
    }
    Invoke-LoggedStep -Name 'git checkout main (spire-lens-mcp)' -Body {
        git -C $mcpRoot checkout main
    }
    Invoke-LoggedStep -Name 'git pull spire-lens-mcp' -Body {
        git -C $mcpRoot pull --ff-only origin main
    }
} else {
    $parent = Split-Path -Parent $mcpRoot
    New-Item -ItemType Directory -Force -Path $parent | Out-Null
    Invoke-LoggedStep -Name 'git clone spire-lens-mcp' -Body {
        git clone $mcpRepo $mcpRoot
    }
}

if ($LASTEXITCODE -ne 0) {
    throw "Unable to refresh spire-lens-mcp checkout at '$mcpRoot'."
}

Invoke-LoggedStep -Name 'uv run python py_compile server.py' -Body {
    uv run --directory (Join-Path $mcpRoot 'mcp') python -m py_compile server.py
}

$buildScript = Join-Path $mcpRoot 'build.ps1'
if (-not (Test-Path -LiteralPath $buildScript)) {
    throw "SpireLensMcpBridge build script was not found at '$buildScript'."
}

Invoke-LoggedStep -Name 'Build SpireLensMcpBridge DLL' -Body {
    & $buildScript -GameDir $gameDir -Configuration Release
    if ($LASTEXITCODE -ne 0) {
        throw 'SpireLensMcpBridge build failed.'
    }
}

$modsDir = Join-Path $gameDir 'mods'
New-Item -ItemType Directory -Force -Path $modsDir | Out-Null

# Clean up any artifacts left by older naming conventions before deploying the
# current files. The order matters: a runner that has both an old SpireLensMcp
# folder AND old flat SpireLensMcp.{dll,json} files needs both removed before
# the new deploy lands, or STS2 may load two copies of the bridge mod.
$staleMcpFolder = Join-Path $modsDir 'SpireLensMcp'
if (Test-Path -LiteralPath $staleMcpFolder) {
    Remove-Item -LiteralPath $staleMcpFolder -Recurse -Force -ErrorAction Stop
}
foreach ($staleFile in @('SpireLensMcp.dll', 'SpireLensMcp.json')) {
    $stalePath = Join-Path $modsDir $staleFile
    if (Test-Path -LiteralPath $stalePath) {
        Remove-Item -LiteralPath $stalePath -Force -ErrorAction Stop
    }
}

Invoke-LoggedStep -Name 'Stop existing STS2 processes' -Body {
    # Was: Get-CimInstance Win32_Process | Where-Object { ... } | Stop-Process.
    # Get-CimInstance wedges in the GH Actions runner context (likely Session 0
    # / service-context WMI/DCOM quirk; see spirelens#162). Get-Process calls
    # NtQuerySystemInformation directly — no WMI / DCOM / impersonation, runs
    # in <50ms regardless of shell. Filter to game-directory-resident processes
    # by MainModule path; skip entries whose MainModule we can't read.
    $prefix = $gameDir.TrimEnd('\') + '\'
    $procs = Get-Process -Name 'SlayTheSpire2','crashpad_handler' -ErrorAction SilentlyContinue |
        Where-Object {
            try {
                $path = $_.MainModule.FileName
                -not [string]::IsNullOrWhiteSpace($path) -and
                $path.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)
            } catch {
                $false
            }
        }
    foreach ($p in $procs) {
        Write-Host "Stopping $($p.ProcessName).exe pid=$($p.Id)"
        try {
            Stop-Process -Id $p.Id -Force -ErrorAction Stop
            $p.WaitForExit(5000) | Out-Null
        } catch {
            Write-Warning "Stop-Process failed for $($p.ProcessName).exe pid=$($p.Id): $($_.Exception.Message)"
        }
        if (-not $p.HasExited) {
            Write-Warning "Stop-Process didn't terminate $($p.ProcessName).exe pid=$($p.Id) within 5s; escalating to taskkill /F /T"
            & taskkill.exe /F /T /PID $p.Id 2>$null
        }
    }
    Start-Sleep -Seconds 2
}

Invoke-LoggedStep -Name 'Deploy SpireLensMcpBridge into mods/' -Body {
    # The mod loader pairs <id>.dll with <id>.json by basename; basename must
    # match mod_manifest.json's id field, currently SpireLensMcpBridge.
    Copy-Item -LiteralPath (Join-Path $mcpRoot 'out\SpireLensMcpBridge\SpireLensMcpBridge.dll') -Destination (Join-Path $modsDir 'SpireLensMcpBridge.dll') -Force
    Copy-Item -LiteralPath (Join-Path $mcpRoot 'mod_manifest.json') -Destination (Join-Path $modsDir 'SpireLensMcpBridge.json') -Force
}

if (-not $StartSts2) { return }

$restartScript = Join-Path $repoRoot '.github\scripts\restart-sts2.ps1'
if (-not (Test-Path -LiteralPath $restartScript)) {
    throw "STS2 restart script was not found at '$restartScript'."
}

Invoke-LoggedStep -Name 'Restart STS2 and wait for bridge' -Body {
    & $restartScript `
        -Mode Restart `
        -McpConfigPath $jobMcpConfigPath `
        -StartupTimeoutSeconds 60 `
        -ShutdownTimeoutSeconds 45
}
