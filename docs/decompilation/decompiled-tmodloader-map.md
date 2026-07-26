# Decompiled tModLoader Navigation Map

## Snapshot And Role

- Local root: `TModLoader Decompiled/`
- Product: tModLoader 2026.04.3.0 stable, commit `abca3f47`
- Bundled Terraria: 1.4.4.9
- Version source: `TModLoader Decompiled/Properties/AssemblyInfo.cs`
- Patched game root: `TModLoader Decompiled/Terraria/`
- Mod API root: `TModLoader Decompiled/Terraria/ModLoader/`

Use this tree first for Ariadne implementation work. It contains tModLoader's patched copy of Terraria as well as loader APIs and custom menus. The installed tModLoader may be newer than this copied snapshot; when versions differ, confirm signatures against the installed references by compiling.

## Fast Lookup

| Area | Primary paths and symbols |
| --- | --- |
| Mod entry point | `Terraria/ModLoader/Mod.cs`, `ModType.cs`, and `ModContent.cs` |
| Cross-cutting systems | `Terraria/ModLoader/ModSystem.cs`; especially `UpdateUI`, `PostUpdateInput`, `ModifyInterfaceLayers`, and `PostDrawInterface` |
| World/player update observation | `ModSystem.PostUpdatePlayers()` runs after every active `Player.Update` in `Main.DoUpdateInWorld`; it is a stable point for client-only position observers that do not modify movement |
| Biome state | `Terraria/Player.cs` exposes the `Zone*` properties populated during biome updates; `ModLoader/BiomeLoader.cs`, `ModBiome.cs`, `Player.InModBiome`, and `ModContent.GetContent<ModBiome>()` provide the supported mod-biome boundary |
| Informational accessories | `Terraria/ModLoader/InfoDisplay.cs`, the built-in `*InfoDisplay.cs` types, `InfoDisplayLoader.Active`, and `Main.DrawInfoAccs` define availability, vanilla values, global modifiers, nearby-clock support, and team-shared readings |
| Player ground-contact observation | `Terraria/Collision.cs`; `FindCollisionTile` can probe a short distance in the player's gravity direction with both cardinal and slope checks, preserving solid tiles, platforms, half-blocks, slopes, and inverted gravity |
| Point terrain probes | `Terraria/Collision.cs`; `IsWorldPointSolid(Vector2, bool)` rejects inactive/actuated and non-solid tiles, optionally excludes platforms, and tests full blocks, half-blocks, and all four slope shapes at the exact world point |
| Camera visibility and lighting | `Terraria/Graphics/Camera.cs` (`ScaledPosition`, `ScaledSize`) and `Terraria/Lighting.cs` (`Brightness`) define the zoom-aware visible-world and current-light boundaries |
| Draw-time camera override and clamp | `Terraria/Main.cs` (`DoDraw_UpdateCameraPosition`, `ClampScreenPositionToWorld`) applies vanilla tracking/lerp, camera modifiers, `PlayerLoader.ModifyScreenPosition`, then `SystemLoader.ModifyScreenPosition`, rounds `Main.screenPosition`, and finally clamps it to the world. `ModSystem.ModifyScreenPosition` is therefore the supported final camera-position override before rounding and clamp |
| Player-shaped movement collision | `Terraria/Collision.cs`; `TileCollision(Vector2, Vector2, int, int, bool, bool, int)` resolves full blocks, half-blocks, platforms, actuated state, cardinal tile faces, gravity direction, and world-adjacent movement, while `SlopeCollision(Vector2, Vector2, int, int, float, bool)` supplies the player slope adjustment. `Player.cs` applies `TileCollision`, advances position, then applies its `SlopingCollision` wrapper; high-speed virtual bodies should substep before following the same two public boundaries |
| Dynamic audio streaming | `Terraria/Audio/ASoundEffectBasedAudioTrack.cs`; constructs `DynamicSoundEffectInstance`, polls `PendingBufferCount` from its main-thread `Update`, submits prepared PCM buffers, and controls playback through the inherited `SoundEffectInstance` methods |
| Native sound selection and installed PCM boundary | `Terraria/WorldGen.cs` (`KillTile_PlaySounds`) is the authoritative vanilla/modded tile-hit dispatcher and reaches `TileLoader.KillSound`; its final calls converge on public `SoundEngine.PlaySound(in SoundStyle, ...)`. `SoundStyle.Variants` exposes suffixes but selected variants remain internal and normally random. `ModLoader/Engine/TMLContentManager.cs` sets `Main.instance.Content.RootDirectory` to the verified Terraria `Content` directory and optionally searches tModLoader's own `Content` first. Current installed vanilla sound XNBs are uncompressed `SoundEffectReader` payloads containing PCM16 WAVEFORMATEX data, which can be decoded locally without packaging extracted assets |
| Hostile NPC qualification and grouping | `Terraria/NPC.cs`; `CanBeChasedBy(object, bool)`, `active`, `life`, `boss`, and `realLife` expose the chaseable-enemy, living, priority, and multipart-root boundaries used by client-only awareness |
| Packaged PCM loading | `Terraria/ModLoader/Mod.cs`; `Mod.GetFileBytes(string)` is the supported boundary for reading a WAV packaged inside the owning `.tmod` before a mod validates and decodes it |
| Tile semantics and object roots | `Terraria/Main.cs` (`IsTileSpelunkable`), `Terraria/Map/MapHelper.cs` (`CreateMapTile`, `TileToLookup`), `Terraria/Lang.cs` (`GetMapObjectName`), `Terraria/ObjectData/TileObjectData.cs` (`GetTileData`, `TopLeft`), and `Terraria/WorldGen.cs` (`GetTreeBottom`, `GetTreeType`) for the biome/type of a shared tree trunk |
| World interaction and teleport | `Terraria/Player.cs` (`IsInTileInteractionRange`, `TileInteractionsCheck`, `Teleport`), `Terraria/Collision.cs`, `Terraria/ModLoader/CombinedHooks.cs` (`CanBeTeleportedTo`), and `MessageID.TeleportEntity` handling in `NetMessage.cs`/`MessageBuffer.cs` |
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
| Mod keybinds, profiles, and trigger edges | `Terraria/ModLoader/ModKeybind.cs`, `KeybindLoader.cs`, `Terraria/GameInput/PlayerInputProfile.cs`, `KeyConfiguration.cs`, and `TriggersPack.cs`; patched consumption is in `PlayerInput.cs`. `KeyConfiguration.SetupKeys` creates empty entries for registered mod controls and `ReadPreferences` restores only persisted non-empty assignments, so an existing profile can require a one-time missing-binding migration. `PlayerInput.Save()` is the public persistence boundary. `TriggersPack.Reset` clones the mutable current set into old state, so a `PostUpdateInput` feature that clears a held native trigger must retain its own press latch instead of trusting the next tick's `JustPressed` value |
| Keyboard world cursor and zoom transform | `Terraria/Main.cs` (`DoUpdate_HandleInput`, `MouseWorld`) and `Terraria/GameInput/PlayerInput.cs` (`UpdateInput`, `CacheZoomableValues`, `SetZoom_Unscaled`, `CacheMousePositionForZoom`, `SetZoom_MouseInWorld`); `ModSystem.PostUpdateInput` runs after physical input and its first mouse cache, but before the unscaled/world zoom passes and before trigger state is copied into the local player |
| Native Smart Cursor | `Terraria/GameContent/SmartCursorHelper.cs` (`SmartCursorLookup`) consumes `Main.MouseWorld`, writes `Player.tileTargetX/Y` and `Main.SmartCursorX/Y`, and exposes resolution through `Main.SmartCursorShowing`; the local-player call is inside `Player.Update`, so `ModSystem.PostUpdatePlayers` can observe the resolved tile. Accessibility feedback must track both the resolved coordinate and a semantic key because adjacent equal targets and same-tile state changes are independently meaningful |
| Native combat lock-on reference | `Terraria/GameInput/LockOnHelper.cs`; `RefreshTargets`, `ValidTarget`, and `Update` define its damageable/lit/on-screen/collision checks, including `ItemID.Sets.LockOnIgnoresCollision`. Keyboard accessibility targeting can reuse those boundaries without enabling gamepad mode or global lock-on |
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

