# Terrarium

Terrarium is an accessibility-focused tModLoader mod for Terraria. The first development area is menu reading for blind and low-vision players.

## Current Accessibility

The Terraria title flow is replaced with a Terrarium-owned menu stack. It begins with the seven Terraria/tModLoader actions—Single Player, Multiplayer, Achievements, Workshop, Settings, Credits, and Exit—and continues into custom semantic screens rather than the stock visual menus.

Current custom screens include:

- Character and world selection, creation, appearance, inline file actions, and deletion confirmation.
- Join via IP, recent servers, host options, password entry, and live connection status.
- Achievement search, completion filters, details, progress, and reset confirmation.
- General, interface, video, audio, cursor, language, tModLoader, and keyboard-binding settings, both from the title screen and while playing.
- Installed mods, Mod Sources, Mod Packs, subscribed-world import, resource packs, and logs.
- A multi-level in-game inventory tree covering the hotbar, backpack, coins and ammo, trash, armor, accessories, vanity, dyes, equipment, loadouts, containers, shops, crafting, Guide recipes, reforging, and NPC conversations. Modded accessory slots and modded NPC chat buttons are included when tModLoader exposes them.

Platform-owned actions such as the Steam friends list, Steam Workshop web page, File Explorer folders, and entering gameplay intentionally leave the custom menu stack.

- Up and Down Arrow move through the options and wrap at either end.
- Home, End, Page Up, and Page Down move through long lists.
- Left and Right Arrow adjust choices, toggles, and sliders. On a character or world row they rotate through that file's Play, Favorite, Cloud, Seed, Rename, Delete, and contextual warning actions.
- In in-game hierarchical menus, Right or Enter opens submenus and activates buttons, while Left or Escape returns to the parent. When an adjustable setting is focused, Left and Right continue to change its value.
- Enter activates the focused option.
- Escape goes back in submenus. It has no effect at the root main menu because there is no previous screen.
- F1 opens an arrow-navigable contextual help screen describing the focused option and the controls available in the current menu. F1 or Escape closes help.
- Text fields speak edits and accept Enter or cancel with Escape.
- Focus is spoken as a semantic label, role, state, description, and position in the menu.

While the inventory is open:

- The inventory opens at level 0, a compact vertical list beginning with Inventory and Crafting, followed by contextual Interactions, Armor, Accessories, and Equipment as applicable. Settings and Save and Exit are always the final two options.
- Groups can contain submenus to any depth. For example, Armor contains Equipped Armor, Vanity Armor, and Armor Dyes at level 1, with their slots at level 2; modded accessory variants can reach level 3. Up and Down move and wrap within the current level, Right or Enter opens a submenu, and Left returns to its parent.
- Main Inventory contains its forty storage slots followed by Trash as a pseudo forty-first slot. Quick stack, Sort Inventory, and Sort Ammo are level 1 options in the Inventory group.
- Home and End jump to the first and last option at the current level, and Page Up and Page Down move by ten options.
- Letter keys jump to matching entries in alphabetical order. Repeating a letter cycles through its matches and wraps; empty item slots are skipped. This works in inventories, chests and banks, shops, recipe lists, equipment, and action lists.
- Enter performs the normal primary click. Shift+Enter performs the secondary click used for splitting stacks and other alternate actions.
- Ctrl+F toggles favorite on supported inventory items, Ctrl+R reads full details and tooltips, and F1 reads the inventory controls.
- Escape closes the inventory. Settings and Save and Exit are available at the bottom of the main tree.

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
