# Terrarium Agent Guide

## Project

Terrarium is a tModLoader accessibility mod for Terraria. The tracked mod source is in `Mods/Terrarium/`. Keep accessibility behavior client-safe; the mod metadata uses `side = NoSync` so it can remain optional on multiplayer clients and servers.

Use C# 12 and .NET 8. Follow the existing project structure and keep features separated by responsibility once implementation grows beyond the initial mod class.

## Decompiled Source Lookup

Local decompiled references are available but are deliberately excluded from Git:

- `TModLoader Decompiled/` - the patched Terraria and tModLoader runtime source
- `Terraria Decompiled/` - the standalone vanilla Terraria source

Before searching either source tree, read the relevant tracked navigation map:

- `docs/decompilation/decompiled-tmodloader-map.md`
- `docs/decompilation/decompiled-terraria-map.md`

Use this lookup order for every investigation into game or loader behavior:

1. Read the relevant map and choose the narrowest listed entry point.
2. For code that runs in this mod, inspect `TModLoader Decompiled/` first because it contains tModLoader's patched Terraria implementation and public mod hooks.
3. Use `Terraria Decompiled/` to understand standalone vanilla behavior or compare newer vanilla changes.
4. Search declarations and call sites with `rg`; do not scan both complete trees unless the maps and targeted searches are insufficient.
5. Verify hook names and public signatures against the tModLoader tree and by compiling the mod.

The snapshots are not the same version. The copied tModLoader tree is Terraria 1.4.4.9 with tModLoader 2026.04.3.0, while the standalone Terraria tree is 1.4.5.6. Do not transfer signatures or behavior between them without checking both. If the installed tModLoader version differs from the copied snapshot, treat the installed assemblies and a successful build as authoritative.

Treat both decompiled trees as read-only reference material. Never edit them, copy substantial implementation from them, stage them, commit them, or publish them. Implement features through supported tModLoader APIs and narrowly scoped runtime hooks where an API does not exist.

Useful search forms from the repository root:

```powershell
rg -n "class TypeName|MethodName\(" "TModLoader Decompiled\Terraria" -g "*.cs"
rg -n "class TypeName|MethodName\(" "Terraria Decompiled\Terraria" -g "*.cs"
rg --files "TModLoader Decompiled\Terraria\ModLoader" | rg "Menu|UI|Input"
```

When a stable, high-value entry point is discovered, update the appropriate map in the same change.

## Menu Accessibility

For menu reading, prefer semantic state over rendered-pixel or draw-call inference. A spoken entry should be derived from its localized label, role, current state, and position in a collection when those values exist. Suppress unchanged repeated announcements, but do not hide state transitions or actionable error messages.

Account for both menu systems:

- Legacy menus driven by `Main.menuMode`, `selectedMenu`, `Main.UpdateMenu`, and `Main.DrawMenu`.
- `UserInterface`/`UIState` menus driven through `Main.MenuUI`, including tModLoader's custom UI states.

Keyboard, mouse, and gamepad focus may use different state. Check `PlayerInput`, `GamepadMainMenuHandler`, and `UILinkPointNavigator` before assuming the hovered element is the selected element.

## Build And Verification

Build and package with tModLoader's own toolchain:

```powershell
.\Tools\build.ps1
```

The script resolves `TML_INSTALL_PATH`, `TERRARIA_TML_PATH`, or `TMLSteamPath` before checking the standard Steam installation, then invokes `tModLoader.dll -build`. Do not use plain MSBuild without a full .NET SDK and the tModLoader targets.

Before finishing a code change, compile it and inspect `git status`. Confirm the two decompiled directories remain ignored with `git check-ignore` if ignore rules or repository layout changed.
