# Portal Atlas

Personal portal journal and map panel for Valheim. Record gates as you find them, sort and search them on the large map, and optionally Refresh the full world list as a dedicated admin. Vanilla tag pairing stays.

![Portal Atlas panel on the large map](media/portals-panel.png)

| | |
| --- | --- |
| Version | 1.2.5 |
| GUID | `sonicdm.valheimportallist` |
| Dependencies | BepInExPack Valheim, Jötunn, Server Devcommands *(dedicated Refresh)* |

## Features

- Opens from the large map **Portals** button or the `portals` command
- Builds a **known-portal journal** as you approach, interact with, or travel through portals
- Filter by tag; sort by name, distance to you, or a map click
- Connection status: **connected**, **one-way**, **unconnected**, **missing**
- **Pin** for a permanent map pin; **Ping** / **Ping exit** to center the map (one player ping at a time)
- Temporary orange overlay markers while the panel is open (not saved)
- Optional **Auto-pin when I approach** (off by default)
- **Refresh world** for dedicated admins (session-only; never writes the journal)

## How to use

1. Walk up to, use, or travel through a portal — it is saved to your local journal.
2. Open the large map (`M`) and click **Portals**, or run `portals`.
3. Search tags and sort as needed.
4. Select a row for details, then **Pin**, **Ping**, or **Ping exit**.
5. Admins can **Refresh world** for a live server-wide list, then **Add to journal** on rows they want to keep. **Show known** returns to the journal.

Approach range for auto-record (and Auto-pin): `Pins.ApproachRangeMeters` (default `8`).

**Clear saved** only removes pins this mod created. Manually placed map pins are never touched.

## Refresh world (admins)

Never runs when the panel opens. Does **not** write your journal.

| Setup | What you need |
| --- | --- |
| Dedicated server | Portal Atlas **and Jötunn** on the server (UI is skipped headless). [Server Devcommands](https://thunderstore.io/c/valheim/p/JereKuusela/Server_devcommands/) on the **admin client and the server**, plus your id in `adminlist.txt` |
| Offline / listen host | Portal Atlas + Jötunn — Refresh scans the local world |

Refresh is shown only when Server Devcommands reports you as admin. You do not need to enable `devcommands`.

## Install

Install with **Gale / r2modman / Thunderstore** — `manifest.json` pulls **Jötunn** and **Server Devcommands**.

| Role | What to install |
| --- | --- |
| Game client | Portal Atlas (pulls Jötunn). Server Devcommands for dedicated Refresh |
| Dedicated server | Portal Atlas **and** Jötunn (same as any other Jötunn mod). Server Devcommands if you use admin Refresh |

## Commands

| Command | Purpose |
| --- | --- |
| `portals` | Toggle the Portals panel (opens the large map if needed) |
| `portallist` | Export every portal to CSV under `BepInEx/cache/PortalAtlas/` (host, or dedicated admin) |

## Files

Under `BepInEx/cache/PortalAtlas/` (not config):

- Journal JSON per world + character
- Host CSV / tag summary / text report (startup dump and `portallist`)

## Troubleshooting

Set `General.DebugLogging = true` in the mod config. Detailed `[DEBUG]` lines for journal, approach, pins, UI, refresh RPC, access checks, and world scans go to `BepInEx/LogOutput.log`.
