# Decompiled tModLoader Navigation Map

## Snapshot And Role

- Local root: `TModLoader Decompiled/`
- Product: tModLoader 2026.04.3.0 stable, commit `abca3f47`
- Bundled Terraria: 1.4.4.9
- Version source: `TModLoader Decompiled/Properties/AssemblyInfo.cs`
- Patched game root: `TModLoader Decompiled/Terraria/`
- Mod API root: `TModLoader Decompiled/Terraria/ModLoader/`

Use this tree first for Terrarium implementation work. It contains tModLoader's patched copy of Terraria as well as loader APIs and custom menus. The installed tModLoader may be newer than this copied snapshot; when versions differ, confirm signatures against the installed references by compiling.

## Fast Lookup

| Area | Primary paths and symbols |
| --- | --- |
| Mod entry point | `Terraria/ModLoader/Mod.cs`, `ModType.cs`, and `ModContent.cs` |
| Cross-cutting systems | `Terraria/ModLoader/ModSystem.cs`; especially `UpdateUI`, `PostUpdateInput`, `ModifyInterfaceLayers`, and `PostDrawInterface` |
| System hook dispatch | `Terraria/ModLoader/SystemLoader.cs`; shows when and how `ModSystem` hooks run |
| Player hooks | `Terraria/ModLoader/ModPlayer.cs` and `PlayerLoader.cs` |
| Content hooks | `ModItem.cs`, `GlobalItem.cs`, `ModNPC.cs`, `GlobalNPC.cs`, `ModProjectile.cs`, and their `*Loader.cs` dispatchers |
| Runtime detours | `Terraria/ModLoader/MonoModHooks.cs`; use only when a supported `ModSystem` or loader hook cannot observe the behavior |
| Patched Terraria behavior | `Terraria/Main.cs` and the same `Terraria/UI/`, `GameInput/`, and `GameContent/` layout described in the vanilla map |
| tModLoader menu router | `Terraria/ModLoader/UI/Interface.cs`; converts tModLoader menu modes into `Main.MenuUI` states |
| tModLoader menu states | `Terraria/ModLoader/UI/`; `UIMods`, `UIModSources`, `UIModInfo`, messages, progress, and shared controls |
| Mod Browser | `Terraria/ModLoader/UI/ModBrowser/` |
| Config UI | `Terraria/ModLoader/Config/UI/`, with models and attributes in `Terraria/ModLoader/Config/` |
| Menu themes | `Terraria/ModLoader/ModMenu.cs` and `MenuLoader.cs` |
| Mod keybinds | `Terraria/ModLoader/ModKeybind.cs` and `KeybindLoader.cs`; patched consumption is in `Terraria/GameInput/PlayerInput.cs` |
| Localization | `Terraria/ModLoader/LocalizationLoader.cs`, `ILocalizedModType.cs`, `ILocalizedModTypeExtensions.cs`, and patched `Terraria/Localization/` |
| Logging and errors | `Terraria/ModLoader/Logging.cs`, `Engine/LoggingHooks.cs`, and `Engine/ErrorReporting.cs` |
| Mod loading/building | `Terraria/ModLoader/Core/`; `AssemblyManager`, `ModOrganizer`, `ModCompile`, and `LocalMod` |
| Networking | `Terraria/ModLoader/ModNet.cs`, `ModPacket.cs`, and `ModSystem` network hooks |
| Build identity | `Terraria/ModLoader/BuildInfo.cs` and `Properties/AssemblyInfo.cs` |

## Menu Mode Router

`Terraria/ModLoader/UI/Interface.cs` is the central map from `Main.menuMode` to tModLoader UI states. It sets `Main.MenuUI` and generally changes the active mode to `888` while a `UIState` is visible.

| Mode | State or purpose |
| --- | --- |
| `10000` | Installed Mods (`UIMods`) |
| `10001` | Mod Sources (`UIModSources`) |
| `10002` | Load/reload mods (`UILoadMods`) |
| `10003` | Build mod (`UIBuildMod`) |
| `10005` | Error message (`UIErrorMessage`) |
| `10007` | Mod Browser (`UIModBrowser`) |
| `10008` | Mod info (`UIModInfo`) |
| `10013` | General info message (`UIInfoMessage`) |
| `10016` | Mod Packs (`UIModPacks`) |
| `10019` | Extract mod (`UIExtractMod`) |
| `10020` | Download progress (`UIDownloadProgress`) |
| `10023` | General progress (`UIProgress`) |
| `10024` | Mod configuration (`UIModConfig`) |
| `10025` | Create mod (`UICreateMod`) |
| `10027` | Mod configuration list (`UIModConfigList`) |
| `10028` | Server mod mismatch message (`UIServerModsDifferMessage`) |
| `888` | An active `Main.MenuUI` state |

