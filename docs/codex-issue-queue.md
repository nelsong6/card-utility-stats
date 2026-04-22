# Codex Issue Queue

This side machine is not just a one-off CI runner anymore. It is intended to become a persistent autonomous issue worker for `card-utility-stats`.

The key requirement is operational, not prompt-based:

- the machine can stay on by itself
- Codex can run headlessly on the machine
- issues are processed through an actual queue
- the queue is drained one issue at a time until empty
- progress does not depend on a human repeatedly telling Codex to continue

## Queue Contract

The queue is defined by GitHub issue labels.

Issues are eligible for autonomous work when they have:

- `codex-queue`

Queue state is tracked with:

- `codex-active`
- `codex-blocked`
- `codex-complete`

Recommended meaning:

- `codex-queue`
  - ready for the worker to pick up
- `codex-active`
  - currently claimed by the worker
- `codex-blocked`
  - removed from the queue pending human input or missing prerequisites
- `codex-complete`
  - processed by the queue worker and no longer queued

## Processing Model

The worker script does not process "a couple of issues if the model remembers."

Instead it performs this deterministic loop:

1. acquire a local lock so only one queue run is active on the machine
2. find the oldest open issue labeled `codex-queue`
3. claim it by adding `codex-active`
4. invoke Codex headlessly against the repository for that issue
5. read the structured result from Codex
6. update labels/comments based on that result
7. repeat until no queued issues remain

That outer loop is owned by the script, not by the model.

## Why This Matters

This directly addresses the failure mode where Codex says "I'll keep going through 50 items" but stops after 2 or 3. In this design:

- each Codex run is responsible for one issue only
- the queue worker is responsible for continuing to the next issue
- the queue drain only stops when the queue is empty, the worker is blocked, or the machine/process fails

## Headless Codex

The worker does not rely on the packaged WindowsApps alias for `codex.exe`, which is awkward to execute from unattended shells.

Instead, the bootstrap step copies the installed Codex CLI into a normal local path and executes that copy. The copied CLI still uses the existing `~/.codex` auth and configuration on the machine.

## Scheduling

The intended steady state is hybrid:

- GitHub issue events wake the queue worker immediately on the self-hosted runner
- a Windows Scheduled Task still wakes the queue worker periodically as a recovery mechanism
- each invocation drains the queue until empty
- if another invocation starts while one is already running, the lock file causes it to exit cleanly

This gives the machine fast reaction time without making webhooks or event delivery the only correctness path.

## Dashboard Push Events

The worker can optionally push signed lifecycle events to a remote dashboard backend so the dashboard changes status immediately when this side machine starts or finishes work.

Supported event transitions include:

- `worker_run_started`
- `issue_claimed`
- `issue_finished`
- `queue_empty`
- `worker_run_finished`
- `worker_run_failed`

The worker signs each event with a short-lived HS256 JWT and posts it to the configured dashboard endpoint.

### Worker-side setup

1. Store the shared JWT secret on the side machine.
2. Recommended secret name: `codex-queue-jwt-secret`
3. The worker looks for the secret in either:
   - environment variable `CODEX_QUEUE_JWT_SECRET`
   - PowerShell SecretManagement via `Get-Secret -Name codex-queue-jwt-secret -AsPlainText`

If you use PowerShell SecretManagement, an example is:

```powershell
Set-Secret -Name codex-queue-jwt-secret -Secret 'replace-with-long-random-secret'
```

### Endpoint configuration

The worker only pushes events when `DashboardEventUrl` is configured.

There are two intended ways to provide that:

- GitHub Actions wake path
  - set repository variable `CODEX_QUEUE_PUSH_EVENT_URL`
- Local scheduled task path
  - reinstall the scheduled task with `-DashboardEventUrl`

Example:

```powershell
.\ops\codex-queue\Install-IssueQueueWorkerTask.ps1 `
  -RepoRoot 'D:\repos\card-utility-stats' `
  -WorkerName 'sts2-side-a' `
  -DashboardEventUrl 'https://diagrams.romaine.life/ci/codex/push'
```

### Backend-side setup

The receiving dashboard backend must know the same shared secret.

For the `diagrams` backend this can come from either:

- Key Vault secret `codex-queue-jwt-secret`
- environment variable `CODEX_QUEUE_JWT_SECRET`

## Current Scope

The worker is repo-specific right now:

- repo: `nelsong6/card-utility-stats`
- local checkout: `D:\repos\card-utility-stats`
- worker name: `sts2-side-a`

That is intentional. The first milestone is to make the pattern real and reliable on one side machine. If it works, the queue worker can later be generalized into a shared automation repo.
