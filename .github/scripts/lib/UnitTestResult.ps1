Set-StrictMode -Version 3.0

# Pure, dot-sourceable parsing of an observed `dotnet test` result.
#
# This file is the structural replacement for the retired prose-scan gate
# (`Test-TextMentionsFailedTests`), which decided pass/fail by regex-scanning
# the agent's English narration. That approach threw away the ground truth
# `dotnet test` already produces — a deterministic exit code plus a structured
# TRX with per-test outcomes — and was both wrong (it false-aborted a fully
# green run whose note said "99 passed, 0 failed") and flaky (the verdict
# depended on the agent's wording).
#
# `Get-ObservedUnitTestResult` reads the observed outcome instead: the process
# exit code and the TRX `<ResultSummary>` counters / failing `<UnitTestResult>`
# names. It is intentionally pure — no script-scope closures, no dependency on
# $IssueNumber / $ValidationArtifactDir / any caller state — so the Pester
# suite can dot-source THIS file and exercise the real production code rather
# than a mirror re-implementation (the smell that let the original bug ship).

function Get-ObservedUnitTestResult {
    <#
    .SYNOPSIS
        Resolve the observed result of a `dotnet test` invocation from its exit
        code and TRX file.

    .DESCRIPTION
        Ground truth is the observed outcome, not any claimed intent:
          - `passed` is ($ExitCode -eq 0 -and $failed -eq 0). Both must hold; a
            zero exit with a parsed failure (or a nonzero exit with a clean TRX)
            is treated as failed/declared via the exit code respectively.
          - `total` / `failed` / `failed_names` come from the TRX when it is
            present and parseable: `<ResultSummary><Counters .../></ResultSummary>`
            for counts and `<UnitTestResult outcome="Failed" testName="..."/>`
            for the failing test names.

        Defensive contract for a missing or unparseable TRX:
          - nonzero exit  -> passed=$false with a synthetic note (the run failed
            and we have no structured detail to refine it).
          - zero exit     -> passed=$true with a synthetic note (the runner
            reported success even though the TRX is absent/unreadable).

    .PARAMETER ExitCode
        The `$LASTEXITCODE` captured immediately after `dotnet test`.

    .PARAMETER TrxPath
        Path to the TRX log written via `--logger "trx;LogFileName=..."`.

    .OUTPUTS
        [pscustomobject] with: passed [bool], total [int], failed [int],
        failed_names [string[]], notes [string].
    #>
    param(
        [Parameter(Mandatory = $true)][int]$ExitCode,
        [AllowNull()][AllowEmptyString()][string]$TrxPath
    )

    $total = 0
    $failed = 0
    $failedNames = [System.Collections.Generic.List[string]]::new()
    $trxParsed = $false
    $parseNote = $null

    if (-not [string]::IsNullOrWhiteSpace($TrxPath) -and (Test-Path -LiteralPath $TrxPath)) {
        try {
            [xml]$trx = Get-Content -LiteralPath $TrxPath -Raw

            # Namespace-agnostic lookups: the VSTest TRX schema lives in the
            # http://microsoft.com/schemas/VisualStudio/TeamTest/2010 namespace,
            # so match on LocalName rather than binding a namespace manager.
            $counters = $trx.SelectNodes('//*[local-name()="ResultSummary"]/*[local-name()="Counters"]')
            if ($null -ne $counters -and $counters.Count -gt 0) {
                $counter = $counters[0]
                $totalAttr = $counter.Attributes['total']
                $failedAttr = $counter.Attributes['failed']
                if ($null -ne $totalAttr) { [void][int]::TryParse($totalAttr.Value, [ref]$total) }
                if ($null -ne $failedAttr) { [void][int]::TryParse($failedAttr.Value, [ref]$failed) }
            }

            $unitResults = $trx.SelectNodes('//*[local-name()="UnitTestResult"]')
            if ($null -ne $unitResults) {
                foreach ($node in $unitResults) {
                    $outcomeAttr = $node.Attributes['outcome']
                    if ($null -ne $outcomeAttr -and $outcomeAttr.Value -eq 'Failed') {
                        $nameAttr = $node.Attributes['testName']
                        $name = if ($null -ne $nameAttr) { [string]$nameAttr.Value } else { '' }
                        if ([string]::IsNullOrWhiteSpace($name)) { $name = '(unnamed test)' }
                        $failedNames.Add($name)
                    }
                }
            }

            # Prefer the enumerated failing-name count when the summary counter
            # is absent or lower (e.g. an aborted runner that wrote results but
            # not a complete summary): observed failing rows beat a stale count.
            if ($failedNames.Count -gt $failed) { $failed = $failedNames.Count }

            $trxParsed = $true
        } catch {
            $parseNote = "Unit-test TRX at '$TrxPath' could not be parsed: $($_.Exception.Message)."
        }
    } else {
        $parseNote = "Unit-test TRX was not found at '$TrxPath'."
    }

    if (-not $trxParsed) {
        if ($ExitCode -eq 0) {
            return [pscustomobject]@{
                passed = $true
                total = 0
                failed = 0
                failed_names = @()
                notes = "dotnet test reported success (exit 0) but no structured TRX result was available. $parseNote".Trim()
            }
        }
        return [pscustomobject]@{
            passed = $false
            total = 0
            failed = 0
            failed_names = @()
            notes = "dotnet test failed (exit $ExitCode) and no structured TRX result was available to enumerate failing tests. $parseNote".Trim()
        }
    }

    $passed = ($ExitCode -eq 0 -and $failed -eq 0)
    $note = if ($passed) {
        "$total unit test(s) passed (exit $ExitCode, 0 failed)."
    } elseif ($failedNames.Count -gt 0) {
        "$failed of $total unit test(s) failed (exit $ExitCode). Failing: $($failedNames -join '; ')."
    } else {
        "dotnet test reported failure (exit $ExitCode) for $total unit test(s); the TRX enumerated $failed failed but no failing test names."
    }

    return [pscustomobject]@{
        passed = $passed
        total = $total
        failed = $failed
        failed_names = @($failedNames)
        notes = $note
    }
}