Search the numeric mode in `Interface.cs` and the concrete state because transitions back to other screens are often implemented by assigning the next mode.

## Menu Reading Entry Points

For a general observer, use `ModSystem.PostUpdateInput()` when title-menu coverage is required. `SystemLoader.UpdateUI` deliberately dispatches `ModSystem.UpdateUI(GameTime)` only while `Main.gameMenu` is false, whereas `SystemLoader.PostUpdateInput` has no title-screen exclusion. Inspect `Main.gameMenu`, `Main.menuMode`, `Main.MenuUI.CurrentState`, and input-mode state, but avoid announcing every update. Track the semantic selection and speak only when it changes or when the selected item's state changes.

For tModLoader screens, the concrete `UIState` owns its controls. Many list entries are custom types such as `UIModItem`, `UIModSourceItem`, and Mod Browser item types, so a single hard-coded element shape will not cover every menu. Reusable primitives are in `Terraria/ModLoader/UI/` and `UI/Elements/`, while base event and tree behavior remains under `Terraria/UI/UIElement.cs`.

Gamepad focus is still driven by `Terraria/UI/Gamepad/UILinkPointNavigator.cs`. tModLoader UI states commonly assign `UILinkPointNavigator.Shortcuts.BackButtonCommand` and `BackButtonGoto`; search those assignments to understand controller navigation and back behavior.

### Legacy Main Menu (Mode 0)

`Main.DrawMenu` builds seven mode-0 entries in this order: Single Player, Multiplayer, Achievements, Workshop, Settings, Credits, and Exit. Their activation paths are respectively menu modes `1`, `12`, `888` with `Main.AchievementsMenu`, `888` with a `UIWorkshopHub`, `11`, `3000` plus the credits sky, and `Main.GameAskedToQuit = true`. Single Player also calls public `Main.ClearPendingPlayerSelectCallbacks()` and private tModLoader helper `PrepareLoadedModsAndConfigsForSingleplayer()`, which checks pending mod/config changes before character selection; custom activation must preserve that behavior.

The tModLoader call to `Interface.AddMenuButtons` occurs between Workshop and Settings, but the method is empty in both the copied 2026.04.3 snapshot and installed 2026.05.3 source. Do not assume that remains empty in later loader versions; re-check it when updating the menu model.

### Character, World, And Multiplayer Flows

- `GameContent/UI/States/UICharacterSelect.cs` loads `Main.PlayerList` through public `Main.LoadPlayers()`. `GameContent/UI/Elements/UICharacterListItem.cs` activates a selection through public `Main.SelectPlayer(PlayerFileData)`; file data exposes localized-name inputs, favorite, rename, cloud/local move, and player metadata.
- Character-creation hair is rebuilt by `Main.Hairstyles.UpdateUnlocks()` before `UICharacterCreation.MakeHairsylesMenu` reads `AvailableHairstyles`. The unlock catalog is in `GameContent/HairstyleUnlocksHelper.cs`; modded hair count, character-creation availability, and Stylist conditions are exposed by `ModLoader/HairLoader.cs` and `ModHair.cs`. `HairID.Count` is the boundary between vanilla and modded styles.
- `GameContent/UI/States/UIWorldSelect.cs` loads `Main.WorldList` through public `Main.LoadWorlds()`. World compatibility combines Journey-mode parity with `SystemLoader.CanWorldBePlayed`. `UIWorldListItem` sets a `WorldFileData` active and either calls `WorldGen.playWorld()` or advances to host configuration. Its standard row actions are Play, Favorite, Cloud/Local (only when `SocialAPI.Cloud` exists), Copy Seed (only for a valid generator version), Rename, and Delete; tModLoader adds contextual mod-list/mod-pack mismatch and mod-save-error buttons. Cloud moves recalculate and check Steam quota before calling `MoveToCloud`.
- Character creation saves with public `PlayerFileData.CreateAndSave`, but starter stats/inventory are initialized by private `UICharacterCreation.SetupPlayerStatsAndInventoryBasedOnDifficulty`. World creation uses public `WorldFile.CreateMetadata`, `WorldFileData.SetSeed`, `UIWorldCreation.ProcessSpecialWorldSeeds`, and `WorldGen.CreateNewWorld`.
- Legacy multiplayer mode `12` branches to Join via IP, a platform friends interface when available, or Host and Play. Direct-IP submission sets `Netplay.ListenPort`, resolves through `Netplay.SetRemoteIPAsync`, and starts with public `Main.StartClientGameplay`. Host launch ultimately crosses private `Main.OnSubmitServerPassword`; server password requests cross private `OnSubmitServerPasswordFromRequest`.

