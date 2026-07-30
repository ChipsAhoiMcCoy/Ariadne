# Ariadne Agent Guide

Keep answers succinct.

## Build

Build with `.\Tools\build.ps1`. Do not build `Mods/Ariadne/Ariadne.csproj` directly; the script uses the .NET host bundled with tModLoader, which may be the only one installed.

Compile every code change before reporting it as done, and report a failure with the compiler output rather than calling it a partial success. In-game behavior cannot be verified from here, so say plainly which parts were compiled only and still need a play test.

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
4. Search declarations and call sites with the Grep tool scoped to one tree; do not scan both complete trees unless the maps and targeted searches are insufficient.
5. Verify hook names and public signatures against the tModLoader tree and by compiling the mod.

The snapshots are not the same version. The copied tModLoader tree is Terraria 1.4.4.9 with tModLoader 2026.04.3.0, while the standalone Terraria tree is 1.4.5.6. Do not transfer signatures or behavior between them without checking both. If the installed tModLoader version differs from the copied snapshot, treat the installed assemblies and a successful build as authoritative.

Treat both decompiled trees as read-only reference material. Never edit them, copy substantial implementation from them, stage them, commit them, or publish them. Implement features through supported tModLoader APIs and narrowly scoped runtime hooks where an API does not exist.

These trees are large. Always scope a search to one of them with an explicit `path`, and prefer a type or symbol name over a broad phrase:

- Grep: `pattern: "class TypeName|MethodName\\("`, `path: "TModLoader Decompiled/Terraria"`, `glob: "*.cs"`
- Glob: `pattern: "**/ModLoader/**/*Input*.cs"`, `path: "TModLoader Decompiled/Terraria"`

When a stable, high-value entry point is discovered, update the appropriate map in the same change.