`Main.DoUpdate_HandleInput` calls `PlayerInput.UpdateInput()` and then `SystemLoader.PostUpdateInput()`, so `ModKeybind.JustPressed` and pause/title/UI lifecycle state can be handled there without an audio callback. Continuous client streams should poll and submit their `DynamicSoundEffectInstance` buffers on this main update path. Stop and clear queued audio when `Main.gamePaused`, `!Main.hasFocus`, title state, death, or an in-game UI requires silence; `PostUpdateInput` remains the appropriate cleanup point even when movement observation in `PostUpdatePlayers` is no longer producing a fresh snapshot.

`PlayerInput.UpdateInput()` also calls `CacheZoomableValues()` before `PostUpdateInput`. A system that replaces `PlayerInput.MouseX/Y` and `Main.mouseX/Y` in `PostUpdateInput` must call the public cache method again after replacement. The subsequent `SetZoom_Unscaled`, `CacheMousePositionForZoom`, and `SetZoom_MouseInWorld` calls then derive the native zoom-aware `Main.MouseWorld` from the replacement. Setting only the public coordinate fields is insufficient because the first unscaled pass restores the earlier cached physical position.

For tModLoader screens, the concrete `UIState` owns its controls. Many list entries are custom types such as `UIModItem`, `UIModSourceItem`, and Mod Browser item types, so a single hard-coded element shape will not cover every menu. Reusable primitives are in `Terraria/ModLoader/UI/` and `UI/Elements/`, while base event and tree behavior remains under `Terraria/UI/UIElement.cs`.

