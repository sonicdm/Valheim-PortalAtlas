# Portal Atlas

Personal portal atlas for Valheim. Record gates as you find them; admins can Refresh the full world list. Vanilla tag pairing stays.

| | |
| --- | --- |
| Version | 1.2.3 |
| GUID | `sonicdm.valheimportallist` |
| Dependencies | BepInExPack Valheim, Jötunn |

## How to use

1. Approach, interact with, or travel through a portal — it is saved to your local journal.
2. Open the large map (`M`) and click **Portals**, or press the configured toggle key (default `P`) / run `portals`.
3. Search tags, sort by name, distance to you, or a map click (nearest to that point).
4. Connection status shows **connected**, **one-way**, **unconnected**, or **missing**.
5. **Pin** saves a permanent map pin for the selected portal. **Ping** / **Ping exit** center the map and ping that portal or its paired exit (only one player ping can be active at a time). Temporary orange overlay markers appear while the panel is open (not saved).
6. Optional **Auto-pin when I approach** creates permanent portal pins as you walk up (off by default).

### Refresh world

Never runs automatically when the panel opens. **Does not write your journal.**

- **Dedicated admin** (`adminlist.txt`, synced via Jötunn): Refresh pulls the live server list over RPC.
- **Offline / listen host**: Refresh scans the local world (no `devcommands` required).
- Use **Add to journal** on a selected row if you want that portal saved.
- **Show known** returns to the journal.

Approach range for auto-record (and Auto-pin) is configurable: `Pins.ApproachRangeMeters` (default 8).

**Troubleshooting:** set `General.DebugLogging = true` in the mod config. Detailed `[DEBUG]` lines for journal, approach, pins, UI, refresh RPC, access checks, and world scans go to `BepInEx/LogOutput.log`.

**Clear saved** only removes pins this mod created (private name marker). Manually placed map pins are never touched.

### Files

Under `BepInEx/cache/PortalAtlas/` (not config):

- Journal JSON per world + character
- Host CSV / tag summary / text report (startup dump and `portallist`)

## Commands

```text
portals
portallist
```

## Install

Place the DLL (or Thunderstore zip) into your BepInEx plugins via r2modman. Install on the dedicated server for admin Refresh and CSV dumps; the client journal works even if the server lacks the mod.
