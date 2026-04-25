# Laptop Issue-Agent Runner

This is the active issue-agent host path.

The repo no longer treats Azure VMSS or a builder VM as the primary deployment
target for live `issue-agent` work. The expected host is now a local Windows
machine, typically the work laptop.

## Goal

Bring one Windows machine online as a self-hosted GitHub Actions runner with:

- runner label `issue-agent`
- Claude Code installed locally
- Slay the Spire 2 installed locally
- STS2 Modding MCP installed locally
- Azure CLI logged in locally as an account that can read the Key Vault secret

## Host Requirements

Install these once on the laptop:

- GitHub Actions runner files
- Git
- GitHub CLI
- Azure CLI
- .NET 9 SDK
- Python 3.12
- Steam
- Slay the Spire 2
- Claude Code
- STS2 Modding MCP checkout and its local dependencies

Common Claude CLI locations supported by the workflow:

- `D:\automation\claude-code\node_modules\@anthropic-ai\claude-code\bin\claude.exe`
- `C:\automation\claude-code\node_modules\@anthropic-ai\claude-code\bin\claude.exe`
- `%USERPROFILE%\automation\claude-code\node_modules\@anthropic-ai\claude-code\bin\claude.exe`

If you want a different location, set repository variable
`ISSUE_AGENT_CLAUDE_CLI_PATH`.

## Anthropic Key Loading

The issue-agent workflow loads `ANTHROPIC_API_KEY` by running Azure CLI on the
self-hosted runner:

```powershell
az keyvault secret show --vault-name $env:KEY_VAULT_NAME --name spirelens --query value --output tsv
```

Set up Azure CLI once in the same Windows user/session that starts the
interactive runner:

```powershell
az login
az account show
az keyvault secret show --vault-name romaine-kv --name spirelens --query value --output tsv
```

The final command should print the secret value. Do not paste that value into
logs, issues, or pull requests.

The workflow reads the vault name from repository variable `KEY_VAULT_NAME` and
expects the secret to be named `spirelens`.

## Register The Runner

1. Install the GitHub Actions runner files somewhere stable such as
   `D:\actions-runner-spirelens`, `C:\actions-runner-spirelens`,
   `D:\actions-runner`, `C:\actions-runner`, or `%USERPROFILE%\actions-runner`.
2. If `GITHUB_PAT` is not already set, make it available locally before running
   the helper script.
3. Run the local registration helper from an elevated PowerShell session if you
   want the runner installed or repaired as a Windows service:

```powershell
pwsh -NoProfile -File .\ops\windows-worker\Register-LocalIssueAgentRunner.ps1 `
  -RepositorySlug nelsong6/spirelens `
  -RunnerLabels issue-agent
```

The script will:

- reuse `GITHUB_PAT` if already set, or
- read `github-pat` from Key Vault if `-KeyVaultName` is supplied, then
- register the machine as a repository-scoped runner, and
- run it as a Windows service by default

If the runner files are not under one of those default paths, pass
`-RunnerRoot`.

For live STS2 validation, prefer running the runner interactively from the
logged-in Steam user session instead of as a Windows service:

```powershell
cd C:\actions-runner-card-utility-stats
.\run.cmd
```

A Windows service launches STS2 in session 0, which does not provide the same
Steam client/session context as the logged-in user desktop.

## Workflow Expectations

The issue-agent workflow expects:

- the runner has labels `self-hosted`, `windows`, and `issue-agent`
- the repo checkout contains a working `.mcp.json`
- Claude can list and connect to `spire-lens-mcp`
- STS2 is available locally when the issue requires live validation
- Azure CLI is logged in for the runner user and can read Key Vault secret `spirelens`

The workflow itself still handles:

- mapping the Key Vault secret to `ANTHROPIC_API_KEY`
- uploading logs, screenshots, and validation artifacts

## Sanity Check

Once the runner is online in GitHub:

1. Confirm the machine appears under repository runners with label
   `issue-agent`.
2. Run the `Issue Agent` workflow manually for a low-risk issue number.
3. Confirm the run:
   - starts on the laptop
   - passes the STS2 bridge readiness check
   - launches Claude with MCP available
   - uploads the expected artifacts

## Secondary Machines

If you want a second machine later, use the same script and keep the same
`issue-agent` label unless you deliberately want to split hosts by capability.
