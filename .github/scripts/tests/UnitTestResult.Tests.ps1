#Requires -Modules @{ ModuleName = 'Pester'; ModuleVersion = '5.0.0' }

# Tests for Get-ObservedUnitTestResult. Unlike EvidenceGuard.Tests.ps1 (which
# mirrors logic the guard could not dot-source), these exercise the REAL
# production function by dot-sourcing lib/UnitTestResult.ps1 directly — the
# function is intentionally pure (no script-scope closures, no $IssueNumber /
# $ValidationArtifactDir dependency) precisely so the contract is tested through
# production code, not a re-implementation that can drift.

BeforeAll {
    Set-StrictMode -Version 3.0
    $ErrorActionPreference = 'Stop'

    . (Join-Path $PSScriptRoot '..' 'lib' 'UnitTestResult.ps1')

    # Build a minimal-but-realistic VSTest TRX. The real runner emits the
    # 2010 TeamTest namespace; the parser is namespace-agnostic (local-name()),
    # so we include the namespace to prove that.
    function New-TrxFixture {
        param(
            [int]$Total,
            [int]$Failed,
            [string[]]$FailedNames = @(),
            [string[]]$PassedNames = @()
        )
        $sb = [System.Text.StringBuilder]::new()
        [void]$sb.AppendLine('<?xml version="1.0" encoding="UTF-8"?>')
        [void]$sb.AppendLine('<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">')
        [void]$sb.AppendLine('  <Results>')
        foreach ($n in $PassedNames) {
            [void]$sb.AppendLine("    <UnitTestResult testName=`"$n`" outcome=`"Passed`" />")
        }
        foreach ($n in $FailedNames) {
            [void]$sb.AppendLine("    <UnitTestResult testName=`"$n`" outcome=`"Failed`" />")
        }
        [void]$sb.AppendLine('  </Results>')
        [void]$sb.AppendLine('  <ResultSummary outcome="Completed">')
        $passed = $Total - $Failed
        [void]$sb.AppendLine("    <Counters total=`"$Total`" executed=`"$Total`" passed=`"$passed`" failed=`"$Failed`" />")
        [void]$sb.AppendLine('  </ResultSummary>')
        [void]$sb.AppendLine('</TestRun>')

        $path = Join-Path $TestDrive ("trx-" + [guid]::NewGuid().ToString('n') + '.trx')
        [System.IO.File]::WriteAllText($path, $sb.ToString())
        return $path
    }
}

Describe 'Get-ObservedUnitTestResult' {
    Context 'all tests pass (exit 0, 0 failed)' {
        It 'reports passed=true with the total count and no failing names' {
            $trx = New-TrxFixture -Total 99 -Failed 0 -PassedNames @('A', 'B', 'C')
            $r = Get-ObservedUnitTestResult -ExitCode 0 -TrxPath $trx
            $r.passed       | Should -BeTrue
            $r.total        | Should -Be 99
            $r.failed       | Should -Be 0
            @($r.failed_names).Count | Should -Be 0
        }
    }

    Context 'N failures (exit 1, failing names present)' {
        It 'reports passed=false and enumerates the failing test names' {
            $trx = New-TrxFixture -Total 50 -Failed 2 `
                -FailedNames @('SchemaLoadingTests.LoadsPooledShape', 'PoisonTooltipTests.ShowsDownstreamDamage') `
                -PassedNames @('X', 'Y')
            $r = Get-ObservedUnitTestResult -ExitCode 1 -TrxPath $trx
            $r.passed | Should -BeFalse
            $r.total  | Should -Be 50
            $r.failed | Should -Be 2
            @($r.failed_names) | Should -Contain 'SchemaLoadingTests.LoadsPooledShape'
            @($r.failed_names) | Should -Contain 'PoisonTooltipTests.ShowsDownstreamDamage'
            # The failing names belong in the human-readable note too.
            $r.notes | Should -Match 'SchemaLoadingTests.LoadsPooledShape'
        }
    }

    Context 'zero tests / empty TRX' {
        It 'treats a zero-test run with exit 0 as passed' {
            $trx = New-TrxFixture -Total 0 -Failed 0
            $r = Get-ObservedUnitTestResult -ExitCode 0 -TrxPath $trx
            $r.passed | Should -BeTrue
            $r.total  | Should -Be 0
            $r.failed | Should -Be 0
        }
    }

    Context 'the exact historical trap, framed as a result' {
        It '"99 passed, 0 failed" maps to PASSED because failed -eq 0 (verdict no longer depends on prose)' {
            # The retired prose scan false-aborted on the note "99 passed, 0
            # failed" because its regex let the pass-count "99" sit within four
            # words of "failed". The observed result is unambiguous: 99 total,
            # 0 failed, exit 0 -> passed. Prove the structured verdict ignores
            # any such wording entirely.
            $trx = New-TrxFixture -Total 99 -Failed 0
            $r = Get-ObservedUnitTestResult -ExitCode 0 -TrxPath $trx
            $r.passed | Should -BeTrue
            $r.total  | Should -Be 99
            $r.failed | Should -Be 0
        }
    }

    Context 'exit code is part of the verdict, not just the TRX counts' {
        It 'a clean TRX with a nonzero exit is still passed=false' {
            # passed requires BOTH exit 0 AND failed 0. A runner that exits
            # nonzero (e.g. a crash after the summary was flushed) must not be
            # laundered into a pass by a 0-failed counter.
            $trx = New-TrxFixture -Total 10 -Failed 0
            $r = Get-ObservedUnitTestResult -ExitCode 1 -TrxPath $trx
            $r.passed | Should -BeFalse
        }
        It 'enumerated failing rows outvote a stale 0 counter' {
            # Defensive: a partial/aborted runner can write failing result rows
            # but a summary that still reads failed="0". The observed failing
            # rows win.
            $trx = New-TrxFixture -Total 5 -Failed 0 -FailedNames @('Flaky.Test')
            $r = Get-ObservedUnitTestResult -ExitCode 1 -TrxPath $trx
            $r.passed | Should -BeFalse
            $r.failed | Should -Be 1
            @($r.failed_names) | Should -Contain 'Flaky.Test'
        }
    }

    Context 'defensive: missing / unparseable TRX' {
        It 'missing TRX with a nonzero exit -> passed=false with a synthetic note' {
            $missing = Join-Path $TestDrive 'does-not-exist.trx'
            $r = Get-ObservedUnitTestResult -ExitCode 1 -TrxPath $missing
            $r.passed | Should -BeFalse
            $r.notes  | Should -Match 'no structured TRX'
        }
        It 'missing TRX with a zero exit -> passed=true with a synthetic note' {
            $missing = Join-Path $TestDrive 'does-not-exist.trx'
            $r = Get-ObservedUnitTestResult -ExitCode 0 -TrxPath $missing
            $r.passed | Should -BeTrue
            $r.notes  | Should -Match 'no structured TRX'
        }
        It 'null/empty TRX path is handled like a missing file' {
            $r = Get-ObservedUnitTestResult -ExitCode 0 -TrxPath ''
            $r.passed | Should -BeTrue
        }
        It 'unparseable (non-XML) TRX with a nonzero exit -> passed=false' {
            $junk = Join-Path $TestDrive 'garbage.trx'
            [System.IO.File]::WriteAllText($junk, 'this is not xml <<<')
            $r = Get-ObservedUnitTestResult -ExitCode 1 -TrxPath $junk
            $r.passed | Should -BeFalse
            $r.notes  | Should -Match 'could not be parsed|no structured TRX'
        }
    }

    Context 'output shape' {
        It 'always returns the documented fields' {
            $trx = New-TrxFixture -Total 1 -Failed 0
            $r = Get-ObservedUnitTestResult -ExitCode 0 -TrxPath $trx
            $r.PSObject.Properties.Name | Should -Contain 'passed'
            $r.PSObject.Properties.Name | Should -Contain 'total'
            $r.PSObject.Properties.Name | Should -Contain 'failed'
            $r.PSObject.Properties.Name | Should -Contain 'failed_names'
            ($r.passed -is [bool]) | Should -BeTrue
            ($r.total  -is [int])  | Should -BeTrue
            ($r.failed -is [int])  | Should -BeTrue
        }
    }
}
