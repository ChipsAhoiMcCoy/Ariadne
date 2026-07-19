# Decompiled Terraria Navigation Map

## Snapshot And Role

- Local root: `Terraria Decompiled/`
- Product: standalone Terraria 1.4.5.6
- Version source: `Terraria Decompiled/Properties/AssemblyInfo.cs`
- C# implementation root: `Terraria Decompiled/Terraria/`

This tree is the standalone vanilla game reference. For behavior that runs inside this tModLoader mod, start with the tModLoader map and patched tree. The tModLoader snapshot bundles Terraria 1.4.4.9, so signatures and behavior in this newer standalone tree may not exist in the mod runtime.

## Fast Lookup

| Area | Primary paths and symbols |
| --- | --- |
| Game loop and global state | `Terraria/Main.cs`; search `Update`, `Draw`, `gameMenu`, and the relevant static field |
| Legacy title menus | `Terraria/Main.cs`; `menuMode`, `selectedMenu`, `UpdateMenu()`, and `DrawMenu(GameTime)` |
| Menu UI host | `Terraria/Main.cs`; `Main.MenuUI`; then `Terraria/UI/UserInterface.cs` and `Terraria/UI/UIState.cs` |
| UI framework | `Terraria/UI/UIElement.cs`, `UIState.cs`, `UserInterface.cs`, `CalculatedStyle.cs`, and `UIMouseEvent.cs` |
| Gamepad focus and links | `Terraria/UI/Gamepad/UILinkPointNavigator.cs`, `UILinkPoint.cs`, `GamepadMainMenuHandler.cs`, `GamepadPageID.cs`, and `GamepadPointID.cs` |
| Modern menu states | `Terraria/GameContent/UI/States/`; character select/creation, world select/creation, controls, achievements, workshop, and virtual keyboard |
| Reusable UI controls | `Terraria/GameContent/UI/Elements/`; text, panels, lists, scrollbars, search, buttons, sliders, character/world list items |
| Input state | `Terraria/GameInput/PlayerInput.cs`, `TriggersSet.cs`, `KeyConfiguration.cs`, `PlayerInputProfile.cs`, and `InputMode.cs` |
| In-game options | `Terraria/IngameOptions.cs`; settings rows, hover state, and input handling |
| Inventory and item slots | `Terraria/Main.cs` inventory draw/update paths and `Terraria/UI/ItemSlot.cs` |
| Crafting and recipes | `Terraria/Recipe.cs`, `RecipeGroup.cs`, and crafting sections in `Terraria/Main.cs` |
| Chests and storage | `Terraria/Chest.cs`, `Terraria/UI/ChestUI.cs`, and chest sections in `Terraria/Main.cs` |
| Localization | `Terraria/Localization/Language.cs`, `LanguageManager.cs`, `LocalizedText.cs`, and `Terraria/Lang.cs` |
| Sound playback and IDs | `Terraria/Audio/SoundEngine.cs`, `SoundStyle.cs`, `ActiveSound.cs`, and `Terraria/ID/SoundID.cs` |
| Player and movement | `Terraria/Player.cs`; search the specific field or action before reading the complete file |
| NPCs, items, projectiles | `Terraria/NPC.cs`, `Item.cs`, `Projectile.cs`, plus matching `Terraria/ID/*ID.cs` files |
| Tiles and world | `Terraria/Tile.cs`, `WorldGen.cs`, `Framing.cs`, and `Terraria/WorldBuilding/` |
| Networking | `Terraria/NetMessage.cs`, `MessageBuffer.cs`, and `Terraria/ID/MessageID.cs` |
| Map and minimap | `Terraria/Map/` and `Terraria/GameContent/UI/Minimap/` |
| Assets and rendering | `Terraria/GameContent/TextureAssets.cs`, `Terraria/Graphics/`, and the relevant draw method |
| Save data | `Terraria/IO/PlayerFileData.cs`, `WorldFileData.cs`, `PlayerFile.cs`, and `WorldFile.cs` |

The 1.4.5.6 `GameContent/HairstyleUnlocksHelper.cs` is useful for resolving vanilla progression IDs that the older tModLoader decompiler could not materialize: styles `123` through `132` require Martian Madness, style `133` additionally requires Moon Lord, and styles `145`, `162`, `163`, and `164` require Plantera at the Stylist. Re-check `HairID.Count` and the patched helper before using newer 1.4.5-only hairstyle IDs in a tModLoader mod.

## Menu Reading Entry Points

`Main` has two distinct menu paths. Legacy title screens are selected by `Main.menuMode` and assembled inside `DrawMenu`; activation and transitions are handled by `UpdateMenu` and draw-time selection logic. UI-state screens set `Main.menuMode` to `888` and render through `Main.MenuUI`.

For UI-state menus, inspect in this order:

1. The concrete state in `Terraria/GameContent/UI/States/`.
2. The element classes it creates in `Terraria/GameContent/UI/Elements/`.
3. `UIElement` for events, child traversal, dimensions, and hover state.
4. `UserInterface` for the active state, mouse capture, update, and draw traversal.
5. `UILinkPointNavigator` when keyboard/gamepad selection differs from mouse hover.

For legacy menus, search the exact `menuMode` value in both `UpdateMenu` and `DrawMenu`. Text is often added through `Language.GetTextValue`, `Lang.menu`, `Lang.inter`, or `Utils.DrawBorderString`; the selected row is commonly tracked by `selectedMenu`.

## Search Recipes

```powershell
# Find a declaration and its call sites.
rg -n "class UserInterface|SetState\(|CurrentState" "Terraria Decompiled\Terraria" -g "*.cs"

# Trace a legacy menu mode through update and draw logic.
rg -n "menuMode == 12|menuMode = 12|case 12" "Terraria Decompiled\Terraria\Main.cs"

# Find localized labels used by a screen or element.
rg -n "Language.GetText|Lang\.menu|Lang\.inter" "Terraria Decompiled\Terraria\GameContent\UI" -g "*.cs"

# Find input and gamepad focus behavior for an action.
rg -n "ActionName|CurrentPoint|ChangePoint|SetPosition" "Terraria Decompiled\Terraria\GameInput" "Terraria Decompiled\Terraria\UI\Gamepad" -g "*.cs"

# Locate a type when only part of its name is known.
rg --files "Terraria Decompiled\Terraria" -g "*.cs" | rg "Character|World|Menu|Input"
```

## Decompiler Caveats

Generated names, flattened control flow, and `Unknown result type` comments may be decompiler artifacts. Prefer public type/method signatures and corroborate behavior through call sites. Do not copy this source into the mod; use it to identify supported tModLoader APIs or the smallest necessary hook.
