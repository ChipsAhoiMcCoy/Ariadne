#nullable enable

using Microsoft.Xna.Framework.Input;
using Terraria.ModLoader;
using Terrarium.Accessibility;

namespace Terrarium;

public sealed class TerrariumMod : Mod
{
	internal static ScreenReaderService ScreenReader { get; private set; } = new();
	internal static ModKeybind? OpenScannerKeybind { get; private set; }
	internal static ModKeybind? ToggleWallTonesKeybind { get; private set; }

	public override void Load()
	{
		ScreenReader.Initialize(this);
		OpenScannerKeybind = KeybindLoader.RegisterKeybind(this, "OpenScanner", Keys.End);
		ToggleWallTonesKeybind = KeybindLoader.RegisterKeybind(this, "ToggleWallTones", Keys.Home);
	}

	public override void Unload()
	{
		ScreenReader.Dispose();
		ScreenReader = new ScreenReaderService();
		OpenScannerKeybind = null;
		ToggleWallTonesKeybind = null;
	}
}
