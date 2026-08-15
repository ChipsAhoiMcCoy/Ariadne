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
	internal static ModKeybind? CombatTargetCycleKeybind { get; private set; }
	internal static ModKeybind? FreecamModifierKeybind { get; private set; }
	internal static ModKeybind? PlayerStatusKeybind { get; private set; }
	internal static ModKeybind? OpenWaypointsKeybind { get; private set; }
	internal static ModKeybind? RadarSweepKeybind { get; private set; }
	internal static ModKeybind? HotbarPreviousKeybind { get; private set; }
	internal static ModKeybind? HotbarNextKeybind { get; private set; }
	internal static ModKeybind? HousingQueryKeybind { get; private set; }
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
		CombatTargetCycleKeybind = RegisterKeybind("CombatTargetCycle", Keys.Tab, introducedVersion: 5);
		// Sits directly right of the aim-right key, at the outer edge of the gameplay
		// cluster the hand already rests on. Held with the modifier it switches passive
		// discovery off for the session. It shipped once on Delete, which the migration
		// re-points for any profile still holding that.
		RadarSweepKeybind = RegisterKeybind(
			"RadarSweep",
			Keys.OemQuotes,
			introducedVersion: 7,
			replacesKey: Keys.Delete);
		// Both are chords with CombatTargetModifier. E is Grapple in every stock profile,
		// which HotbarCycleSystem withholds for the frames the chord owns.
		HotbarPreviousKeybind = RegisterKeybind("HotbarPrevious", Keys.Q, introducedVersion: 6);
		HotbarNextKeybind = RegisterKeybind("HotbarNext", Keys.E, introducedVersion: 6);
		HousingQueryKeybind = RegisterKeybind("HousingQuery", Keys.U, introducedVersion: 8);
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
		CombatTargetCycleKeybind = null;
		FreecamModifierKeybind = null;
		PlayerStatusKeybind = null;
		OpenWaypointsKeybind = null;
		RadarSweepKeybind = null;
		HotbarPreviousKeybind = null;
		HotbarNextKeybind = null;
		HousingQueryKeybind = null;
		RegisteredDefaults.Clear();
	}

	/// <summary>
	/// Declares one control and the default it should reach a profile with.
	/// </summary>
	/// <param name="replacesKey">
	/// A default this one supersedes. A profile still holding exactly that earlier key is
	/// moved across; a profile holding anything else has been rebound deliberately and is
	/// left alone.
	/// </param>
	private ModKeybind RegisterKeybind(
		string name,
		Keys defaultBinding,
		int introducedVersion,
		Keys? replacesKey = null)
	{
		ModKeybind keybind = KeybindLoader.RegisterKeybind(this, name, defaultBinding);
		RegisteredDefaults.Add(new(
			$"{Name}/{name}",
			defaultBinding.ToString(),
			introducedVersion,
			replacesKey?.ToString()));
		return keybind;
	}
}

internal readonly record struct RegisteredKeybindDefault(
	string FullName,
	string Key,
	int IntroducedVersion,
	string? ReplacesKey = null);
