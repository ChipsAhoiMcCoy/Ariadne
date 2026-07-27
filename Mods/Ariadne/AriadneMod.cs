#nullable enable

using System.Collections.Generic;
using Microsoft.Xna.Framework.Input;
using Terraria.ModLoader;
using Ariadne.Accessibility;

namespace Ariadne;

public sealed class AriadneMod : Mod
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
	internal static ModKeybind? FreecamModifierKeybind { get; private set; }
	internal static ModKeybind? PlayerStatusKeybind { get; private set; }
	internal static ModKeybind? OpenWaypointsKeybind { get; private set; }
	internal static IReadOnlyList<RegisteredKeybindDefault> KeybindDefaults => RegisteredDefaults;

	public override void Load()
	{
		ScreenReader.Initialize(this);
		RegisteredDefaults.Clear();
		OpenScannerKeybind = RegisterKeybind("OpenScanner", Keys.End, introducedVersion: 1);
		ToggleWallTonesKeybind = RegisterKeybind("ToggleWallTones", Keys.Home, introducedVersion: 1);
		AimUpKeybind = RegisterKeybind("AimUp", Keys.O, introducedVersion: 1);
		AimLeftKeybind = RegisterKeybind("AimLeft", Keys.K, introducedVersion: 1);
		AimDownKeybind = RegisterKeybind("AimDown", Keys.L, introducedVersion: 1);
		AimRightKeybind = RegisterKeybind("AimRight", Keys.OemSemicolon, introducedVersion: 1);
		UseHeldItemKeybind = RegisterKeybind("UseHeldItem", Keys.I, introducedVersion: 1);
		SecondaryUseKeybind = RegisterKeybind("SecondaryUse", Keys.P, introducedVersion: 1);
		CombatTargetModifierKeybind = RegisterKeybind("CombatTargetModifier", Keys.LeftAlt, introducedVersion: 1);
		FreecamModifierKeybind = RegisterKeybind("FreecamModifier", Keys.RightShift, introducedVersion: 2);
		PlayerStatusKeybind = RegisterKeybind("PlayerStatus", Keys.Back, introducedVersion: 3);
		// Held with CombatTargetModifier, which doubles as Ariadne's command prefix.
		OpenWaypointsKeybind = RegisterKeybind("OpenWaypoints", Keys.W, introducedVersion: 4);
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
		FreecamModifierKeybind = null;
		PlayerStatusKeybind = null;
		OpenWaypointsKeybind = null;
		RegisteredDefaults.Clear();
	}

	private ModKeybind RegisterKeybind(string name, Keys defaultBinding, int introducedVersion)
	{
		ModKeybind keybind = KeybindLoader.RegisterKeybind(this, name, defaultBinding);
		RegisteredDefaults.Add(new($"{Name}/{name}", defaultBinding.ToString(), introducedVersion));
		return keybind;
	}
}

internal readonly record struct RegisteredKeybindDefault(
	string FullName,
	string Key,
	int IntroducedVersion);
