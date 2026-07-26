# Ariadne

Ariadne is an accessibility-focused tModLoader mod for Terraria. It provides semantic menu reading and spatial terrain audio for blind and low-vision players.

## Current Accessibility

The Terraria title flow is replaced with a Ariadne-owned menu stack. It begins with the seven Terraria/tModLoader actions—Single Player, Multiplayer, Achievements, Workshop, Settings, Credits, and Exit—and continues into custom semantic screens rather than the stock visual menus.

Current custom screens include:

- Character and world selection, creation, appearance, inline file actions, and deletion confirmation.
- Join via IP, recent servers, host options, password entry, and live connection status.
- Achievement search, completion filters, details, progress, and reset confirmation.
- General, interface, video, audio, cursor, language, tModLoader, and keyboard-binding settings, both from the title screen and while playing.
- An in-game World and Player Status hierarchy covering world difficulty and events, moon phase, biome, health, mana, defense, armor set bonus, breath, bosses, all current buffs and debuffs, minion and sentry capacity and instances, and informational-accessory readings.
- Installed mods, Mod Sources, Mod Packs, the Mod Browser, Workshop publishing and import tools, resource packs, and logs.
- A multi-level in-game inventory tree covering the hotbar, backpack, coins and ammo, trash, armor, accessories, vanity, dyes, equipment, loadouts, containers, shops, crafting, and reforging. Focused conversation, Guide crafting-help, and sign screens expose NPC services, tModLoader chat-button hooks, material-based recipe lookup, sign editing, modded shops, Stylist and Dresser customization, NPC housing selection, Journey research and duplication, the Bestiary, emotes, and Journey powers without opening the general inventory for ordinary dialogue.
- A semantic world map listing the current biome and coordinates, world spawn, the last death marker, active players, bosses, town NPCs, and discovered pylons. Pylon entries request normal Terraria travel without requiring mouse targeting.
- Spoken legacy chat editing and death/respawn status, including edit feedback, dropped coins, and the visible respawn countdown.

Ariadne also provides continuous wall tones during unobstructed gameplay. Three independent procedural-noise voices indicate the nearest collision terrain to the left, right, and gravity-relative ceiling. Distance controls pitch and loudness, screen position supplies stereo or binaural placement and vertical pitch, rough terrain adds controlled irregularity, and the ceiling has a gentle pulse so it remains distinguishable from equal side walls. Floors, pass-through platforms, liquids, entities, background walls, and hazards are intentionally silent.

Hostile mob tones provide a separate on-screen enemy-awareness layer. Up to three enemies by default receive continuous, gently ticking procedural triangle tones, with visible multipart segments grouped into one emitter. Proximity increases loudness and tick rate, horizontal screen position supplies stereo level and timing differences, and vertical screen position controls pitch. Stable assignments and a 20-percent-nearer replacement threshold keep ordinary enemies from chattering between slots. Hostility comes from Terraria's chaseable-enemy semantics, without lighting or line-of-sight requirements, but an NPC must genuinely intersect the zoom-aware camera viewport.

The virtual world cursor plays the targeted foreground tile or liquid's native Terraria sound on every nonempty manual unlocked-cursor tile step and every changed nonempty Smart Cursor result. Ariadne reads these assets only from the player's installed Terraria content, decodes and caches their PCM in memory, and routes cached sounds through the same ILD, ITD, and vertical-pitch spatializer used by its authored audio. Common variants warm in the background; an uncommon or unsupported sound falls back immediately to Terraria's ordinary spatial playback while its cache is unavailable. Terraria's declared sound variants are selected in sequence instead of randomly repeating, and a new result replaces any unfinished cursor sound. Water and honey alternate the native strong and weak splashes, shimmer cycles its four native splash sounds, and lava uses a short, faded segment of Terraria's native lava ambience asset. Empty space—including wall- or wire-only coordinates—remains silent. Unlocked movement announces the target, raw tile X and Y, and held-item reach by default. Coordinate announcements can be disabled to restore the target, reach, and player-relative description. Smart Cursor remains concise: it speaks only the target name, never coordinates or reach, while still reporting coordinate changes and same-tile semantic state changes.

Biome announcements speak the current biome after entering the world and each stable biome transition after a short debounce. The announcement leads with the biome itself, as in "Forest Biome", rather than a prefix, so the name arrives first. Vanilla special biomes, pillars, and mod-added `ModBiome` content are included. Announcements intentionally omit time, exact position, depth, weather, and other informational-accessory readings.

A low-health heartbeat pulses once health reaches half and quickens at the 40, 30, 20, 10, and 5 percent thresholds, rising slightly in pitch and loudness as it goes. Terraria has no native heartbeat asset, so the two-part "lub-dub" cycle is synthesized; raising its pitch for a worse wound also tightens the gap between the two thumps. The remaining health percentage is spoken when health first crosses into each lower threshold, and a single confirmation is spoken once health has recovered. A two-percent margin above each boundary keeps a wound that hovers on a threshold from flickering between two rates.

Breath is reported while submerged. Entering water or honey announces the submersion, each ten percent of remaining breath is announced as it is lost, and running out is announced as drowning begins. Every report is paired with Terraria's own splash sound. Surfacing is announced once, and both transitions are debounced so bobbing at a liquid surface cannot chatter. Players whose breath does not drain, such as those wearing gills or a Neptune's Shell, hear only the submersion and surfacing announcements.

