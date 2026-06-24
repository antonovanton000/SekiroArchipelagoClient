# Sekiro Archipelago Client

Sekiro Archipelago Client is a Windows client and runtime mod for playing
*Sekiro: Shadows Die Twice* as an Archipelago multiworld game.

The client connects to an Archipelago room, reads the room slot data, generates a
Sekiro randomizer mod locally, and then tracks in-game pickups through a native
DLL bridge. It is built on top of the existing Sekiro item and enemy randomizer
work, with additional Archipelago mapping, item placement, runtime pickup
tracking, and debugging tools.

This project is still in active development. Expect rough edges, especially when
testing new AP world versions or unusual room option combinations.

## Features

- Connects to Archipelago rooms as a Sekiro slot.
- Generates Sekiro game files from the Archipelago room configuration.
- Places Archipelago items into Sekiro item lots, shops, NPC rewards, boss drops,
  enemy drops, and other supported locations.
- Supports foreign items by creating synthetic in-game items with Archipelago
  descriptions.
- Tracks in-game pickups and sends completed location checks back to the
  Archipelago server.
- Supports key item tracking in the room screen.
- Supports DeathLink when enabled by room options.
- Supports enemy, boss, miniboss, Headless, and regular enemy randomizer options
  exposed by the Sekiro AP world.
- Supports challenge presets such as `Ashina Zoo`, `Nightmare Mode`, and
  `Oops All`.
- Includes a debug audit that verifies generated game files against the
  Archipelago location mapping.
- Includes an Item Tracker window for full manual verification against an
  Archipelago spoiler log.
- Adds English fallback text for generated foreign items in Russian game
  language mode.

# IMPORTANT
Unofficial Client Notice

This client is not an official Sekiro Archipelago client and is not affiliated with the Archipelago project or the From Software Archipelago team.

It was created and is maintained by me independently as a personal community project.

The client is currently stable, supports full playthroughs, is compatible with the latest Sekiro APWorld releases, and fully supports Enemy Randomizer gameplay.

If you are looking for the official Sekiro Archipelago client, please use:

https://github.com/fswap/from-software-archipelago-clients/releases?q=Sekiro

## Requirements

- Windows.
- Steam version of *Sekiro: Shadows Die Twice*.
- Sekiro version `1.06`.
- .NET 10 Desktop Runtime.
- An Archipelago room generated with a compatible Sekiro AP world.
- A clean Sekiro installation is strongly recommended.

Before playing, set Sekiro to offline mode in-game. This prevents online
penalties and avoids unnecessary network interaction while using modified game
files.

## Installation

1. Download the latest release archive.
2. Extract the archive.
3. Copy the extracted `randomizerAP` folder into the Sekiro game directory, next
   to `sekiro.exe`.

   Example:

   ```text
   steamapps/common/Sekiro/randomizerAP/
   steamapps/common/Sekiro/sekiro.exe
   ```

4. Run:

   ```text
   randomizerAP/SekiroAPClient.exe
   ```

5. Enter your Archipelago room address, player name, and password if the room
   requires one.
6. Click `Connect`.
7. Wait for the client to generate the local randomizer files.
8. Click `Launch Game` from the room page.

The generated mod files are written into the client output folder under:

```text
randomizer/
```

The client saves the current generated state to:

```text
ap_randomization_state.json
```

If this file matches the current room and slot, the client can reuse the existing
randomization state instead of regenerating everything.

## Basic Usage

1. Start `SekiroAPClient.exe`.
2. Connect to your Archipelago room.
3. Let the client generate the randomizer files.
4. Launch Sekiro through the client.
5. Play normally.
6. When you pick up an AP-tracked item, the native DLL reports the pickup to the
   client.
7. The client checks the corresponding Archipelago location.
8. Incoming Archipelago items are granted in-game through the DLL bridge.

The room page includes:

- A key item tracker for major progression items.
- A server log tab.
- A command box for Archipelago commands such as `!hint`, `!remaining`, and
  `!release`.
- A notifications toggle for received item messages.
- A `Rerandomize run` button if you need to regenerate the local world.

## Debug Mode

Debug mode is intended for development and verification.

To enable it:

1. Open the app.
2. Go to `Settings`.
3. Enable the `Debug` switch.
4. Reconnect or regenerate the room as needed.

Debug mode writes additional files near the client executable, including:

```text
debug_apIdsToItemIds.json
debug_apIdsToKeys.json
debug_itemCounts.json
debug_locations.json
missingApLocaitons.txt
apLocationToLotId.txt
randoaudit.txt
ap_randomization_state.json
```

The most important files are:

- `randoaudit.txt`: verifies that the generated game files contain the expected
  items, quantities, lots, shops, and event flags.
- `apLocationToLotId.txt`: shows the mapping from Archipelago location IDs to
  Sekiro lot/shop IDs and event flags.
- `ap_randomization_state.json`: saved runtime mapping used by the client and
  Item Tracker.

The native DLL also emits pickup and reward logs while Debug mode is active.
These logs are useful when checking whether a game pickup was detected by the
runtime hooks.

## Troubleshooting

### The game does not launch

- Make sure `randomizerAP` is inside the Sekiro directory next to `sekiro.exe`.
- Make sure the Steam version of Sekiro is installed.
- Make sure Sekiro is updated to version `1.06`.
- Remove old mod loader files from previous Sekiro mod setups if they conflict
  with this client.

### The client connects but items do not appear in-game

- Start the game through the client's `Launch Game` button.
- Check that the room page says `Connected to Game`.
- Enable Debug mode and check the DLL/debug logs.

### A pickup does not check the Archipelago location

- Enable Debug mode.
- Open `apLocationToLotId.txt` and confirm the AP location maps to the expected
  Sekiro lot or shop ID.
- Open `randoaudit.txt` and confirm the generated item matches the expected lot.
- Use Item Tracker and press `Report` on the problematic row.

### A location is already checked or inactive in-game

This usually means the relevant Sekiro event flag was already set or the location
uses a special flag/script path. Use Item Tracker's `Check Flags` button and
create an issue report for the row.

## Development Notes

The solution contains two main projects:

- `SekiroAPClient`: WPF application, Archipelago connection, randomizer
  generation, audit tools, Item Tracker, and pipe server.
- `SekiroInjector`: native DLL that hooks Sekiro runtime item pickup, shop,
  reward, event flag, DeathLink, and overlay-related behavior.

Important data folders:

- `SekiroAPClient/dists/Base`: base Sekiro data used by the randomizer.
- `SekiroAPClient/Randomizer`: integrated Sekiro randomizer code.
- `sekiro_apworld`: local reference copy of the Sekiro AP world.

## Credits

Special thanks to:

- thefifthmatt, whose Sekiro item and enemy randomizer code forms the foundation
  for major parts of this project.
- Yenix and contributors from the AP After Dark community for the Sekiro AP world
  integration.
- The Archipelago project and community.
- SoulsFormats for FromSoftware file format support.
- SoulsIds for structured game ID definitions and utilities.
- MinHook and Dear ImGui for native runtime support in the injector.
