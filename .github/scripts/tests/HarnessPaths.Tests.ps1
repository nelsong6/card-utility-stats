#Requires -Modules @{ ModuleName = 'Pester'; ModuleVersion = '5.0.0' }

# Proves git_ref controls the verification harness: native-runtime.ps1 must
# resolve its phase scripts (run-phases.ps1 / prepare-scenario.ps1) and read
# .mcp.json from -HarnessRoot (the git_ref-staged harness), NEVER from -RepoRoot
# (the feature-branch code under test).
#
# Two layers, mirroring the rest of this suite:
#   1. Dot-source the REAL pure resolver (lib/HarnessPaths.ps1) and assert it
#      derives every harness path from HarnessRoot — exactly the
#      UnitTestResult.Tests.ps1 pattern (test production code, not a mirror).
#   2. AST/source-scan native-runtime.ps1 to assert it actually wires those
#      resolutions to -HarnessRoot and no longer reads scripts/.mcp.json from
#      -RepoRoot — the NoProseUnitTestGate.Tests.ps1 pattern (structural guard
#      so the regression cannot silently come back).

BeforeAll {
    Set-StrictMode -Version 3.0
    $ErrorActionPreference = 'Stop'

    . (Join-Path $PSScriptRoot '..' 'lib' 'HarnessPaths.ps1')

    $script:RuntimePath = Join-Path $PSScriptRoot '..' 'native-runtime.ps1'
    if (-not (Test-Path -LiteralPath $script:RuntimePath)) {
        throw "native-runtime.ps1 not found at $script:RuntimePath"
    }
    $script:RuntimeSource = Get-Content -LiteralPath $script:RuntimePath -Raw

    $errors = $null
    $tokens = $null
    $script:RuntimeAst = [System.Management.Automation.Language.Parser]::ParseInput(
        $script:RuntimeSource, [ref]$tokens, [ref]$errors)
    if ($errors.Count -gt 0) {
        throw "native-runtime.ps1 failed to parse:`n$(($errors | ForEach-Object { '  L' + $_.Extent.StartLineNumber + ': ' + $_.Message }) -join "`n")"
    }

    function Get-RuntimeFunction {
        param([string]$Name)
        $script:RuntimeAst.FindAll(
            { param($n) $n -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq $Name },
            $true
        ) | Select-Object -First 1
    }
}

Describe 'Resolve-HarnessPaths (the real pure resolver)' {
    Context 'every harness artifact is rooted at HarnessRoot' {
        It 'derives .mcp.json and the phase scripts from HarnessRoot, mirroring the repo layout' {
            # Literal Windows-path expectations (NOT Join-Path, which is host-OS
            # dependent and DriveNotFound-throws on the Linux CI runner). The
            # harness target is always a Windows laptop path.
            $harnessRoot = 'C:\glimmung-runs\spirelens-run\harness'
            $r = Resolve-HarnessPaths -HarnessRoot $harnessRoot

            $r.HarnessRoot         | Should -Be $harnessRoot
            $r.McpConfigPath       | Should -Be 'C:\glimmung-runs\spirelens-run\harness\.mcp.json'
            $r.ScriptsDir          | Should -Be 'C:\glimmung-runs\spirelens-run\harness\.github\scripts'
            $r.RunPhasesPath       | Should -Be 'C:\glimmung-runs\spirelens-run\harness\.github\scripts\run-phases.ps1'
            $r.PrepareScenarioPath | Should -Be 'C:\glimmung-runs\spirelens-run\harness\.github\scripts\prepare-scenario.ps1'
        }

        It 'normalizes a trailing separator on HarnessRoot' {
            $r = Resolve-HarnessPaths -HarnessRoot 'C:\glimmung-runs\run\harness\'
            $r.McpConfigPath | Should -Be 'C:\glimmung-runs\run\harness\.mcp.json'
            $r.RunPhasesPath | Should -Be 'C:\glimmung-runs\run\harness\.github\scripts\run-phases.ps1'
        }

        It 'never returns a path under an unrelated RepoRoot (the code under test)' {
            # The feature-branch checkout. Not one resolved harness path may live
            # under it — that was the bug (grader ran from the feature branch).
            $repoRoot    = 'D:\repos\SpireLens'
            $harnessRoot = 'C:\glimmung-runs\spirelens-run\harness'
            $r = Resolve-HarnessPaths -HarnessRoot $harnessRoot

            foreach ($p in @($r.McpConfigPath, $r.ScriptsDir, $r.RunPhasesPath, $r.PrepareScenarioPath)) {
                $p | Should -Not -BeLike "$repoRoot*"
                $p | Should -BeLike "$harnessRoot*"
            }
        }

        It 'tracks HarnessRoot when it changes (no hard-coded path leaks in)' {
            $a = Resolve-HarnessPaths -HarnessRoot 'C:\a\harness'
            $b = Resolve-HarnessPaths -HarnessRoot 'C:\b\harness'
            $a.RunPhasesPath | Should -BeLike 'C:\a\harness*'
            $b.RunPhasesPath | Should -BeLike 'C:\b\harness*'
            $a.McpConfigPath | Should -Not -Be $b.McpConfigPath
        }
    }

    Context 'input validation' {
        It 'throws on a blank HarnessRoot rather than resolving a bare-relative path' {
            { Resolve-HarnessPaths -HarnessRoot '' }   | Should -Throw
            { Resolve-HarnessPaths -HarnessRoot '   ' } | Should -Throw
        }
    }
}

Describe 'native-runtime.ps1 wires harness resolution to -HarnessRoot, not -RepoRoot' {
    # Exact source substrings (single-quoted: no PowerShell interpolation, so the
    # literal $ in $RepoRoot etc. is matched verbatim via .Contains()).
    BeforeAll {
        $script:RepoRootRunPhasesJoin       = '$RepoRoot ''.github\scripts\run-phases.ps1'''
        $script:RepoRootPrepareScenarioJoin = '$RepoRoot ''.github\scripts\prepare-scenario.ps1'''
        $script:RepoRootMcpJoin             = 'Join-Path $RepoRoot ''.mcp.json'''
        $script:HarnessRootMcpJoin          = 'Join-Path $HarnessRoot ''.mcp.json'''
    }

    It 'declares a mandatory -HarnessRoot parameter distinct from -RepoRoot' {
        $names = $script:RuntimeAst.ParamBlock.Parameters |
            ForEach-Object { $_.Name.VariablePath.UserPath }
        $names | Should -Contain 'HarnessRoot'
        $names | Should -Contain 'RepoRoot'
    }

    It 'dot-sources the pure HarnessPaths resolver relative to its own script dir' {
        # Sibling lib is harness-owned, so it loads via $PSScriptRoot — never via
        # RepoRoot or HarnessRoot.
        $script:RuntimeSource.Contains("Join-Path `$PSScriptRoot 'lib' 'HarnessPaths.ps1'") |
            Should -BeTrue -Because 'the resolver is a harness sibling, loaded from the script dir'
    }

    It 'resolves the phase scripts from the harness, with the old RepoRoot joins gone' {
        # The historical bug joined run-phases.ps1 / prepare-scenario.ps1 onto
        # $RepoRoot (the feature branch). Those joins must be gone...
        $script:RuntimeSource.Contains($script:RepoRootRunPhasesJoin) |
            Should -BeFalse -Because 'run-phases.ps1 must not be sourced from the code-under-test RepoRoot'
        $script:RuntimeSource.Contains($script:RepoRootPrepareScenarioJoin) |
            Should -BeFalse -Because 'prepare-scenario.ps1 must not be sourced from the code-under-test RepoRoot'
        # ...and the script paths now come from the resolver result.
        $script:RuntimeSource.Contains('$harness.RunPhasesPath')       | Should -BeTrue
        $script:RuntimeSource.Contains('$harness.PrepareScenarioPath') | Should -BeTrue
    }

    It 'reads .mcp.json from HarnessRoot in Resolve-Sts2GameDir (never from RepoRoot)' {
        $fn = Get-RuntimeFunction -Name 'Resolve-Sts2GameDir'
        $fn | Should -Not -BeNullOrEmpty
        $text = $fn.Extent.Text
        $text.Contains('HarnessRoot')             | Should -BeTrue
        $text.Contains('$RepoRoot')               | Should -BeFalse -Because 'the code-under-test root has no business here'
        $text.Contains($script:RepoRootMcpJoin)   | Should -BeFalse
    }

    It 'reads the .mcp.json template from HarnessRoot in New-NativeMcpConfig (never from RepoRoot)' {
        $fn = Get-RuntimeFunction -Name 'New-NativeMcpConfig'
        $fn | Should -Not -BeNullOrEmpty
        $text = $fn.Extent.Text
        $text.Contains('HarnessRoot')             | Should -BeTrue
        $text.Contains($script:RepoRootMcpJoin)   | Should -BeFalse
        $text.Contains($script:HarnessRootMcpJoin) | Should -BeFalse -Because 'the .mcp.json path comes via Resolve-HarnessPaths, not an inline join'
    }

    It 'forwards BOTH -HarnessRoot (harness) and -RepoRoot (code under test) to the phase scripts' {
        # RepoRoot is NOT abolished — it must still flow onward as the
        # code-under-test root (Tests build, agent working dir), alongside the
        # harness root.
        $script:RuntimeSource.Contains("'-RepoRoot', `$RepoRoot")       | Should -BeTrue
        $script:RuntimeSource.Contains("'-HarnessRoot', `$HarnessRoot") | Should -BeTrue
    }
}