Backspace speaks one character-status entry per press and advances through the report while the player keeps pressing, restarting at the beginning once the sequence has been left alone for four seconds. The order is health, mana, defense, armor set bonus, minions and sentries, buffs and debuffs, time of day, moon phase, breath when it is not full, biome and layer, and active bosses when any are present. Time of day follows the same accessory rules as the status menu: without a watch it degrades to a rough phase such as morning, afternoon, or evening, and an equipped watch, nearby clock, or teammate sharing raises it to the hour, half hour, or exact minute that tier grants. The set bonus reports the equipped armor set's granted effect, including modded sets, and reports none when no set is complete.

The status menu follows Terraria's information-accessory rules. Time, weather, fishing power, detected treasure, rare creatures, nearby enemy count, kill count, DPS, speed, compass position, depth, and mod-added information displays appear only while their native display is active through an equipped item, nearby clock, teammate sharing, or the providing mod. Hiding an icon does not revoke information the player otherwise possesses.

Moon phase is the deliberate exception to that rule and is reported without a Sextant, both as a status row and in the Backspace report. A sighted player reads the moon straight off the night sky at no cost, so gating the phase behind an accessory would withhold information the interface never actually charged for. The Sextant's own reading still appears among the informational-accessory readings when the accessory is active.

Biome announcements are enabled by default. The low-health heartbeat is enabled at 45 percent volume with its threshold announcements enabled, and breath announcements are enabled. Cursor earcons are enabled at 35 percent volume, with exact unlocked-cursor coordinates enabled. Wall tones are enabled at 35 percent volume with a 12-tile range. Hostile mob and combat-target cues are enabled at 30 percent volume with three hostile emitters. One global interaural-time-difference switch and strength slider serves wall, hostile, combat-target, and cached native cursor audio; ITD is enabled by default with 0.65 milliseconds of maximum far-ear delay, adjustable from 0 to 1.00 milliseconds in 0.05-millisecond steps. Disabling ITD retains stereo level differences and vertical pitch. A native cursor sound that must use Terraria's fallback playback temporarily retains Terraria's ordinary spatial pan instead. These values can be changed in Ariadne Accessibility Settings through either Ariadne's accessible configuration editor or tModLoader's standard editor.

Any Terraria, tModLoader, or third-party `UIState` without a purpose-built Ariadne screen receives a universal semantic keyboard adapter. It discovers live buttons, list entries, item slots, text fields, toggles, and sliders; derives their labels from localized UI content; keeps the selected row in view; and announces changing progress or error text. This supplies baseline access to newly added screens without waiting for a dedicated implementation. See [the screen coverage matrix](docs/accessibility-screen-coverage.md) for the routing model and known spatial boundaries.

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

- The inventory opens at level 0 with Inventory and Crafting as one branch; Crafting is nested alongside the hotbar, main inventory, coins and ammo, and inventory actions. Armor, Accessories, and Equipment is one combined root branch containing those three sections. World and Player Status, Settings, and Save and Exit are always the final three options.
- Groups can contain submenus to any depth. For example, Armor contains Equipped Armor, Vanity Armor, and Armor Dyes at level 1, with their slots at level 2; modded accessory variants can reach level 3. Up and Down move and wrap within the current level, Right or Enter opens a submenu, and Left returns to its parent.
- Main Inventory contains its forty storage slots followed by Trash as a pseudo forty-first slot. Quick stack, Sort Inventory, and Sort Ammo are level 1 options in the Inventory group.
- Home and End jump to the first and last option at the current level, and Page Up and Page Down move by ten options.
- Letter keys jump to matching entries in alphabetical order. Repeating a letter cycles through its matches and wraps; empty item slots are skipped. This works in inventories, chests and banks, shops, recipe lists, equipment, and action lists.
- Enter performs the normal primary click. Shift+Enter performs the secondary click used for splitting stacks and other alternate actions.
- Ctrl+F toggles favorite on supported inventory items, Ctrl+R reads full details and tooltips, and F1 reads the inventory controls.
- Escape closes the inventory. World and Player Status, Settings, and Save and Exit are available at the bottom of the main tree.

During gameplay, Home toggles only wall tones for the current session and announces the new state without changing the saved configuration. Hostile/combat-target audio and cursor earcons are controlled exclusively by their saved client configuration. Home remains available for its existing navigation behavior whenever an inventory, chat field, map, NPC/sign editor, full-screen interface, or accessible menu is active. Saving a changed Wall tones setting clears the session override.

Both continuous gameplay audio streams stop and clear their source, delay, assignment, and queued-buffer state on pause, focus loss, death, title/world transitions, and all supported UI contexts, then resume from fresh state during normal gameplay. Cursor and combat-target one-shots are stopped at the same boundaries. The systems are entirely client-side and generate no network traffic. Wall tones and hostile tones are generated procedurally, while cursor earcons use in-memory PCM decoded from the local Terraria installation behind a separate failure boundary; no extracted game audio is packaged with Ariadne.

Ariadne-authored one-shot sounds such as procedural footsteps, the low-health heartbeat, and combat-target cues retain a 70-percent peak ceiling before their playback-specific and user-configured gains are applied. Cached native cursor earcons retain the source asset's own samples and level before the cursor-volume setting and spatial transform. Continuous wall and hostile tones retain lower per-voice headroom because several voices can mix simultaneously.

Speech and braille output use [Prism](https://github.com/ethindp/prism), with active screen readers such as NVDA preferred over built-in speech fallbacks. Ariadne currently packages Prism for 64-bit Windows clients; servers and unsupported platforms skip speech initialization safely.

## Repository Layout

- `Mods/Ariadne/` contains the tracked tModLoader mod.
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
