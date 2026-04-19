# CardStats

Per-card attribution stats mod for [Slay the Spire 2](https://store.steampowered.com/app/2868840/Slay_the_Spire_2/). For every card you play, tracks what actually happened — effective damage vs. overkill, block that absorbed vs. wasted, drawn cards played vs. idle, energy generated vs. unused.

**Status:** Early WIP. Scaffold only. No actual attribution code yet — see [issue #5](https://github.com/nelsong6/card-stats/issues/5) for M1 progress.

## Why

Existing stats mods answer "how often did I *pick* this card" ([SlayTheStats](https://www.nexusmods.com/slaythespire2/mods/349)) or "how much value did this *relic* provide" ([Relic Stats](https://www.nexusmods.com/slaythespire2/mods/327)). Nothing tracks how much of what each card *attempted* actually mattered. A 6-damage Strike into a 4-HP enemy and a 6-damage Strike into a fresh elite look the same on a play counter, but they have very different value.

## What it tracks (target design)

**Attack cards** — four numbers per play:

- `raw_damage_intended` — damage the card tried to deal (after buffs/debuffs)
- `blocked_by_target` — enemy block that absorbed some
- `overkill` — damage past enemy HP (wasted)
- `effective_damage` — what actually counted

**Block cards** — how much of the generated block actually absorbed incoming damage vs. expired unused. Per-card block attribution uses a heuristic (see [issue #1](https://github.com/nelsong6/card-stats/issues/1)).

**Utility cards** — closure tracking:

- Energy generated: was it spent or end-of-turn wasted?
- Cards drawn: were they played this turn/run or sit in hand?

## How you'd use it

A **"View Stats"** checkbox sits next to the game's existing "View Upgrades" toggle on deck-view screens (current run and past runs). Mutually exclusive with View Upgrades — it replaces the card's description text with its attribution stats for that run. Available only on screens showing cards in your actual deck (not on the generic Compendium, since lifetime aggregation doesn't exist yet — see [issue #2](https://github.com/nelsong6/card-stats/issues/2)).

## Roadmap

| Milestone | Scope | Status |
|---|---|---|
| **M1** | Attack damage attribution — the 4 numbers above | [#5](https://github.com/nelsong6/card-stats/issues/5) |
| **M2** | Block attribution (needs [#1](https://github.com/nelsong6/card-stats/issues/1) resolved) | — |
| **M3** | Utility card closure (energy, draw) | — |
| **M4** | In-game UI: "View Stats" checkbox on deck view | — |
| **M5** | Run History integration — browse past-run stats | — |
| **M6** | Publish v0.1 to Nexus | — |

## Storage

Per-run JSON files at `<game>/mods/CardStats/runs/<run-id>.json`. Contains both aggregated stats (fast for UI) and a full event log (one entry per card-played event, for future analysis). Schema versioned — see [issue #4](https://github.com/nelsong6/card-stats/issues/4).

## Requirements

- Slay the Spire 2 (tested against v0.103.2)
- [BaseLib](https://www.nexusmods.com/slaythespire2/mods/103) — required dependency

## Install

Drop `CardStats.dll` and `CardStats.json` (and `CardStats.pck` once UI lands) into `<game install>/mods/`. Requires BaseLib.

## Build from source

**Prereqs:** .NET 9 SDK, Slay the Spire 2 installed locally.

```sh
# If the path discovery in Sts2PathDiscovery.props doesn't find your game,
# create Directory.Build.props with your path:
cat > Directory.Build.props <<'XML'
<Project>
    <PropertyGroup>
        <Sts2Path>D:/SteamLibrary/steamapps/common/Slay the Spire 2</Sts2Path>
    </PropertyGroup>
</Project>
XML

dotnet build -c Release
```

The build's `CopyToModsFolderOnBuild` target auto-deploys the `.dll` and manifest to `<game>/mods/CardStats/`. No manual copy step.

## Credits

- Scaffolded from [Alchyr/ModTemplate-StS2](https://github.com/Alchyr/ModTemplate-StS2)
- Concept inspired by the gap between pick-rate trackers and actual-impact tracking
- BaseLib by [Alchyr](https://www.nexusmods.com/slaythespire2/mods/103)

## License

MIT.
