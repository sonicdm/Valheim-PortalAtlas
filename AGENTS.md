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

Dedicated **Refresh world** is **Server Devcommands only** on the admin client (`PermissionManager.IsAdmin`), and the dedicated host must answer the Portal Atlas connect handshake (`PortalAtlas_Hello` / `HelloAck`). Do not add vanilla / Jötunn / `devcommands` fallbacks for dedicated admin. Listen host uses `ZNet.IsServer()` and does not need Server Devcommands or the handshake.

**Jötunn:** required dependency (client and dedicated). Skip UI with `GUIManager.IsHeadless()` on the server — do not strip the Jötunn assembly reference. Keep `ValheimModding-Jotunn-…` in `manifest.json`. Docker/dedicated: install Portal Atlas **and** Jötunn (normal Thunderstore / Gale install).

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

Build against the flattened folder:

```text
E:\Scripts\Valheim Mods\Reqs
```

Override with `-LibDir` on `build.ps1` / `package.ps1` / `release.ps1`.

**Newest dependency source (Gale Default profile):** when refreshing refs, prefer copying from:

```text
C:\Users\Allan\AppData\Roaming\com.kesomannen.gale\valheim\profiles\Default\BepInEx\
  core\          → BepInEx.dll, 0Harmony.dll
  plugins\ValheimModding-Jotunn\Jotunn.dll
  plugins\JereKuusela-Server_devcommands\ServerDevcommands.dll
```

Game / Unity assemblies still come from the Steam Valheim `valheim_Data\Managed` folder into `Reqs` (Gale plugins alone are not enough to compile).

- Never commit those DLLs or the Reqs folder.
- GitHub Actions only refreshes release notes from `CHANGELOG.md` on tag push.

## Build habits

```powershell
.\build.ps1
```

After code changes, verify with `.\build.ps1` before finishing. Prefer **build only** while iterating — do not package/release unless asked.

## Version bumps

Do not bump `1.2.6` until the user asks to ship. When shipping, keep these aligned: `PluginVersion`, csproj `<Version>`, `manifest.json` `version_number`, README Version row, `CHANGELOG.md` `## X.Y.Z`.

## Install

Do **not** copy the DLL into an r2modman profile. User installs themselves. Build output: `bin\Release\PortalAtlas.dll` and `dist\PortalAtlas.dll`.
