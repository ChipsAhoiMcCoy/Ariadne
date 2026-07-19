# Terrarium

Terrarium is an accessibility-focused tModLoader mod for Terraria. The first development area is menu reading for blind and low-vision players.

## Current Accessibility

The Terraria title flow is replaced with a Terrarium-owned menu stack. It begins with the seven Terraria/tModLoader actions—Single Player, Multiplayer, Achievements, Workshop, Settings, Credits, and Exit—and continues into custom semantic screens rather than the stock visual menus.

Current custom screens include:

- Character and world selection, creation, appearance, inline file actions, and deletion confirmation.
- Join via IP, recent servers, host options, password entry, and live connection status.
- Achievement search, completion filters, details, progress, and reset confirmation.
- General, interface, video, audio, cursor, language, tModLoader, and keyboard-binding settings.
- Installed mods, Mod Sources, Mod Packs, subscribed-world import, resource packs, and logs.

Platform-owned actions such as the Steam friends list, Steam Workshop web page, File Explorer folders, and entering gameplay intentionally leave the custom menu stack.

- Up and Down Arrow move through the options and wrap at either end.
- Home, End, Page Up, and Page Down move through long lists.
- Left and Right Arrow adjust choices, toggles, and sliders. On a character or world row they rotate through that file's Play, Favorite, Cloud, Seed, Rename, Delete, and contextual warning actions.
- Enter activates the focused option.
- Escape goes back in submenus. It has no effect at the root main menu because there is no previous screen.
- F1 opens an arrow-navigable contextual help screen describing the focused option and the controls available in the current menu. F1 or Escape closes help.
- Text fields speak edits and accept Enter or cancel with Escape.
- Focus is spoken as a semantic label, role, state, description, and position in the menu.

Speech and braille output use [Prism](https://github.com/ethindp/prism), with active screen readers such as NVDA preferred over built-in speech fallbacks. Terrarium currently packages Prism for 64-bit Windows clients; servers and unsupported platforms skip speech initialization safely.

## Repository Layout

- `Mods/Terrarium/` contains the tracked tModLoader mod.
- `Tools/build.ps1` compiles or packages the mod using the local tModLoader installation.
- `docs/decompilation/` contains tracked navigation maps for local decompiled references.
- `Terraria Decompiled/` and `TModLoader Decompiled/` are local, read-only references excluded from Git.

## Build

Install tModLoader through Steam, then compile and package the mod with:

```powershell
.\Tools\build.ps1
```

Set `TML_INSTALL_PATH` if tModLoader is not in its standard Steam location.

## Local References

The decompiled source trees are intentionally untracked and must not be published. Start source investigations with [the tModLoader map](docs/decompilation/decompiled-tmodloader-map.md) or [the Terraria map](docs/decompilation/decompiled-terraria-map.md), then narrow searches with `rg`.
