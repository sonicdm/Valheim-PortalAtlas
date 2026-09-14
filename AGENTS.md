# AGENTS.md — Portal Atlas

Guidance for Cursor agents (and humans) working in this repo.

## What this is

Valheim **BepInEx** mod: personal portal atlas + map panel. Vanilla tag pairing stays. Full world list is opt-in Refresh only.

| | |
| --- | --- |
| GUID | `sonicdm.valheimportallist` |
| Assembly | `PortalAtlas.dll` |
| Source | `src/` |
| GitHub | https://github.com/sonicdm/Valheim-PortalAtlas |

## Product rules

- Panel opens on the **known-portal journal**, never an automatic world dump.
- **Refresh world** is explicit and **session-only** — it never writes the journal. Use **Add to journal** to save a selected world-list row.
- Journal JSON and CSV/txt dumps live under `BepInEx/cache/PortalAtlas/` — **not** `BepInEx/config` (r2modman config UI).
- Approach a loaded portal to record it (`Pins.ApproachRangeMeters`, default 8); Auto-pin (default off) is the only permanent pin path.
- Temporary map overlay while the panel is open; tinted differently from saved pins.
- Map **Portals** button opens the dedicated wood panel.
- Pin remove/clear only affects pins this mod owns (private name marker). Never touch manually placed pins.
- Still read-only on world data: do not change portal tags or connections.

UX (Jötunn):

- `GUIManager.CreateWoodpanel` / `CreateInputField` / `CreateButton`
- Subscribe `GUIManager.OnCustomGUIAvailable`; skip GUI when `GUIManager.IsHeadless()`
- Block movement only while a search field is focused; do not hold `BlockInput` for the whole map session

## Valheim references (local only)

```text
E:\Scripts\Valheim Mods\Reqs
```

Override with `-LibDir` on `build.ps1` / `package.ps1` / `release.ps1`.

- Never commit those DLLs or the Reqs folder.
- GitHub Actions only refreshes release notes from `CHANGELOG.md` on tag push.

## Build habits

```powershell
.\build.ps1
```

After code changes, verify with `.\build.ps1` before finishing. Prefer **build only** while iterating — do not package/release unless asked.

## Version bumps

Do not bump `1.2.3` until the user asks to ship. When shipping, keep these aligned: `PluginVersion`, csproj `<Version>`, `manifest.json` `version_number`, README Version row, `CHANGELOG.md` `## X.Y.Z`.

## Install

Do **not** copy the DLL into an r2modman profile. User installs themselves. Build output: `bin\Release\PortalAtlas.dll` and `dist\PortalAtlas.dll`.
