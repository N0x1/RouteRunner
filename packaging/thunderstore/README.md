# Route Runner

Learn a route. Record your movement. Share it with a friend.

Route Runner adds movement ghosts to **STRAFTAT exploration mode**, making it easier to practise jumps, movement sequences and routes across the game's maps. Follow your own recording or import someone else's take.

## Features

- Record and replay a ghost of your character's movement.
- Organise routes by map, with search and an all-maps view.
- Share `.sroute` files or copy a route code to your clipboard.
- Trim the start and end of a take, with previews and backups.
- Adjust playback speed, loop routes, scrub the timeline and return to the start.
- Customise ghost colour, transparency, countdown and keybinds.
- Hide the HUD or switch the mod off through the game's Mods menu.
- Select multiple routes for deletion, with confirmation and recovery.

**Route Runner works in exploration mode only.** The ghost is visual: it has no collision, damage or player controller.

## Installation

Install **Route Runner** using r2modman or Thunderstore Mod Manager, then launch STRAFTAT with **Start modded**. BepInExPack and Kestrel's Mod Menu are installed as dependencies.

If upgrading from a manually imported copy, remove or disable that old copy before installing this package. Keep your saved routes and configuration; only one Route Runner DLL should be enabled in the profile.

For manual installation, install the listed dependencies first, then copy `plugins/RouteRunner/RouteRunner.dll` into `BepInEx/plugins/RouteRunner/` in your STRAFTAT installation.

## Quick start

1. Enter an exploration map.
2. Press **F6** to open the route panel and name your next take.
3. Press **Record**, perform your route, then press **F7** to save.
4. Press **F10** to return to the route's start, then **F8** to play the ghost.
5. Follow the ghost after the countdown.

| Default key | Action |
| --- | --- |
| F6 | Open / close the panel |
| F7 | Start recording / finish and save |
| F8 | Play / restart the selected ghost |
| F9 | Save the current recording and remove the ghost |
| F10 | Return to the selected route's start |

Opening the panel ends an active recording and pauses ghost playback. Close it to move and follow the ghost. If Escape opens the game's pause menu, choose Resume to return to practice.

All bindings can be changed in **Settings → Mods → Route Runner**. Existing custom bindings are retained when updating.

## Settings

Open **Settings → Mods → Route Runner** for keybinds, recording limits, ghost appearance, countdown and panel size.

- **Enable Mod:** switch Route Runner off or on without restarting. Disabling saves a valid current recording, removes the ghost and closes the panel. Saved routes are kept.
- **Show HUD:** hide or show all gameplay banners, timers and notifications. The panel and route actions still work with the HUD hidden.
- **Idle hotkey hint:** show a persistent shortcut reminder. Off by default; requires Show HUD.
- **Colour:** enter RGB or RGBA hex, with or without `#`. RGB keeps your current transparency; RGBA includes alpha. Press Enter or leave the field to apply.
- **Transparency:** 0% is solid; 100% is invisible.

Settings save automatically. If Mod Menu is unavailable, the route panel provides fallback controls. To re-enable a disabled mod without Mod Menu, close the game and set `Enable Mod = true` in `BepInEx/config/practice.straftat.routerunner.cfg`.

## Sharing routes

### Send a route

Select a route in **Routes**, open **Sharing**, choose **Export file**, then **Open exports folder**. Send the `.sroute` file to your friend.

### Import a route

Open **Sharing → Open imports folder**, place the received `.sroute` file there, and select **Import files**. Use **All maps** in the route library to find it, then enter that exact map in exploration to practise.

For short takes, **Copy route code** and **Import clipboard code** provide a text alternative. A code contains the entire recording and can exceed chat limits; files are better for longer routes.

Both players need access to the recorded map, including any required DLC. Route sharing does not unlock maps. Use matching game and mod versions where possible.

## Playback and trimming

The **Playback** tab offers speed control, looping, pause/resume and timeline scrubbing. Scrubbing pauses playback; resume and close the panel to continue.

To trim a route, load it and select **Edit selected route**. Adjust the start and end sliders, preview the edges, then **Save trim**. Saving updates the route and keeps the previous file in **Backups**. Closing the panel or changing tabs cancels unsaved edits.

## Deleting and recovering routes

Click a route to load it; tick its checkbox to select it for deletion. **Delete checked** and **Clear all routes** ask for confirmation. Clear all applies across every map, regardless of the current filter.

Deleted routes move to **Deleted**. **Undo last delete** restores the most recent batch in the current session. To recover older deleted routes or trim backups, copy their `.sroute` files into **Imports**, then import them normally. Imports, exports and backups are not removed by Clear all.

## Saved files

All paths are relative to the BepInEx folder used by your game or mod-manager profile:

| Location | Contents |
| --- | --- |
| `RouteRunner/Maps` | Saved routes |
| `RouteRunner/Imports` | Received route files |
| `RouteRunner/Exports` | Exported route files |
| `RouteRunner/Deleted` | Recoverable deletions |
| `RouteRunner/Backups` | Previous trimmed versions |
| `config/practice.straftat.routerunner.cfg` | Settings |

Use **Sharing → Open route library folder** to find the active library. Direct game launches and mod-manager profiles can use different libraries. Earlier RouteGhost data in the same BepInEx installation is copied to the Route Runner locations; originals are preserved.

## Notes

- Built for Windows Mono STRAFTAT, against game version 1.4.8a.
- The default recording rate is 30 pose samples per second, with a three-minute limit. File-size limits also apply.
- Opening a game menu, respawning, leaving exploration or changing maps ends a recording.
- Ghosts replay character movement, not map state, moving platforms, weapon actions or destructibles.
- Return to start resets position, facing and main movement velocity. It does not rewind weapons, cooldowns or the map.
- The HUD is inactive when nothing needs displaying and the idle hint is off. Recording and rendering still have a performance cost; results vary by system and mod combination.

If a route does not play, check that you are in exploration on its exact map. For errors, check `BepInEx/LogOutput.log` for **Route Runner**.

## Credits and licence

Thanks to Lemaitre Logiciels for STRAFTAT and its public reference source, Kestrel for Mod Menu, and the BepInEx project for the mod-loading framework.

Route Runner is an independent mod, distributed under the MIT licence. Game assemblies and dependency binaries are not included in this package.
