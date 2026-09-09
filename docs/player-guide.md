# Ariadne player guide

This guide describes Ariadne 1.0.0. Keys are defaults for a US keyboard; punctuation positions and existing profiles can differ. Change gameplay bindings in **Settings → Controls**. Live **Alt+H** gameplay help reports current bindings. Menu keys and Alt+H are fixed, independent of the rebindable Ariadne Modifier.

## Contents

- [Installation](#installation)
- [Terraria movement and basic controls](#terraria-movement-and-basic-controls)
- [Ariadne gameplay controls](#ariadne-gameplay-controls)
- [Menus and text entry](#menus-and-text-entry)
- [Inventory, crafting, and services](#inventory-crafting-and-services)
- [Aiming, building, and combat](#aiming-building-and-combat)
- [Scanner and radar](#scanner-and-radar)
- [Waypoints, map, housing, and freecam](#waypoints-map-housing-and-freecam)
- [Learning the sounds](#learning-the-sounds)
- [Status and chat](#status-and-chat)
- [Settings, saves, and multiplayer](#settings-saves-and-multiplayer)
- [Troubleshooting](#troubleshooting)

## Installation

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

References: [official tModLoader usage guide](https://github.com/tModLoader/tModLoader/wiki/Basic-tModLoader-Usage-Guide) and [Prism 0.17.3 platform requirements](https://github.com/ethindp/prism/tree/v0.17.3).

The 1.0.0 package was built with **tModLoader 2026.7.3.0 (Terraria 1.4.4 line)**. Other branches/versions have not been verified.

## Terraria movement and basic controls

Ariadne retains ordinary movement during gameplay. These are standard keyboard defaults. Equipment-dependent actions require the relevant item.

| Key | Action |
| --- | --- |
| A / D | Walk left / right. |
| W / S | Climb up / down ropes. S also drops through platforms. W is Up, not Jump. |
| Space | Jump; hold as appropriate for swimming, wings, or equipped movement abilities. |
| E | Fire an equipped grappling hook toward your aim. |
| R | Mount/dismount using the equipped mount. |
| 1–9, 0 | Select hotbar slots 1–10. |
| Mouse wheel | Cycle hotbar slots. |
| Left / Right mouse | Primary / secondary use; Ariadne also provides I / P. |
| Left Control | Smart Cursor, using the configured toggle/hold behavior. |
| Left Shift | Smart Select: temporarily select a suitable tool. |
| H / J / B | Quick heal / quick mana / quick buff with available items. |
| Escape | Open the accessible inventory; close it when open. |
| M | Open the semantic world map through Terraria's fullscreen-map binding. |
| Enter | Open chat; send the current message when editing. |
| F1 / F2 / F3 | Equipment loadouts 1 / 2 / 3. Ariadne help is Alt+H. |

Tab belongs to Ariadne's combat-target cycle during gameplay, replacing the usual map-style action there. Menu navigation and typing operate the active interface rather than your character.

## Ariadne gameplay controls

| Default key | Action |
| --- | --- |
| Alt+H | Gameplay/contextual help. Either Alt key works for help. |
| O / K / L / Semicolon (;) | Aim up / left / down / right; unlocked cursor moves tile by tile. |
| I | Use the held item; hold for supported repeated use. |
| P | Secondary use or interact at the cursor. |
| Tab | Lock nearest eligible enemy, then cycle outward; after the farthest, release and recenter. No Alt needed. |
| Backspace | Next character-status entry; restart after four idle seconds. |
| End | Visible-surroundings scanner. |
| Apostrophe (') | Speak/ping one radar contact per press; fresh snapshot after four idle seconds. |
| Left Alt + Apostrophe | Toggle passive radar for this session. |
| Home | Toggle wall/ceiling tones for this session. |
| Left Alt + Q / E | Previous / next hotbar slot, including empty slots, wrapping. |
| Left Alt + W | Open this world's waypoints. |
| U | Open Housing Query; refresh the snapshot while active. |
| Hold Right Shift + W/A/S/D | Move freecam; release Right Shift to return to your body. |

Left Alt is the default **Ariadne Modifier**. Rebind it and chord components in Controls. Ariadne suppresses the underlying native action while owning a chord, so Left Alt+E does not also fire Grapple. Home/End retain list-navigation meanings in interfaces.

## Menus and text entry

| Key | Behavior |
| --- | --- |
| Up / Down | Previous / next entry, wrapping. |
| Enter | Activate or open submenu. |
| Right | Open submenu where supported, or adjust focused setting. |
| Left | Return to parent where supported, or adjust focused setting. |
| Home / End | First / last entry. |
| Page Up / Page Down | Move through long lists. |
| Letter keys | Jump by name; repeat to cycle matches. In text fields, type normally. |
| Escape | Return, close, or cancel. Root title menu has no previous screen. Inventory Escape closes inventory. |
| Alt+H | Context help; Alt+H or Escape closes a help screen. |
| Enter / Escape in text field | Accept / cancel; edits are spoken. |
| Shift+Enter in fallback UI | Alternate/right click. |
| Ctrl+R in fallback UI | Read discovered text for the focused control. |

On character/world rows, Left/Right select available Play, Favorite, Cloud, Seed, Rename, Delete, or warning actions; Enter executes. Deletion is confirmed. Character/world creation and appearance offer descriptive settings and text fields.

The main menu covers Single Player, Multiplayer, Achievements, Workshop, Settings, Credits, and Exit. Multiplayer provides IP entry, recent servers, host options, passwords, and connection status. Achievements provide search, filters, details, and reset confirmation. Workshop includes mods, sources, packs, browser, publishing/import, resource packs, and logs. Steam friends, browser pages, and Explorer folders leave Ariadne's menus.

## Inventory, crafting, and services

Escape opens the tree. **Inventory and Crafting** contains hotbar, forty main storage slots, coins/ammo, crafting, and inventory actions. **Armor, Accessories, and Equipment** contains equipment, vanity, dyes, and loadouts. World and Player Status, Settings, and Save and Exit are the last three root entries. Containers, shops, and contextual services appear as appropriate.

| Key | Inventory action |
| --- | --- |
| Up / Down | Move within current group/column. |
| Right / Enter on group | Enter submenu. |
| Left | Previous column, or parent from the first column. |
| Right in multi-column item pane | Next column. |
| Enter on item | Primary click: pick up/place or native slot action. |
| Shift+Enter | Secondary click, including stack splitting/alternate actions. |
| Tab | Open/close actions for the focused item. |
| Ctrl+Tab / Ctrl+Shift+Tab | Next / previous sibling pane, restoring its last focus. |
| Ctrl+F | Toggle favorite on supported items. |
| Ctrl+R | Full details and tooltips. |
| I in Crafting | Craft focused recipe; hold to repeat. Equipment crafts once per press. |
| Letter keys | Find names; skip empty slots. |
| Home / End | List edges. |
| Page Up / Page Down | Ten-entry jumps. |
| Alt+H / Escape | Help / close inventory. |

Press **Tab on an item** for available Open, Use/Consume, Equip/Unequip, Favorite, Research, Take one, Move to hotbar/inventory, Store/Take from an open container, Drop, or Trash/Sell actions. Availability depends on the item and context. Up/Down selects an action, Enter performs it, Ctrl+R reads details, and Tab returns. Journey Research consumes the needed items; completed research unlocks duplication. Ctrl+Tab moves between sibling panes such as hotbar, inventory, coins, ammo, and crafting without climbing back through the tree.

One column is the default. Inventory, hotbar, crafting, storage, and shops each support 1–10 columns in [settings](settings.md), filled top to bottom in balanced columns. Trash follows slot 40 in the final main-inventory column. Container slot counts exclude action buttons. Quick stack, Sort Inventory, and Sort Ammo appear under inventory.

To craft: approach the required station, open Inventory and Crafting → Crafting, select/read a recipe, and press I. Results enter inventory. Holding I stops when ingredients run out and never starts the next recipe. Overflow can remain in the held slot; crafting stops until it can be stored.

Aim at an NPC and press P for conversation/services, or activate its scanner result. Shops, reforging, Guide material-based crafting help, Stylist/Dresser customization, and sign editing have focused screens; follow their actions and Alt+H. Inventory access also covers housing assignment, Bestiary, emotes, Journey research/duplication, and Journey powers. Journey features require Journey mode; enemy difficulty adjusts by 0.05x from 0.50x to 3.00x.

## Aiming, building, and combat

Select the intended tool/item, aim with O/K/L/;, then use I to mine, place, attack, or use it. Unlocked steps describe target, raw tile coordinates, and held-item reach by default, including entities under the exact cursor point. An out-of-reach result requires moving your player closer; moving the cursor does not extend item reach.

Nonempty foreground/liquid steps play native Terraria sounds. Empty space and wall/wire-only locations are silent. Coordinate speech can be disabled for shorter relative descriptions. Smart Cursor retains Terraria's targeting and toggle/hold rules and speaks concise target names. For P interaction, an interactable entity directly under an unlocked cursor has priority, then the exact tile/object. Enemies are descriptive and do not steal tile interaction. Locked objects still require their keys, such as a Golden Key for an Old Shaking Chest.

Tab cycles eligible combat targets nearest outward and finally releases. Announcements include name, health, direction, distance, and protection state. Manual aim cancels the lock. Temporarily unreachable held targets can be reacquired automatically on return. Locking aims for you; press I to attack, subject to your weapon's normal rules.

Protected boss parts and shielded Celestial Pillars remain meaningful targets before they can be hurt; damageable targets are preferred. Rising/falling cues report vulnerability changes. Destroyed Moon Lord shells and permanently invulnerable True Eyes are excluded. World Status lists active pillars and shields globally.

## Scanner and radar

End opens a **fixed visible-surroundings snapshot**, grouped into nonempty categories such as resources, containers, creatures/NPCs, enemies, dropped items, liquids, plants, and placed objects. It uses lit targets in the visible area, not a complete underground/world-wide search.

Right/Enter opens a category, arrows browse, and Ctrl+R reads details. Results give direction from your current position and end with fixed raw tile coordinates. Close and reopen to refresh.

**Enter on a target teleports your player** to a searched safe landing and performs its supported native interaction. If no landing is available, it reports the problem and stays open. Resources still need the appropriate tool/use to harvest. Server rejection/correction of travel is announced.

Passive radar sounds for newly discovered contacts rather than repeating while you stand still. Default range is 30 tiles, with ores/valuables, containers, and creatures enabled.

| Bell strikes | Category |
| --- | --- |
| 1 | Spelunker-style ores/valuables, gems, pots, life crystals. |
| 2 | Containers. |
| 3 | Town/rescuable NPCs and passive creatures. |
| 4 | Other enabled categories, such as dropped items or enemies. |

Apostrophe captures a nearest-first list and reads/pings one contact per press, wrapping at the end. Four idle seconds refresh it. Manual checks always include dropped items and stacks, even with passive item discovery disabled; pickups/changed stacks refresh the list. Left Alt+Apostrophe toggles passive radar for the session. Settings control range, categories, volume, and manual contact speech.

## Waypoints, map, housing, and freecam

**Waypoints:** Left Alt+W opens saved locations for this world. Choose Add waypoint here, type a name or accept the suggestion with Enter. Enter on a saved waypoint teleports there or searches nearby footing if terrain changed. Tab opens its travel, rename, delete, and coordinate actions; Tab returns to the list. Delete requires Enter twice; moving away cancels confirmation. Waypoints are local and shared by characters on this installation, not automatically with other players.

**Map:** M opens a semantic list including biome/position, world spawn, last death, active players, bosses, town NPCs, and discovered pylons. Pylon actions request normal Terraria travel and retain its requirements. The map lists meaningful locations rather than reading every tile.

**Housing:** U captures enclosed rooms intersecting the viewport, including occupied/unsuitable rooms regardless of lighting. Focus starts nearest the player. Arrows move between room centers, Enter runs a live housing query, U refreshes, and Escape exits. Spatial sounds distinguish suitable, occupied, and unsuitable. The inventory Housing query tool opens this mode; NPC assignment remains a separate list with native availability rules.

**Freecam:** Hold Right Shift and use W/A/S/D (or rebound movement keys) to explore with the camera. Collision limits and a 60-tile radius from the live player apply, with contact feedback. A periodic spatial body beacon points back to your live player. Release Right Shift to return. Camera movement does not teleport your body or grant invulnerability; the world can keep running. Menus, death, and context changes can end freecam.

## Learning the sounds

Open **Ariadne Sound Guide** directly from the main menu, or **Settings → Ariadne Sound Guide** in-game. Focusing a cue plays the named variant. Enter advances to and plays the next variant; repeat to cycle. Left/Right select and speak a variant without playing it; move away and back to preview that selection. Auditions use your configured volumes and Terraria's Sound volume.

| Cue | Meaning |
| --- | --- |
| Continuous side/ceiling noise | Movement-blocking terrain. Side walls and gravity-relative ceiling have distinct filters; traversable steps and pass-through platform ceilings are excluded. |
| Movement bump | Attempted movement is blocked. |
| Footsteps | Ground movement feedback. |
| Centered rising/falling climbing ticks | Vertical tile boundaries crossed while attached to rope-like terrain, not ordinary jumps/falls. |
| Reassuring / warning ledge cue | Known safe landing / unsafe or unknown landing ahead. |
| Platform, descending track tick, rope cue | Traversal landmarks crossed on the ground; speech names new runs/types and rope direction. |
| Continuous enemy tone | One enemy: held target, otherwise nearest eligible on-screen enemy. Held-target tone is a perfect fifth higher. |
| Target acquisition/loss and rising/falling protection cues | Combat lock or vulnerability changes. |
| Radar bell | New contact; strike count identifies category. |
| Native tile/liquid sound | Cursor changed to a nonempty foreground/liquid target. |
| Heartbeat | Health at/below half; faster rates at 40%, 30%, 20%, 10%, and 5%. |
| Splash with speech | Submersion, breath loss, drowning, or surfacing. |
| Freecam beacon | Direction back to your live body. |
| Housing result sound | Suitable, occupied, or unsuitable focused room. |

Stereo position indicates left/right; pitch conveys height. Supported cues change loudness with distance by default. Stereo headphones help distinguish these cues. Ledge detection begins at three-tile drops, looks four reachable tiles ahead, and searches thirty tiles downward by default. Lava, shimmer, hurting tiles, damaging falls, and unknown bottoms are unsafe. A reassuring cue cannot guarantee that terrain or enemies will remain safe afterward.

Gameplay audio pauses/resets in supported interfaces, on pause, focus loss, death, and world transitions. Silence in a menu is expected. Home affects wall tones only; enemy/target and cursor sounds use saved settings.

## Status and chat

Backspace reads one entry per press: health, mana, defense, armor set bonus, summons/capacity, buffs/debuffs, time, moon phase, reduced breath when applicable, biome/layer, and active bosses when present. After four idle seconds it restarts. **World and Player Status** in inventory provides a browsable hierarchy plus events, pillars, and informational accessories.

Time precision, weather, fishing power, treasure/rare-creature detection, nearby enemy counts, kill count, DPS, speed, compass, depth, and mod information displays follow Terraria's accessory, clock, and teammate-sharing rules. Without a watch, time is approximate. Moon phase is deliberately available without a Sextant. Hiding an information icon does not revoke information you possess.

Automatic reports include stable biome transitions, hotbar changes, pickups, new capacity-bearing summons, low-health thresholds/recovery, breath loss in 10% steps, Sonar Potion catches, and death/respawn information. Initial summon synchronization and removals do not trigger new-summon reports. Characters whose breath does not drain still receive submersion/surface reports.

| Key in chat | Action |
| --- | --- |
| Type | Edit with spoken feedback. |
| Up | Read newest message, then progressively older messages. |
| Down | Move toward newer messages, then back to the edit field. |
| Enter | Send current message. |
| Escape | Close without sending. |
| Alt+H | Chat help. |

Incoming messages are spoken even with chat closed. Signs and other text fields have their own accept/cancel actions; follow their instructions.

## Settings, saves, and multiplayer

In-game, use **Settings → Mod Configuration → Ariadne: Ariadne Accessibility Settings**. At the title screen, use **Workshop → Manage Mods → Mod Configuration**, or the Ariadne row's **Config** action. Use **Save changes** to apply pending edits; **Revert unsaved changes** restores saved values. **Restore defaults** changes pending values and still requires Save changes. Leaving with pending edits asks whether to discard them. The [complete settings reference](settings.md) lists every saved option, default, and range. Volumes scale Terraria's Sound volume; zero Sound mutes cues regardless of Ariadne sliders.

Back up the active tModLoader save directory, especially Players, Worlds, ModConfigs, and **Ariadne/Waypoints**. Waypoints live at `Ariadne/Waypoints/<world identity>.json`, outside the world file. Copying a world alone does not carry waypoints. Prism extracts its runtime under the save directory automatically.

Ariadne is **NoSync**: the server and other clients need not install it for your accessibility features. Other gameplay mods retain their own requirements. Scanner/waypoint travel sends normal Terraria teleport messages; servers may reject/correct it. Multiplayer simulation can continue while browsing menus, so choose a safe place. Audio and speech processing are local.

## Troubleshooting

| Problem | Check |
| --- | --- |
| Mod absent or not loading | Verify both Ariadne.tmod and enabled.json are in the active Mods folder, the JSON contains "Ariadne", and its filename is not enabled.json.txt. Launch tModLoader, not Terraria. |
| No speech | Check Windows x64, active screen reader/working fallback, and completed reload. Transient initialization failures retry with a recovery announcement. |
| No cues | Check Terraria Sound, feature volume/toggle, Home/radar session state, game focus, and open interfaces. Try the Sound Guide. |
| Different keys | Check Controls and Alt+H; existing profiles/layouts and overlapping mod bindings can differ. |
| Crafting stops | Check ingredients, station, space, and held item. Equipment crafts once per press. |
| Scanner misses something | Add light, approach, close/reopen. Snapshot is fixed and visible-area limited; radar adds range/categories. |
| Travel fails | Listen for missing landing, stale target, or server correction; re-scan or choose another location. |
| Speech stops on reload | Expected while unloaded; it returns only if Ariadne remains enabled and reload succeeds. |
| Another mod's UI has poor labels | Generic fallback is best effort; custom-drawn controls may lack semantic information. Report the screen and mod. |
| External window uses different keys | Steam, Explorer, and browsers own their input/accessibility. |

This release packages English localization and Windows x64 speech. Generic UI, mod content, screen-reader backends, and servers can vary. See the [coverage matrix](accessibility-screen-coverage.md). If tModLoader reports incompatibility, record both versions rather than assuming preview/stable branches are interchangeable.

Report problems through [Ariadne issues](https://github.com/ChipsAhoiMcCoy/Ariadne/issues): include mod/tModLoader versions, screen reader, relevant other mods, exact steps, and expected/actual results. The installation's `tModLoader-Logs/client.log` contains Prism initialization and mod errors; accessible Logs tools can help locate logs. Review personal paths and server addresses before sharing.

See [credits, full license texts, and Prism source access](../Mods/Ariadne/ThirdParty/README.md).