`Terraria/UI/UIElement.cs` exposes the live `Children` tree, semantic `UIText` descendants, mouse-event methods, and event backing fields used by controls that do not override a click method. `UIList.Goto` is the narrow way to bring a discovered descendant row into view. A generic semantic adapter should preserve focus by `UIElement.UniqueId`, invoke the normal element event path, and isolate reflected third-party labels or callbacks so one control cannot disable the entire screen.

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
- The in-game options window is legacy `IngameOptions`, while fullscreen in-game states are hosted by `Main.InGameUI`. `IngameFancyUI.OpenUIState` and `Close` preserve chat clearing, inventory visibility, and close behavior, so accessible `UIState` menus can be shared between title and in-game hosts without replacing gameplay UI globally. `Close` deliberately keeps the inventory closed for `UIEmotesMenu`; an emote screen launched from an accessible inventory hierarchy must retain that origin and explicitly restore the inventory after the state closes.
- Information-display access should be gated through public `InfoDisplayLoader.Active(InfoDisplay)`, not inferred from inventory slots. The player's `accWatch`, `accCompass`, `accDepthMeter`, and related fields already include active equipment, nearby clocks where applicable, and vanilla same-team sharing. Vanilla display values are assembled in `Main.DrawInfoAccs`; mod-added values come from public `InfoDisplay.DisplayValue` and should pass through `InfoDisplayLoader.ModifyDisplayParameters`.
- Player biome flags are stable after `Player.UpdateBiomes` and can be observed from `ModSystem.PostUpdatePlayers`. Mod-added biomes are registered content instances discoverable with `ModContent.GetContent<ModBiome>()` and tested through public `Player.InModBiome(ModBiome)`; use their localized `DisplayName` for semantic output.
- `Main.UpdateUIStates` runs before `PlayerInput.UpdateInput` and `ModSystem.PostUpdateInput` each tick. A custom `UIState` that reads Escape directly and closes to gameplay must keep Terraria's Inventory release latch closed and consume `Current.Inventory`/`JustPressed.Inventory` until both the physical Escape key and the configured Inventory binding are released, or the same back action can reopen the inventory.
- Journey's power/duplication interface is a third `UserInterface` path embedded inside the inventory. `CreativeUI` owns a private `_uiState` while `Main.CreativeMenu.Enabled && !Blocked`; it does not set `Main.inFancyUI`. Public `CreativeUI.SacrificeItem`, `CreativeUI.GetSacrificeCount`, `CreativeItemSacrificesCatalog.Instance.SacrificeCountNeededByItemId`, and `Main.LocalPlayerCreativeTracker.ItemSacrifices.FillListOfItemsThatCanBeObtainedInfinitely` provide research and duplication semantics. `CreativeItemSacrificesCatalog.Instance.TryGetSacrificeCountCapToUnlockInfiniteItems` and `ContentSamples.CreativeResearchItemPersistentIdOverride` are the durable catalog boundaries to check when resolving research IDs. `Player.Update` automatically returns or drops `Main.mouseItem` whenever `Main.playerInventory` is false, so a full-screen replacement that duplicates onto the cursor must preserve the inventory-open state while it and any child help screen are active.
- `GameContent/UI/States/UICreativePowersMenu.cs` models Journey powers as `strip 0`, `strip 1`, and `strip 2` snap-point depths. The seven native root controls are duplication (`OptionValue` 1), research (2), time (3), weather (4), personal powers (6), the Boolean infection-spread toggle, and enemy difficulty (`OptionValue` 5). Ariadne's accessible adapter omits options 1 and 2 because its inventory actions and purpose-built duplication screen replace them; its flattened Journey branch preselects options 3, 4, and 6 directly, exposes option 5's live slider on the Journey branch itself, and retains the native Boolean control as the permission-aware infection-spread action. The private main, time, weather, and personal `MenuTree.Sliders` dictionaries retain each slider element before it is appended, allowing the adapter to expose those live getter/setter callbacks as adjustable rows without opening another submenu. Category labels come from `CreativePowers.*Category` localization keys; power buttons keep their semantic localization key in the owning creative-power handler's `_powerNameKey` or `GetButtonTextKey`, while `GroupOptionButton.OptionValue` is only a numeric internal choice. `CreativeUI.ToggleMenu` does not reset the private `MenuTree.CurrentOption` values, so a closed screen retains its open root and nested slider categories unless selected category buttons are collapsed when a new accessible session activates it.
- `UIEmotesMenu` keeps all `EmoteButton` controls under grouped `UIList` rows even when they are outside the visible viewport; `_emoteIndex` and `Lang.GetEmojiName` provide the semantic name, and `UIList.Goto` provides scrolling without exposing the `UIScrollbar` as a selectable action. `EmoteButton.LeftClick` calls public `EmoteBubble.MakeLocalPlayerEmote` and then closes the UI, so a semantic activation that should remain in the picker can call `MakeLocalPlayerEmote` directly. `UIBestiaryTest._workingSetEntries` is the full search/filter/sort result, while `UIBestiaryEntryGrid._atEntryIndex` and the BackPage/NextPage buttons are only visual pagination. `BestiaryEntry.Icon.GetHoverText`, `UIInfoProvider.GetEntryUICollectionInfo`, and `UIBestiaryEntryInfoPage.FillInfoForEntry` are the narrow semantic entry and detail boundaries. The info page calls each `IBestiaryInfoElement.ProvideUIElement` with `OwnerEntry` set, and unlock levels gate stats, item identities, and drop rates separately; `NPCStatsReportInfoElement`, filter-provider tags, `ItemDropBestiaryInfoElement`/`DropRateInfo`, and `ItemFromCatchingNPCBestiaryInfoElement` are the narrow data sources for spoken stats, habitats/events, drops, and catch items.
- `Main.hairWindow`, `Main.clothesWindow`, `Main.editSign`, `Main.editChest`, `Main.drawingPlayerChat`, `Main.mapFullscreen`, and the local player's `dead`/`respawnTimer` are legacy overlay states rather than `UIState` screens. Stylist availability comes from `Main.Hairstyles.UpdateUnlocks`; native close/apply boundaries are `Main.CancelHairWindow`, `Main.CancelClothesWindow`, sign submission, and container-name synchronization. Fullscreen map input reaches `Player.TryOpeningFullscreenMap`, while pylon destinations and travel are exposed by public `Main.PylonSystem.Pylons` and `RequestTeleportation`.

