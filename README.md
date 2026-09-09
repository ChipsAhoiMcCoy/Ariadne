# Ariadne 1.0.0

Ariadne brings spoken menus, keyboard interaction, and spatial audio to Terraria through tModLoader, for blind and low-vision players.

**[Download 1.0.0](https://github.com/ChipsAhoiMcCoy/Ariadne/releases/tag/v1.0.0)** · **[Player guide and keybinds](docs/player-guide.md)** · **[Settings reference](docs/settings.md)** · **[Credits and licenses](Mods/Ariadne/ThirdParty/README.md)**

## Install

1. Install Terraria and tModLoader through Steam. This release's speech requires **64-bit Windows 10 or later**.
2. Download **Ariadne.tmod** and **enabled.json** from the [1.0.0 release Assets](https://github.com/ChipsAhoiMcCoy/Ariadne/releases/tag/v1.0.0). Keep both filenames exactly as downloaded, especially `enabled.json` rather than `enabled.json.txt`.
3. **Keep tModLoader closed.** In File Explorer, open your Windows Documents folder, then `My Games\Terraria\tModLoader\Mods`. You can press **Windows+R**, type `shell:Personal`, and press Enter to open Documents using the keyboard, including redirected Documents folders. Create missing folders if this is a fresh installation. If you deliberately use a custom tModLoader save location or preview branch, use its Mods folder instead.
4. For a fresh installation, copy **both files** into that Mods folder. `enabled.json` contains only Ariadne and tells tModLoader to load it automatically. You do not need to navigate the game's Enable button. Do not unzip `Ariadne.tmod` or put either file in Terraria's Steam installation folder.
5. Start your screen reader, then launch **tModLoader** from Steam. Ariadne will be enabled during startup. Open **Ariadne Sound Guide** on the main menu to learn the sounds; press **Alt+H** for help.

**Already have mods?** Back up your existing `enabled.json` before changing it. Replacing it with the starter file enables only Ariadne and disables your other mod selections (it does not delete the mods). To keep your selections, edit the existing JSON array and add `"Ariadne"` as another entry, separated by a comma, instead of replacing the file. Close tModLoader before editing so it does not overwrite your change.

The supplied starter file is exactly:

```json
[
  "Ariadne"
]
```

Prism, localization, and licenses are already inside `Ariadne.tmod`. No separate DLL, SDK, or compiler is needed. GitHub's automatic Source code ZIP/TAR downloads are not installable mods. Active screen readers such as NVDA are preferred over available built-in speech fallbacks; braille depends on the selected backend and your screen-reader setup.

**Updating an existing Ariadne installation:** Back up saves, close tModLoader, and replace `Ariadne.tmod`. Keep your existing `enabled.json` if it already includes Ariadne; you do not need to replace it on every update. To uninstall without in-game menus, close tModLoader and remove `"Ariadne"` from the enabled array, preserving valid JSON, or remove `Ariadne.tmod` from Mods. Ariadne's speech and keyboard accessibility will then stop.

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

Set `TML_INSTALL_PATH` for a nonstandard Steam location. The build stages source under the Windows Public profile to avoid embedding a personal source path. Distribute the packaged `.tmod` with the clean starter [Distribution/enabled.json](Distribution/enabled.json). Never distribute your personal enabled-mod list or a bare DLL.

Local decompiled game references are intentionally untracked and must not be published. Navigation maps are in [docs/decompilation](docs/decompilation). See the [1.0.0 release notes](docs/releases/1.0.0.md).
