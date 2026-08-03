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

## Normal release flow

The normal release trigger is a merged version bump, not a manually pushed tag:

1. Put the player-facing bullets in each feature pull request's `## Workshop notes` section.
2. Open and merge a release pull request that bumps `SpireLens.json` to the next version.
3. After the merged `main` build succeeds, the release workflow creates the matching tag and
   dispatches the tagged publication automatically.
4. The tagged run creates the GitHub release and uploads the same package and changelog to Steam.

Manually pushing a matching `v*` tag remains a recovery path, but it is not part of the normal
release process.

## Manual workspace publishing

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

## Automated releases

[The release workflow](../.github/workflows/release.yml) publishes the exact
packaged mod to Workshop item `3774710835`. A successful merged `main` build creates and dispatches
the manifest's tag when that version has not been released yet; an explicit `v*` tag still works as
a recovery trigger. The tagged publication job:

1. requires the tag to exactly match `SpireLens.json`'s version;
2. validates the package allowlist;
3. restores an isolated SteamCMD login from the protected `steam-workshop`
   GitHub environment;
4. builds one set of player-facing notes and uses it for both the GitHub
   release and Workshop change note;
5. uploads only content and a change note, leaving page-managed metadata alone;
6. serializes uploads so two tags cannot update the item concurrently.

Release notes come from every merged pull request between the previous tag and
the new tag. Put player-facing bullets in the pull request's
`## Workshop notes` section. If the section is absent or empty, the pull request
title is used as a fallback. Maintenance-only pull requests can opt out by
putting exactly `No player-facing changes.` in that section. Direct commits that
were not associated with a pull request fall back to their commit subject.

The Steam VDF keeps the notes' real line-feed characters inside the quoted
`changenote` value. Do not replace them with a literal `\n`: Steam displays
those two characters verbatim instead of creating a line break.

The environment itself accepts deployments only from tags matching `v*`.

Bootstrap or refresh that environment credential from a trusted Windows machine:

```powershell
.\scripts\bootstrap-steam-workshop-secret.ps1 -SteamUsername <steam-login-name>
```

The script opens SteamCMD for one interactive password/Steam Guard login. It
stores the resulting isolated `config.vdf` as the environment secret
`STEAM_CONFIG_VDF_B64` and the non-secret login name as `STEAM_USERNAME`.
It does **not** upload the Steam password or Steam Guard code. Do not substitute
the desktop Steam client's broad `config.vdf`; use the isolated credential made
by this script.

The Workshop upload itself runs only in the tagged publication job. Pull requests cannot access the
protected publishing secret, and ordinary `main` pushes only queue that tagged job when the manifest
contains a version that has not been released yet.

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
