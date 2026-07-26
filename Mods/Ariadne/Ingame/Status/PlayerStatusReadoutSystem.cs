#nullable enable

using System.Collections.Generic;
using Terraria;
using Terraria.ModLoader;
using Ariadne.Ingame.Controls;

namespace Ariadne.Ingame.Status;

/// <summary>
/// Speaks one character-status entry per keypress and advances through the
/// report while the player keeps pressing, restarting at the first entry once
/// the sequence has been left alone.
/// </summary>
[Autoload(Side = ModSide.Client)]
internal sealed class PlayerStatusReadoutSystem : ModSystem
{
	private const int SequenceIdleResetTicks = 240;

	private string _lastSpokenKey = string.Empty;
	private int _idleTicks = SequenceIdleResetTicks;

	public override void OnWorldLoad() => ResetSequence();

	public override void OnWorldUnload() => ResetSequence();

	public override void PostUpdateInput()
	{
		if (_idleTicks < SequenceIdleResetTicks)
		{
			_idleTicks++;
		}

		if (AriadneMod.PlayerStatusKeybind?.JustPressed != true ||
			!WorldInputContext.IsUnobstructedGameplay())
		{
			return;
		}

		List<PlayerStatusEntry> entries = PlayerStatusReadout.Build(Main.LocalPlayer);
		if (entries.Count == 0)
		{
			return;
		}

		int index = 0;
		if (_idleTicks < SequenceIdleResetTicks && _lastSpokenKey.Length > 0)
		{
			// Track the entry by key rather than position so a report that gains or
			// loses a conditional line mid-sequence still advances predictably.
			int previous = entries.FindIndex(entry => entry.Key == _lastSpokenKey);
			index = previous < 0 ? 0 : (previous + 1) % entries.Count;
		}

		_lastSpokenKey = entries[index].Key;
		_idleTicks = 0;
		AriadneMod.ScreenReader.Output(entries[index].Text);
	}

	public override void Unload() => ResetSequence();

	private void ResetSequence()
	{
		_lastSpokenKey = string.Empty;
		_idleTicks = SequenceIdleResetTicks;
	}
}
