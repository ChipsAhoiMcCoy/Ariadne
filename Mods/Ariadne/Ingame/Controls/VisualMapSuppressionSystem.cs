#nullable enable

using Terraria;
using Terraria.GameInput;
using Terraria.ModLoader;

namespace Ariadne.Ingame.Controls;

/// <summary>
/// Turns off Terraria's drawn map for the duration of a world. The accessible map
/// replaces it, so the minimap and overlay only cost frame time, and their keys
/// only change a picture the player cannot read. MapFull is deliberately left
/// live because AccessibleIngameMenuSystem consumes that press to open the
/// accessible map instead.
/// </summary>
[Autoload(Side = ModSide.Client)]
internal sealed class VisualMapSuppressionSystem : ModSystem
{
	private bool _restoreMapEnabled;
	private bool _hasRestoreValue;

	public override void OnWorldLoad()
	{
		if (!_hasRestoreValue)
		{
			_restoreMapEnabled = Main.mapEnabled;
			_hasRestoreValue = true;
		}
		Main.mapEnabled = false;
	}

	public override void OnWorldUnload() => RestoreMapSetting();

	public override void Unload() => RestoreMapSetting();

	public override void PostUpdateInput()
	{
		if (Main.dedServ || Main.gameMenu)
		{
			return;
		}

		// Held every tick rather than set once, because vanilla's own interface
		// toggle can turn the map back on mid-session.
		Main.mapEnabled = false;
		SuppressMapTriggers();
	}

	private static void SuppressMapTriggers()
	{
		Suppress(PlayerInput.Triggers.Current);
		Suppress(PlayerInput.Triggers.JustPressed);
		Suppress(PlayerInput.Triggers.JustReleased);
	}

	private static void Suppress(TriggersSet triggers)
	{
		// The style key is the one that matters: with the map off it still cycles
		// minimap, overlay, and none, and plays the toggle sound on every press.
		triggers.MapStyle = false;
		triggers.MapZoomIn = false;
		triggers.MapZoomOut = false;
		triggers.MapAlphaUp = false;
		triggers.MapAlphaDown = false;
	}

	private void RestoreMapSetting()
	{
		if (!_hasRestoreValue)
		{
			return;
		}

		// Restored on the way out so Terraria's saved MapEnabled setting keeps the
		// value the player chose, rather than being rewritten by this mod.
		Main.mapEnabled = _restoreMapEnabled;
		_hasRestoreValue = false;
	}
}
