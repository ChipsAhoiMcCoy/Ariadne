# Terrarium

Terrarium is an accessibility-focused tModLoader mod for Terraria. It provides semantic menu reading and spatial terrain audio for blind and low-vision players.

## Current Accessibility

The Terraria title flow is replaced with a Terrarium-owned menu stack. It begins with the seven Terraria/tModLoader actions—Single Player, Multiplayer, Achievements, Workshop, Settings, Credits, and Exit—and continues into custom semantic screens rather than the stock visual menus.

Current custom screens include:

- Character and world selection, creation, appearance, inline file actions, and deletion confirmation.
- Join via IP, recent servers, host options, password entry, and live connection status.
- Achievement search, completion filters, details, progress, and reset confirmation.
- General, interface, video, audio, cursor, language, tModLoader, and keyboard-binding settings, both from the title screen and while playing.
- Installed mods, Mod Sources, Mod Packs, the Mod Browser, Workshop publishing and import tools, resource packs, and logs.
- A multi-level in-game inventory tree covering the hotbar, backpack, coins and ammo, trash, armor, accessories, vanity, dyes, equipment, loadouts, containers, shops, crafting, and reforging. Focused conversation, Guide crafting-help, and sign screens expose NPC services, tModLoader chat-button hooks, material-based recipe lookup, sign editing, modded shops, Stylist and Dresser customization, NPC housing selection, Journey research and duplication, the Bestiary, emotes, and Journey powers without opening the general inventory for ordinary dialogue.
- A semantic world map listing the current biome and coordinates, world spawn, the last death marker, active players, bosses, town NPCs, and discovered pylons. Pylon entries request normal Terraria travel without requiring mouse targeting.
- Spoken legacy chat editing and death/respawn status, including edit feedback, dropped coins, and the visible respawn countdown.

Terrarium also provides continuous wall tones during unobstructed gameplay. Three independent procedural-noise voices indicate the nearest collision terrain to the left, right, and gravity-relative ceiling. Distance controls pitch and loudness, screen position supplies stereo or binaural placement and vertical pitch, rough terrain adds controlled irregularity, and the ceiling has a gentle pulse so it remains distinguishable from equal side walls. Floors, pass-through platforms, liquids, entities, background walls, and hazards are intentionally silent.

Hostile mob tones provide a separate on-screen enemy-awareness layer. Short band-limited square-wave pulses identify up to three enemies by default, grouping visible multipart segments into one emitter. Visible bosses have priority, followed by distance from the player; stable assignments and a 20-percent-nearer replacement threshold keep ordinary enemies from chattering between slots. A single regular pulse opportunity occurs about every 0.18 seconds near the player, slowing smoothly to about 0.55 seconds at the camera edge; multiple enemies rotate through those opportunities instead of producing clustered pulse pairs. The pulse also grows louder on approach through the same smooth distance-gain curve used by wall tones. Horizontal stereo or binaural placement is centered on the player and gently expands offsets near the middle for clearer side discrimination while retaining full left/right at the screen edges. Vertical viewport position makes the top of the screen one octave higher than the bottom. Hostility comes from Terraria's chaseable-enemy semantics, without lighting or line-of-sight requirements, but an NPC must genuinely intersect the zoom-aware camera viewport.

Wall tones are enabled by default at 35 percent volume with a 12-tile range. Hostile mob tones are enabled by default at 30 percent volume with three emitters. Both use binaural spatialization and 0.65 milliseconds of maximum far-ear delay by default; each has an independent ITD slider from 0 to 1.00 milliseconds in 0.05-millisecond steps and can independently use Stereo pan without delay. These values can be changed in Terrarium Audio Settings through either Terrarium's accessible configuration editor or tModLoader's standard editor.

Any Terraria, tModLoader, or third-party `UIState` without a purpose-built Terrarium screen receives a universal semantic keyboard adapter. It discovers live buttons, list entries, item slots, text fields, toggles, and sliders; derives their labels from localized UI content; keeps the selected row in view; and announces changing progress or error text. This supplies baseline access to newly added screens without waiting for a dedicated implementation. See [the screen coverage matrix](docs/accessibility-screen-coverage.md) for the routing model and known spatial boundaries.