### Visible-World Scanning And Native Activation

- `Main.Camera.ScaledPosition` and `ScaledSize` are the camera-transform-aware world rectangle. `Lighting.Brightness(x, y) > 0` is the narrow current-light test; apply it before grouping so an algorithm never traverses a dark deposit or pool tile.
- `Collision.IsWorldPointSolid(position, treatPlatformsAsNonSolid: true)` is the narrow collision ray-march predicate for terrain-only awareness. It respects active/actuated state, `Main.tileSolid`, half-block and slope geometry, and the platform set. It returns false outside the guarded world interior, so callers that treat world bounds as collision must check `WorldGen.InWorld` separately. Small pixel steps followed by a short binary refinement retain those shapes without inspecting tile frames directly.
- Public `Main.IsTileSpelunkable(x, y)` includes the vanilla `Main.tileSpelunker` rule and `TileLoader.IsTileSpelunkable` hooks. `MapHelper.CreateMapTile` applies frame/style selection and mod map options, and its resulting type can be resolved through `Lang.GetMapObjectName`. `TileObjectData.GetTileData` distinguishes placed objects from terrain, while `TileObjectData.TopLeft` provides a stable multi-tile root. Containers additionally use `TileID.Sets.IsAContainer` and `TileLoader.DefaultContainerName`.
- Visible entities for semantic scans that require discovery should be filtered by viewport intersection and at least one positive-brightness tile beneath the clipped hitbox. Multipart NPC segments share `NPC.realLife`; resolve the active root once and union only the visible qualifying segment bounds for the snapshot.
- Hostile-mob audio deliberately uses a different visibility rule: require `active`, positive `life`, and `NPC.CanBeChasedBy(ignoreDontTakeDamage: true)`, then intersect each hitbox with the camera rectangle without a lighting or line-of-sight test. Group qualifying visible segments by the active `realLife` root, use the center of their combined clipped bounds, and propagate `boss` from either a segment or its root.
- Keep hostile emitter qualification and distance relative to the full `Main.Camera.ScaledPosition`/`ScaledSize` rectangle. Normalize both axes linearly against that rectangle: viewport left/center/right map to X `-1`/`0`/`+1`, and viewport top/center/bottom map to Y `-1`/`0`/`+1`. The shared spatial transform then applies the full configured ITD only at either horizontal edge and maps the complete vertical range to playback-rate pitch shifting.
- Hostile awareness uses a dedicated procedural dynamic stereo stream of continuous triangle tones with short periodic tick envelopes. Smooth proximity volume through `SpatialAudioDistanceGain`; keep the carrier fixed at 320 Hz so only vertical screen position changes its pitch through the shared emitter. Tick rate rises exponentially from 1.5 Hz at the viewport boundary to 12 Hz at the player, while horizontal position supplies ILD/ITD. Keep this stream separate from wall tones so an audio failure has a narrow boundary.
- `WorldGen.KillTile_PlaySounds(i, j, fail: true, tileCache)` selects the same tile-hit styles used by vanilla and modded mining without changing the tile. It does not return the selected style, so cursor playback uses a narrowly scoped hook around the final public `SoundEngine.PlaySound(in SoundStyle, ...)` boundary while the dispatcher runs. The hook is inactive for all other playback, converts declared variant suffixes into deterministic single-path styles, applies the cursor callback/volume, and records the returned slots for replacement and lifecycle cleanup.
- Native liquid assets in this runtime are not symmetric numbered families: water, honey, and lava entry/exit normally use `Splash`/`SplashWeak`; shimmer has four dedicated splash styles; `Lavafall` is the only dedicated lava sound exposed by `SoundID`. Cursor liquid feedback therefore alternates the strong/weak splashes for water and honey, cycles the four shimmer styles, and uses a short faded segment of `Lavafall` for a distinct native lava cue.
- `SoundStyle.GetSoundEffect` and `ActiveSound.Sound` expose asset/instance playback controls but no supported decoded-PCM readback. For native cursor ITD, decode the uncompressed PCM16 XNB from `Main.instance.Content.RootDirectory` (with tModLoader's local `Content` as the higher-priority filesystem candidate), downmix to mono, cache by exact resolved variant path, and feed the result into Ariadne's owned spatial renderer. Prewarm common tile/liquid paths asynchronously, bound the session cache, never write or package extracted audio, and immediately retain normal `SoundEngine` playback for pending, modded, resource-pack, compressed, non-PCM, or otherwise unsupported assets.
- A landing candidate can be checked with `Collision.SolidCollision`, `LavaCollision`, `AnyHurtingTiles`, a gravity-direction `TileCollision` support probe, an explicit shimmer-liquid check, and finally `CombinedHooks.CanBeTeleportedTo(player, position, i, j, context)`. The context is an arbitrary stable string and reaches both `ModPlayer` and wall teleport veto hooks.
- `Player.TileInteractionsCheck(x, y)` is the narrow native tile-use path and reaches vanilla behavior plus `ModTile.RightClick`, but `TileInteractionsUse` requires a scoped `tileInteractAttempted`/`releaseUseTile` attempt. Set `Player.tileTargetX/Y` for that one call and restore all four fields afterward. `Player.IsInTileInteractionRange(..., TileReachCheckSettings.Simple)` is the matching normal-range check.
- `Player.Teleport(position, style)` performs the local effect and position change. `TeleportationStyleID.TeleportationPotion` retains a dust effect without calling `SoundEngine`, making it suitable for silent semantic teleports. In multiplayer, `MessageID.TeleportEntity` encodes entity kind `0`, the player index, destination X/Y, and style; the server overrides the claimed player index with the sender, teleports it, checks the remote section, and relays the result. A client-only `NoSync` mod can therefore use this vanilla message without requiring server-side Ariadne content.

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
