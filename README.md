# Sekiro Archipelago Client

Sekiro Archipelago Client is a Windows/Linux client and runtime mod for playing
*Sekiro: Shadows Die Twice* as an Archipelago multiworld game.

The latest release of the apworld can be found at: 
https://github.com/yenix4/ArchipelagoSekiro/releases

The setup guide can be found in the apworld repo:
https://github.com/yenix4/ArchipelagoSekiro/blob/main/worlds/sekiro/docs/setup_en.md

This client connects to an Archipelago room, reads the room slot data, generates a
Sekiro randomizer mod locally, and then tracks in-game pickups through a native
DLL bridge. It is built on top of the existing Sekiro item and enemy randomizer
work, with additional Archipelago mapping, item placement, runtime pickup
tracking, and debugging tools.

## Features

- Connects to Archipelago rooms as a Sekiro slot.
- Generates Sekiro game files from the Archipelago room configuration.
- Places Archipelago items into Sekiro item lots, shops, NPC rewards, boss drops,
  enemy drops, and other supported locations.
- Supports foreign items by creating synthetic in-game items with Archipelago
  descriptions.
- Tracks in-game pickups and sends completed location checks back to the
  Archipelago server.
- Supports all options available in the Sekiro apworld.
- Supports challenge presets such as `Ashina Zoo`, `Nightmare Mode`, and
  `Oops All`.
- Includes a debug audit that verifies generated game files against the
  Archipelago location mapping.
- Adds English fallback text for generated foreign items in Russian game
  language mode.

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

The room page includes:

- A key item tracker for major progression items, including an overlay.
- A server log tab.
- A command box for Archipelago commands such as `!hint`, `!remaining`, and
  `!release`.
- A notifications toggle for received item messages.
- A `Rerandomize run` button if you need to regenerate the local world.

## Development Notes

The solution contains two main projects:

- `SekiroAPClient`: WPF application, Archipelago connection, randomizer
  generation, audit tools, Item Tracker, and pipe server.
- `SekiroInjector`: native DLL that hooks Sekiro runtime item pickup, shop,
  reward, event flag, DeathLink, and overlay-related behavior.

Important data folders:

- `SekiroAPClient/dists/Base`: base Sekiro data used by the randomizer.
- `SekiroAPClient/Randomizer`: integrated Sekiro randomizer code.

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
