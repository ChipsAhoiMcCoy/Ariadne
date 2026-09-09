# Ariadne 1.0.0

Ariadne brings spoken menus, keyboard interaction, and spatial audio to Terraria through tModLoader, for blind and low-vision players.

**[Download 1.0.0](https://github.com/ChipsAhoiMcCoy/Ariadne/releases/tag/v1.0.0)** · **[Player guide and keybinds](docs/player-guide.md)** · **[Settings reference](docs/settings.md)** · **[Credits and licenses](Mods/Ariadne/ThirdParty/README.md)**

## Install

1. Install Terraria and tModLoader through Steam. This release's speech requires 64-bit Windows 10 or later.
2. Download **Ariadne.tmod** from the release's Assets section. GitHub's Source code downloads are for developers.
3. In tModLoader, use **Workshop → Manage Mods → Open Mods Folder**. Close tModLoader and copy `Ariadne.tmod` there. The usual Windows location is `Documents\My Games\Terraria\tModLoader\Mods`; redirected Documents and custom save paths can change it.
4. Start your screen reader, launch tModLoader, enable **Ariadne** in Manage Mods, and reload mods.
5. Once loaded, press **Alt+H** for contextual help. Open **Ariadne Sound Guide** on the main menu to learn the sounds.

The `.tmod` is the only Ariadne file players need to install. It includes Prism, localization, and third-party notices. Ariadne cannot speak the initial setup screens before it is enabled; assistance may be needed for first-time setup.

## Features

- Accessible character/world creation, multiplayer setup, settings, Workshop tools, and hierarchical inventory menus.
- Keyboard world cursor, Smart Cursor support, combat targeting, NPC services, shops, crafting, and housing queries.
- Spatial terrain/enemy cues, radar, footsteps, climbing/ledge feedback, and health/breath announcements.
- Visible-surroundings scanner with teleport/interact actions, world waypoints, semantic map, and freecam exploration.
- Spoken status, chat history, pickups, summons, biome changes, and an interactive sound guide.

Custom screens provide the most complete support. Other screens receive a best-effort keyboard adapter; Steam/browser/Explorer windows use their own accessibility. See the [coverage matrix](docs/accessibility-screen-coverage.md).

Speech and braille integration uses **Prism 0.17.3 by ethindp and the Prism contributors**, under MPL-2.0. [Bundled credits](Mods/Ariadne/ThirdParty/README.md) include upstream notices, dependency licenses, and corresponding source links.

## Development

`Mods/Ariadne/` contains the mod. Install tModLoader, then run:

```powershell
.\Tools\build.ps1
```

Set `TML_INSTALL_PATH` for a nonstandard Steam location. The build stages source under the Windows Public profile to avoid embedding a personal source path. Distribute the packaged `.tmod`, never a bare DLL or your `enabled.json`.

Local decompiled game references are intentionally untracked and must not be published. Navigation maps are in [docs/decompilation](docs/decompilation). See the [1.0.0 release notes](docs/releases/1.0.0.md).
