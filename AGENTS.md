# AGENTS

## Current Truths

- Runtime is split into a stable loader and a hot-reloaded core.
  - [Loader/LoaderMain.cs](Loader/LoaderMain.cs) owns the long-lived bootstrap and `F5` reload flow.
  - [Core/CoreMain.cs](Core/CoreMain.cs) owns Harmony patch install/uninstall and re-entry on each reload.
- Persistence is combat-boundary based.
  - [Core/RunTracker.cs](Core/RunTracker.cs) buffers live combat data in `_pendingCombat`.
  - Nothing is promoted to the permanent run file until combat ends.
  - Reload between combats / between floors is supported and expected.
  - Mid-combat restore is intentionally out of scope.
- The data model is additive through schema `v14`.
  - [Core/RunData.cs](Core/RunData.cs) is the source of truth for the current schema.
  - [Tests/SpireLens.Core.Tests/SchemaLoadingTests.cs](Tests/SpireLens.Core.Tests/SchemaLoadingTests.cs) and the checked-in fixtures pin what remains resumable.
- Card identity is per physical card when the card has stable deck identity.
  - Instance numbers never get reused within a run.
  - Combat-generated cards that do not meaningfully exist in the deck may use pooled summaries instead of fake deck-instance identities.
- Tooltip style is intentionally quiet.
  - Hand view stays compact.
  - Rows should be self-describing without noisy section headers.
  - Inline keyword icons are preferred when they improve scanability without making the layout louder.
  - When the game already has a recognizable asset for the stat, prefer that in-game icon over a generic label.

## Start Here

- Read [README.md](README.md) for the product-level overview.
- Read [docs/architecture.md](docs/architecture.md) for subsystem layout and data flow.
- For tracking behavior, start in [Core/RunTracker.cs](Core/RunTracker.cs).
- For tooltip/UI behavior, start in:
  - [Core/Patches/ViewStatsInjectorPatch.cs](Core/Patches/ViewStatsInjectorPatch.cs)
  - [Core/Patches/CardHoverTooltipPatch.cs](Core/Patches/CardHoverTooltipPatch.cs)

## When Changing Behavior

- If you add persisted fields:
  - bump `RunData.CurrentSchemaVersion`
  - add or update fixture files under [Fixtures/RunSchema](Fixtures/RunSchema/README.md)
  - update [SchemaLoadingTests.cs](Tests/SpireLens.Core.Tests/SchemaLoadingTests.cs)
- If you change tooltip presentation:
  - preserve the compact-vs-full distinction
  - keep labels self-describing
  - avoid adding loud headers unless they clearly earn their space
- If you add new attribution:
  - be explicit when attribution is heuristic, pooled, contributor-ledger based, or case-specific

## Useful Commands

- Build/tests:
  - `dotnet test D:\repos\SpireLens\Tests\SpireLens.Core.Tests\SpireLens.Core.Tests.csproj -c Debug`
- Focused schema tests:
  - `dotnet test D:\repos\SpireLens\Tests\SpireLens.Core.Tests\SpireLens.Core.Tests.csproj -c Debug --filter SchemaLoadingTests`
- Focused tooltip tests:
  - `dotnet test D:\repos\SpireLens\Tests\SpireLens.Core.Tests\SpireLens.Core.Tests.csproj -c Debug --filter PoisonTooltipTests`