Platform-owned actions such as the Steam friends list, Steam Workshop web page, File Explorer folders, and entering gameplay intentionally leave the custom menu stack.

- Up and Down Arrow move through the options and wrap at either end.
- Home, End, Page Up, and Page Down move through long lists.
- Letter keys jump to the next matching option by name in custom and fallback screens.
- Left and Right Arrow adjust choices, toggles, and sliders. On a character or world row they rotate through that file's Play, Favorite, Cloud, Seed, Rename, Delete, and contextual warning actions.
- In in-game hierarchical menus, Right or Enter opens submenus and activates buttons, while Left or Escape returns to the parent. When an adjustable setting is focused, Left and Right continue to change its value.
- Enter activates the focused option.
- Escape goes back in submenus. It has no effect at the root main menu because there is no previous screen.
- F1 opens an arrow-navigable contextual help screen describing the focused option and the controls available in the current menu. F1 or Escape closes help.
- Text fields speak edits and accept Enter or cancel with Escape.
- Focus is spoken as a semantic label, role, state, description, and position in the menu.
- Stock fallback screens use Enter for a normal click, Shift+Enter for a right click or alternate action, and Ctrl+R to read all discovered text for the focused control.

While the inventory is open:

- The inventory opens at level 0, a compact vertical list beginning with Inventory and Crafting, followed by contextual Interactions, Armor, Accessories, and Equipment as applicable. Settings and Save and Exit are always the final two options.
- Groups can contain submenus to any depth. For example, Armor contains Equipped Armor, Vanity Armor, and Armor Dyes at level 1, with their slots at level 2; modded accessory variants can reach level 3. Up and Down move and wrap within the current level, Right or Enter opens a submenu, and Left returns to its parent.
- Main Inventory contains its forty storage slots followed by Trash as a pseudo forty-first slot. Quick stack, Sort Inventory, and Sort Ammo are level 1 options in the Inventory group.
- Home and End jump to the first and last option at the current level, and Page Up and Page Down move by ten options.
- Letter keys jump to matching entries in alphabetical order. Repeating a letter cycles through its matches and wraps; empty item slots are skipped. This works in inventories, chests and banks, shops, recipe lists, equipment, and action lists.
- Enter performs the normal primary click. Shift+Enter performs the secondary click used for splitting stacks and other alternate actions.
- Ctrl+F toggles favorite on supported inventory items, Ctrl+R reads full details and tooltips, and F1 reads the inventory controls.
- Escape closes the inventory. Settings and Save and Exit are available at the bottom of the main tree.

During gameplay, Home toggles only wall tones for the current session and announces the new state without changing the saved configuration. Hostile mob tones are controlled exclusively by their saved client configuration. Home remains available for its existing navigation behavior whenever an inventory, chat field, map, NPC/sign editor, full-screen interface, or accessible menu is active. Saving a changed Wall tones setting clears the session override.

Both gameplay audio streams stop and clear their source, delay, scheduling, assignment, and queued-buffer state on pause, focus loss, death, title/world transitions, and all supported UI contexts, then resume from fresh state during normal gameplay. The systems are entirely client-side and generate no network traffic. Wall tones remain procedural and package no looping audio asset. Hostile tones decode the packaged mono PCM `Assets/Audio/HostileMobPulse.wav` into their dedicated dynamic stereo stream; a missing or malformed pulse, or a hostile-stream audio failure, disables only hostile tones and writes one warning.

Terrarium-authored one-shot sources, including hostile pulses and procedural footsteps, share a 70-percent peak ceiling before their playback-specific and user-configured gains are applied. Hostile waveform revisions also retain a common RMS-energy target so changing timbre does not silently increase perceived volume. Continuous wall tones retain lower per-voice headroom because as many as three voices mix simultaneously.

Speech and braille output use [Prism](https://github.com/ethindp/prism), with active screen readers such as NVDA preferred over built-in speech fallbacks. Terrarium currently packages Prism for 64-bit Windows clients; servers and unsupported platforms skip speech initialization safely.

## Repository Layout

- `Mods/Terrarium/` contains the tracked tModLoader mod.
- `Tools/build.ps1` compiles or packages the mod using the local tModLoader installation.
- `Tools/generate-hostile-mob-pulse.ps1` deterministically regenerates the packaged hostile-mob WAV.
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
