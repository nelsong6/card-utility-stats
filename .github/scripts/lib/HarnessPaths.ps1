Set-StrictMode -Version 3.0

# Pure, dot-sourceable resolution of the verification HARNESS file layout.
#
# The glimmung verify loop runs two distinct trees on the laptop:
#   - the HARNESS (the grader): the .github/scripts/* PowerShell + lib/ and the
#     repo-root .mcp.json. These MUST come from the workflow's `git_ref`
#     (default main), staged per-run, so the grader is fixed regardless of what
#     the agent's feature branch says.
#   - the CODE UNDER TEST: Core/, Tests/, Loader/, etc. — the feature-branch
#     checkout the agent built/tested. That is RepoRoot, and it is deliberately
#     NOT where harness scripts or .mcp.json are read from.
#
# Historically native-runtime.ps1 derived run-phases.ps1 / prepare-scenario.ps1
# and read .mcp.json from -RepoRoot, so the grader silently ran from the feature
# branch. This function is the single source of truth for "given a staged
# harness root, where do the harness artifacts live", so the wiring is explicit
# and testable. It is intentionally pure — no script-scope state, no env, no
# filesystem probing — so its Pester suite dot-sources THIS file and exercises
# the real production code rather than a mirror re-implementation.
#
# The HarnessRoot mirrors the repo layout: <HarnessRoot>\.mcp.json and
# <HarnessRoot>\.github\scripts\*.ps1 (+ lib\). That mirror is what the
# cluster-side native_stage_harness scp's into place from the pod's git_ref
# checkout.

function Resolve-HarnessPaths {
    <#
    .SYNOPSIS
        Resolve the harness artifact paths (scripts + .mcp.json) under a staged
        harness root.

    .DESCRIPTION
        Given the staged HarnessRoot (the per-run copy of the git_ref harness),
        returns the absolute paths to the harness-owned artifacts: the runtime's
        sibling phase scripts (run-phases.ps1, prepare-scenario.ps1) and the
        repo-root .mcp.json template. Callers MUST source these from the returned
        (harness-rooted) paths and never from the code-under-test RepoRoot.

    .PARAMETER HarnessRoot
        Root of the staged harness, laid out like the repo: it contains
        `.mcp.json` and `.github\scripts\...`.

    .OUTPUTS
        [pscustomobject] with HarnessRoot, McpConfigPath, ScriptsDir,
        RunPhasesPath, PrepareScenarioPath.
    #>
    param(
        [Parameter(Mandatory = $true)][string]$HarnessRoot
    )

    if ([string]::IsNullOrWhiteSpace($HarnessRoot)) {
        throw 'HarnessRoot is required to resolve harness paths.'
    }

    # The harness is ALWAYS a Windows laptop path (e.g. C:\glimmung-runs\...),
    # so join with literal backslashes rather than Join-Path. Join-Path would
    # emit '/'-separated paths on a non-Windows host (wrong for the Windows
    # target) and, worse, throws DriveNotFoundException on Linux when the root
    # carries a 'C:' drive qualifier — which would break the cross-platform CI
    # that runs this code's tests. Explicit '\' joins are host-OS-independent
    # and produce the exact path the laptop consumes.
    $root = $HarnessRoot.TrimEnd('\', '/')
    $scriptsDir = "$root\.github\scripts"
    return [pscustomobject]@{
        HarnessRoot         = $HarnessRoot
        McpConfigPath       = "$root\.mcp.json"
        ScriptsDir          = $scriptsDir
        RunPhasesPath       = "$scriptsDir\run-phases.ps1"
        PrepareScenarioPath = "$scriptsDir\prepare-scenario.ps1"
    }
}