Keep the private boundaries isolated and version-checked. The installed tModLoader build is authoritative for those shims.

### Text Entry Sounds

`Main.DrawPlayerChat` compares the value before and after `Main.GetInputText`, plays legacy sound ID `12` (`SoundID.MenuTick`) for any edit, and plays sound ID `11` (`SoundID.MenuClose`) when Enter submits and closes chat. Menu text inputs use the same edit sound. Custom text fields should preserve those sounds while deriving spoken edits from the semantic old/new value.

### Settings, Achievements, And Workshop Data

- Legacy settings mode `11` branches to modes `112` (general), `1112` (interface), `1111` (video), `26` (audio), `1125` (cursor), `1127`/`Main.ManageControlsMenu` (controls), `1213` (language), and `10017` (tModLoader settings). Most values are public `Main`, `PlayerInput`, `ItemSlot.Options`, resource-set, minimap-frame, lighting, and progress-bar state.
- `GameContent/UI/States/UIAchievementsMenu.cs` obtains semantic rows from public `Main.Achievements.CreateAchievementsList()`. `Achievement` exposes localized friendly name, description, category, completion, and optional typed tracker values.
- `GameContent/UI/States/UIWorkshopHub.cs` has six normal hub branches in the patched loader: Installed Mods, Mod Sources, Mod Browser, Mod Packs, subscribed Workshop worlds, and resource packs, plus logs. Installed-mod discovery, deletion, and reload live behind internal `ModOrganizer.FindMods`, `ModOrganizer.DeleteMod`, and `ModLoader.Reload`; keep reflection for these functions localized. Each `UI/UIModItem.cs` row always exposes More Info, adds Configure when the mod is loaded and has registered configs, and adds Delete when the mod is unloaded and its location can be deleted. `Config/UI/UIModConfigList.cs` enumerates the internal `ConfigManager.Configs` registry. `UIModConfig` edits a public `ConfigManager.GeneratePopulatedClone` result and persists it through the active config's public `ModConfig.SaveChanges`; preserve that pending-edit boundary in custom config interfaces. Resource packs use public `AssetInitializer.CreateResourcePackList` and `Main.AssetSourceController.UseResourcePacks`. Subscribed-world import uses `SocialAPI.Workshop.GetListOfSubscribedWorldPaths` and `ImportDownloadedWorldToLocalSaves`.

### In-Game Inventory And Options

