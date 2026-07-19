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

For a general observer, start with `ModSystem.UpdateUI(GameTime)` and `ModSystem.PostUpdateInput()`. Inspect `Main.gameMenu`, `Main.menuMode`, `Main.MenuUI.CurrentState`, and input-mode state, but avoid announcing every update. Track the semantic selection and speak only when it changes or when the selected item's state changes.

For tModLoader screens, the concrete `UIState` owns its controls. Many list entries are custom types such as `UIModItem`, `UIModSourceItem`, and Mod Browser item types, so a single hard-coded element shape will not cover every menu. Reusable primitives are in `Terraria/ModLoader/UI/` and `UI/Elements/`, while base event and tree behavior remains under `Terraria/UI/UIElement.cs`.

Gamepad focus is still driven by `Terraria/UI/Gamepad/UILinkPointNavigator.cs`. tModLoader UI states commonly assign `UILinkPointNavigator.Shortcuts.BackButtonCommand` and `BackButtonGoto`; search those assignments to understand controller navigation and back behavior.

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
