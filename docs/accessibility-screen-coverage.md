# Accessibility Screen Coverage

Terrarium routes Terraria interfaces through three accessibility layers. Purpose-built screens provide the richest semantics for common flows. A universal adapter supplies baseline keyboard and screen-reader access to every stock or mod-provided `UIState` that does not have a dedicated implementation. Small observers cover legacy overlays drawn directly by `Main`.

## Coverage Matrix

| Terraria surface | Accessibility path | Current behavior |
| --- | --- | --- |
| Root title menu, character/world flows, multiplayer, achievements, settings, credits | Purpose-built Terrarium screens | Semantic rows, state, file actions, validation, text editing, and contextual help |
| Installed mods, config lists, Mod Sources, Mod Packs, common Workshop actions | Purpose-built Terrarium screens | Semantic discovery and management actions |
| Workshop publishing/import, Mod Browser, resource-pack details, loader progress/errors, controls, reports, and uncommon vanilla/tModLoader screens | Universal `UIState` adapter | Discovers and operates live buttons, rows, fields, slots, toggles, and sliders; reads dynamic status |
| Inventory, equipment, loadouts, crafting, chests/banks, shops, and reforge | Purpose-built inventory tree | Semantic nested collections using Terraria's native slot and recipe actions |
| NPC conversation and services | Purpose-built focused conversation screen | Dialogue plus all vanilla service buttons and tModLoader `SetChatButtons`/click hooks; includes Nurse, Angler, Tax Collector, Tavernkeep, Stylist, Painter, pets, shops, help, happiness, and modded buttons without opening the general inventory for ordinary conversation |
| Guide crafting help | Purpose-built material and recipe screens | Selects a material from any player inventory slot, lists every matching Guide recipe with ingredients and stations, returns the material safely, and returns to the conversation screen |
| Signs and chest naming | Purpose-built sign screen and text inputs | Reads signs without opening the general inventory, speaks edits, and submits through native sign/container behavior |
| Stylist and Dresser | Purpose-built Terrarium screens | Hairstyle, clothing style, and color editing with apply/cancel restoration |
| Journey research, duplication, and powers | Flattened inventory-tree branch, purpose-built research actions and duplication list, plus the live power controls | Journey's second level contains Duplicated items, Time, Weather, Personal powers, Infection spread, and an Enemy difficulty slider without an intermediate Powers category. Enemy difficulty adjusts directly on that interface. Research is available through a focused item's Tab actions, and the stock Research and Duplication categories are omitted. Other power-category sliders are adjustable rows in their category tree rather than additional submenus |
| Bestiary and emotes | Specialized profiles in the universal `UIState` adapter | Bestiary opens directly into one continuous semantic entry list without page buttons or an extra options level. Focusing an entry populates its native information page and reads every unlocked semantic detail, including stats, tags, kill count, catch item, drops, rates, quantities, and conditions. Emotes expose every named emote without a selectable scrollbar. Enter activates or rereads; Left returns |
| Fullscreen world map | Purpose-built semantic map | Current biome/coordinates, world spawn, last death, players, bosses, town NPCs, discovered pylons, and pylon travel |
| Visible surroundings scanner | Purpose-built fixed-snapshot hierarchy | `Open Scanner` (`End` by default) lists currently lit, on-screen ores/valuables, liquid pools, NPCs, enemies, passive creatures, dropped items, containers, trees/large plants, and placed objects; activation finds a safe nearby landing, promotes NPC chat and signs into focused Terrarium screens, and preserves native pickup and right-click behavior |
| Player chat editor | Legacy overlay observer | Announces opening, edits, deletion, help, and closing while Terraria retains native submission |
| Death/respawn overlay | Legacy overlay observer | Announces death, dropped coins, countdown checkpoints, and respawn |
| Unknown future Terraria/tModLoader screens and third-party `UIState` menus | Universal `UIState` adapter | Baseline discovery from the live element tree; failures in third-party callbacks are isolated |

## Input Contract

- Up/Down, Home/End, and Page Up/Page Down navigate collections.
- Letter keys jump through controls by semantic name.
- Enter activates the primary action. Shift+Enter requests an alternate/right-click action where the live control supports one.
- Left/Right adjust sliders and choices. Hierarchical Terrarium screens also use Right to open and Left to return.
- Ctrl+R reads focused details and F1 reads contextual controls.
- Escape follows the current screen's native back/cancel behavior.

## Spatial Boundaries

The screen layer makes menu choices semantic; it cannot make arbitrary world coordinates meaningful by itself. The visible-surroundings scanner is deliberately limited to the current zoom-aware camera viewport and current `Lighting.Brightness > 0`; it is not a persistent explored-world or through-wall detector. Its immutable snapshot includes only lit evidence, so hidden portions of veins and pools do not contribute to their reported sizes. Close and scan again to refresh it. Players, projectiles, wires, a separate trap category, ordinary terrain blocks, and background walls are not included. A selected target can still become stale, and teleport safety avoids collision, dangerous tiles, lava, shimmer, and unsupported falls but does not protect against nearby enemies.

NPC housing selection still opens Terraria's native world-placement cursor after the NPC or query is chosen. Camera capture mode, tile-by-tile map imagery, wire/build placement, and other freeform world targeting remain spatial gameplay tools rather than menu collections. These should receive dedicated navigation aids if testing shows they are needed; they are not silently classified as accessible menus.

The universal adapter intentionally favors semantic UI state over rendered pixels. A newly encountered control should first be fixed by exposing a useful localized label or supported action in the adapter. A purpose-built state is appropriate when the live screen depends on visual layout, has compound actions, or needs transaction/cancel semantics that a generic click cannot communicate safely.
