# Changelog

## 1.2.6

- Client **handshake** on connect (`PortalAtlas_Hello` / `HelloAck`): Refresh world only works when the dedicated host has Portal Atlas **and** Server Devcommands reports admin.
- Panel shows a **Refresh status line** (ready / checking server / missing mod / need admin) and greys the button when Refresh is unavailable.

## 1.2.5

- Refresh world on a **dedicated server** uses Server Devcommands on the admin **client** for the button, and **PortalAtlas.dll** on the dedicated host for the scan RPC.
- Dedicated host needs **Jötunn** as well (standard headless skip via `GUIManager.IsHeadless()`); one DLL for client and server.
- Refresh / host CSV scan is **time-sliced** (~2ms per frame) so the server keeps networking while scanning; uses `GetPortalList` instead of a full ZDOMan walk.

## 1.2.4

- Removed the configurable toggle key (default `P`). Open the panel from the large map **Portals** button or the `portals` command.

## 1.2.3

- Renamed to **Portal Atlas** (Thunderstore `PortalAtlas`, GitHub `Valheim-PortalAtlas`).
- Cache folder is now `BepInEx/cache/PortalAtlas/` (copies existing files from `ValheimPortalList` once if present).
- GUID unchanged (`sonicdm.valheimportallist`) so BepInEx config carries over.

## 1.2.2

- Dedicated Portals panel from the large map button, toggle key (default `P`), or `portals`.
- Personal known-portal journal: record on approach / interact / teleport; stored under `BepInEx/cache/PortalAtlas/`.
- Full world list only after explicit **Refresh world** (session-only; never writes the journal). Use **Add to journal** to keep a world row.
- Refresh access: dedicated clients via Jötunn `PlayerIsAdmin` / `adminlist.txt`; local and listen hosts can always refresh.
- **Ping** / **Ping exit** open the map, center on the portal, and place the player ping.
- Panel closes when the large map closes.
- Toggle key ignored while chat, console, rename prompts, or the panel filter have focus.
- Temporary tinted map overlay while the panel is open; optional Auto-pin for permanent portal pins.
- Connection status (connected / one-way / unconnected / missing) using live portal connection ZDO links.
- Map-click nearest sort; scrollable list; map zoom blocked while the cursor is over the panel.
- Debounced journal saves and lighter approach / UI polling to reduce hitching.
