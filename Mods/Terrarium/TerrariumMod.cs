#nullable enable

using System.Collections.Generic;
using Microsoft.Xna.Framework.Input;
using Terraria.ModLoader;
using Terrarium.Accessibility;

namespace Terrarium;

public sealed class TerrariumMod : Mod
{
	private static readonly List<RegisteredKeybindDefault> RegisteredDefaults = [];

	internal static ScreenReaderService ScreenReader { get; private set; } = new();
	internal static ModKeybind? OpenScannerKeybind { get; private set; }
	internal static ModKeybind? ToggleWallTonesKeybind { get; private set; }
	internal static ModKeybind? AimUpKeybind { get; private set; }
	internal static ModKeybind? AimLeftKeybind { get; private set; }
	internal static ModKeybind? AimDownKeybind { get; private set; }
	internal static ModKeybind? AimRightKeybind { get; private set; }
	internal static ModKeybind? UseHeldItemKeybind { get; private set; }
	internal static ModKeybind? SecondaryUseKeybind { get; private set; }
	internal static ModKeybind? CombatTargetModifierKeybind { get; private set; }
	internal static IReadOnlyList<RegisteredKeybindDefault> KeybindDefaults => RegisteredDefaults;

	public override void Load()
	{
		ScreenReader.Initialize(this);
		RegisteredDefaults.Clear();
		OpenScannerKeybind = RegisterKeybind("OpenScanner", Keys.End);
		ToggleWallTonesKeybind = RegisterKeybind("ToggleWallTones", Keys.Home);
		AimUpKeybind = RegisterKeybind("AimUp", Keys.O);
		AimLeftKeybind = RegisterKeybind("AimLeft", Keys.K);
		AimDownKeybind = RegisterKeybind("AimDown", Keys.L);
		AimRightKeybind = RegisterKeybind("AimRight", Keys.OemSemicolon);
		UseHeldItemKeybind = RegisterKeybind("UseHeldItem", Keys.I);
		SecondaryUseKeybind = RegisterKeybind("SecondaryUse", Keys.P);
		CombatTargetModifierKeybind = RegisterKeybind("CombatTargetModifier", Keys.LeftAlt);
	}

	public override void Unload()
	{
		ScreenReader.Dispose();
		ScreenReader = new ScreenReaderService();
		OpenScannerKeybind = null;
		ToggleWallTonesKeybind = null;
		AimUpKeybind = null;
		AimLeftKeybind = null;
		AimDownKeybind = null;
		AimRightKeybind = null;
		UseHeldItemKeybind = null;
		SecondaryUseKeybind = null;
		CombatTargetModifierKeybind = null;
		RegisteredDefaults.Clear();
	}

	private ModKeybind RegisterKeybind(string name, Keys defaultBinding)
	{
		ModKeybind keybind = KeybindLoader.RegisterKeybind(this, name, defaultBinding);
		RegisteredDefaults.Add(new($"{Name}/{name}", defaultBinding.ToString()));
		return keybind;
	}
}

internal readonly record struct RegisteredKeybindDefault(string FullName, string Key);
