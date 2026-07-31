# Steam Workshop publishing

SpireLens is published on the Steam Workshop as item
[3774710835](https://steamcommunity.com/sharedfiles/filedetails/?id=3774710835)
(first upload 2026-07-30, visibility private until flipped public on the item page).

## Workspace

Publishing happens from a standalone workspace **outside this repo** (binaries, not committed):

```
D:\repos\spirelens-workshop\
├── ModUploader.exe      Megacrit's official uploader (v0.2.0)
├── steam_api64.dll      ships with the uploader
├── workshop.json        Workshop metadata (changeNote, deps; title/description/visibility null — see below)
├── image.png            thumbnail, PNG, must be < 1MB (Steam backend limit)
├── mod_id.txt           3774710835 — written by the first upload; makes later runs update instead of create
└── content\
    └── SpireLens\       exact copy of the deployed mod folder
        ├── SpireLens.json
        ├── SpireLens.dll
        ├── SpireLens.Core.dll
        └── Mono.Cecil*.dll (4 files)
```

- Uploader source/releases: <https://github.com/megacrit/sts2-mod-uploader>
- `content\` nests the `SpireLens\` mod folder (matches how BaseLib's workshop item is laid out;
  subscribed items land at `steamapps\workshop\content\2868840\<item-id>\<ModName>\`).
- `workshop.json` `dependencies` is a list of **numeric workshop item IDs**. BaseLib = `3737335127`.
  The in-game dependency (`SpireLens.json` → `"id": "BaseLib"`) is separate and also required.

## Publishing an update

1. Bump `"version"` in [SpireLens.json](../SpireLens.json) (repo root, source of truth). SemVer-ish:
   minor for features, patch for fixes. The string is shown to subscribers in-game.
2. Build/deploy as usual, then refresh the workspace copy:

   ```powershell
   Copy-Item "D:\SteamLibrary\steamapps\common\Slay the Spire 2\mods\SpireLens\*" `
             "D:\repos\spirelens-workshop\content\SpireLens\" -Force
   ```

   (Same file set the GitHub release packaging in
   [.github/workflows/release.yml](../.github/workflows/release.yml) stages.)
3. Set `changeNote` in `workshop.json`.
4. Steam client running and logged in, then:

   ```powershell
   Set-Location "D:\repos\spirelens-workshop"; .\ModUploader.exe upload -w "D:\repos\spirelens-workshop"
   ```

Success looks like `Status: k_EItemUpdateStatusCommittingChanges` →
`Successfully uploaded 'SpireLens' ...`. The trailing `k_EItemUpdateStatusInvalid`
line is the SDK's idle state, not an error.

## workshop.json clobbers page edits

Every upload pushes every **non-null** `workshop.json` field to Steam — including title,
description, and visibility. An upload with those fields set will silently overwrite anything
edited on the item's web page (this ate a page-written description once). The workspace file
therefore keeps `title`, `description`, and `visibility` at `null` — those are managed on the
item page — and only `changeNote`, `tags`, `dependencies`, and `contentDescriptors` live in
the file. Null/omitted fields are left untouched by the uploader.

## Gotchas observed on first publish

- **"There was a problem accessing the item"** on the item page right after upload — combination of
  propagation lag and Steam's automated content scan. New commits show *"awaiting analysis by our
  automated content check system"* and stay hidden until the scan passes (seconds-to-hours; no SLA;
  every commit re-triggers it). Not an upload failure — the uploader reading the item back
  ("Querying existing workshop item details") proves it exists.
- Anonymous probes are useless while the item is private: the page shows the same generic error,
  and `ISteamRemoteStorage/GetPublishedFileDetails` returns `result: 9` regardless of scan state.
- The `SpireLens.json` version string exists in three places once deployed: repo root, the deployed
  copy under the game's `mods\`, and the workspace `content\` copy. Repo is the source of truth;
  the other two are overwritten by deploy/copy.
