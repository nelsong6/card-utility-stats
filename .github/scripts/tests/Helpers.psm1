# Test-helper module for the issue-agent Pester suite.
#
# Both production scripts (run-issue-agent-phases.ps1, summarize-issue-agent.ps1)
# define their helpers inline alongside a script-level main flow. Importing the
# scripts directly would fail because the main flow needs param values + an
# STS2 host. Instead we extract the function definitions out of the script
# source via the PowerShell AST and load them into a fresh module scope. This
# keeps the test file in lockstep with the latest source — there is no copied
# function body to drift from production.
function Import-ScriptFunctions {
    param(
        [Parameter(Mandatory = $true)][string]$ScriptPath,
        [string[]]$FunctionNames
    )

    if (-not (Test-Path -LiteralPath $ScriptPath)) {
        throw "Script not found: $ScriptPath"
    }
    $source = Get-Content -LiteralPath $ScriptPath -Raw
    $errors = $null
    $tokens = $null
    $ast = [System.Management.Automation.Language.Parser]::ParseInput($source, [ref]$tokens, [ref]$errors)
    if ($errors.Count -gt 0) {
        throw "Failed to parse $ScriptPath`:`n$(($errors | ForEach-Object { '  L' + $_.Extent.StartLineNumber + ': ' + $_.Message }) -join "`n")"
    }

    $functions = $ast.FindAll(
        { param($node) $node -is [System.Management.Automation.Language.FunctionDefinitionAst] },
        $true
    )

    $selected = if ($FunctionNames) {
        $functions | Where-Object { $FunctionNames -contains $_.Name }
    } else {
        $functions
    }

    $blocks = $selected | ForEach-Object { $_.Extent.Text }
    return ($blocks -join [Environment]::NewLine + [Environment]::NewLine)
}

Export-ModuleMember -Function Import-ScriptFunctions