- `ModSystem.PostUpdateInput()` runs immediately after `PlayerInput.UpdateInput()` and before the current triggers are copied into the local player. This makes it the narrow supported point for semantic keyboard navigation while the inventory is open. Clear any consumed `PlayerInput.Triggers.Current` and `JustPressed` actions there; Tab normally maps to `MapStyle`.
- `Main.DrawInventory()` is the authoritative inventory layout. Player storage uses `inventory[0..9]` for the hotbar, `[10..49]` for the remaining inventory, `[50..53]` for coins, and `[54..57]` for ammo. Equipment uses `armor[0..9]`, vanity `armor[10..19]`, dyes `dye[0..9]`, and the five `miscEquips`/`miscDyes` slots. Index 58 is transient internal mouse-item storage and is not a visible inventory slot.
- Preserve slot behavior through public `ItemSlot.LeftClick`/`RightClick` with the matching `ItemSlot.Context`: inventory `0`, coins `1`, ammo `2`, chest `3`, banks `4`, reforge `5`, trash `6`, Guide `7`, armor/vanity/accessories/dyes `8..12`, shop `15`, miscellaneous equipment `16..20`, and crafting material `22`. tModLoader accessory slots use negative contexts `-10`, `-11`, and `-12`. For explicit semantic actions, `ItemSlot.PickupItemIntoMouse` transfers one item with the appropriate container sync, `Equippable`/`SwapEquip` preserve equipment rules, and `SellOrTrash` preserves the recoverable trash slot or active-shop sale behavior. `PlayerInput.TryEnteringFastUseModeForInventorySlot` stops recognizing a one-item source after its transfer empties that slot. For deterministic semantic quick-use, stage one item with `PickupItemIntoMouse`, enter `TryEnteringFastUseModeForMouseItem`, and return any non-consumed mouse item after fast-use and the item animation/time finish.
- `ChestUI.GetContainerUsageInfo` resolves the active chest or personal bank array. Public `LootAll`, `DepositAll`, `QuickStack`, `Restock`, and `ItemSorting.SortChest` preserve transfer, visualization, and multiplayer behavior. Use `ContainerTransferContext.FromUnknown` when keyboard activation has no world-space click location.
- Craftable semantic rows come from `Main.availableRecipe[0..numAvailableRecipes]`; activate through public `Main.CraftItem`. Requirements are in `Recipe.requiredItem`, `requiredTile`, and `Conditions`. Current tModLoader exposes environment requirements such as liquids and biomes through localized `Recipe.Conditions`; do not rely on the older decompiled snapshot's `needWater`/`needLava`-style fields when compiling against a newer install.
- NPC dialog and button behavior is assembled in `Main.DrawNPCChat()`. Preserve mod behavior through public `NPCLoader.SetChatButtons`, `PreChatButtonClicked`, and `OnChatButtonClicked`; the latter also opens a named `ModNPC` shop through `NPCShopDatabase`. Vanilla shops ultimately call `Main.SetNPCShopIndex(1)` and `Chest.SetupShop`, while the Guide and Goblin Tinkerer switch `Main.InGuideCraftMenu` and `Main.InReforgeMenu` respectively.
- The in-game options window is legacy `IngameOptions`, while fullscreen in-game states are hosted by `Main.InGameUI`. `IngameFancyUI.OpenUIState` and `Close` preserve chat clearing, inventory visibility, and close behavior, so accessible `UIState` menus can be shared between title and in-game hosts without replacing gameplay UI globally.

## Search Recipes

```powershell
# Find the supported system hook and its dispatcher.
rg -n "UpdateUI\(|PostUpdateInput\(|ModifyInterfaceLayers\(" "TModLoader Decompiled\Terraria\ModLoader" -g "*.cs"

# Trace a tModLoader menu mode and its transitions.
rg -n "10024|UIModConfig" "TModLoader Decompiled\Terraria\ModLoader" -g "*.cs"

# Find UI state construction and activation.
rg -n "SetState\(|OnInitialize\(|Append\(" "TModLoader Decompiled\Terraria\ModLoader\UI" -g "*.cs"

# Find a public API declaration and loader dispatch.
rg -n "virtual .*HookName|HookName\(" "TModLoader Decompiled\Terraria\ModLoader" -g "*.cs"

# Compare a patched Terraria method with standalone vanilla.
rg -n "MethodName\(" "TModLoader Decompiled\Terraria" "Terraria Decompiled\Terraria" -g "*.cs"

# Locate likely menu, UI, or input files by name.
rg --files "TModLoader Decompiled\Terraria" -g "*.cs" | rg "Menu|UI|Input|Keybind"
```

## Hook Selection Rules

1. Prefer a documented virtual hook on `ModSystem`, `ModPlayer`, or another `Mod*`/`Global*` type.
2. Read the matching `*Loader` or `SystemLoader` call site to confirm timing and client/server behavior.
3. Use public UI events or state inspection when they expose enough semantic information.
4. Use `MonoModHooks`/`On.Terraria` detours only for behavior that public hooks cannot observe, and always remove hooks during unload.
5. Validate the final signature through a compile because generated hook surfaces and the installed loader can differ from this decompilation.

## Decompiler Caveats

The output contains artifacts such as encoded generated names, flattened control flow, and unresolved IL comments. Use it for navigation, hook timing, and signatures, not as code to copy. The tracked mod must remain an independent implementation built against tModLoader's supported references.
