#nullable enable

using System.Diagnostics;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Audio;
using Terraria.ID;

namespace Terrarium.Ingame.Controls;

internal sealed class PrecisionCursorRepeater
{
	private static readonly long InitialDelayTicks = MillisecondsToStopwatchTicks(450);
	private static readonly long RepeatIntervalTicks = MillisecondsToStopwatchTicks(85);

	private Point _heldDirection;
	private long _nextRepeatTimestamp;

	internal void Update(
		WorldCursorState state,
		Player player,
		Point currentDirection,
		bool anyJustPressed,
		bool interruptInitialAnnouncement)
	{
		long now = Stopwatch.GetTimestamp();
		if (currentDirection == Point.Zero)
		{
			Reset();
			return;
		}

		if (anyJustPressed || _heldDirection != currentDirection)
		{
			state.MovePrecision(currentDirection);
			Announce(state, player, interruptInitialAnnouncement);
			_heldDirection = currentDirection;
			_nextRepeatTimestamp = now + InitialDelayTicks;
			return;
		}

		if (now < _nextRepeatTimestamp)
		{
			return;
		}

		int steps = 0;
		do
		{
			state.MovePrecision(currentDirection);
			Announce(state, player, interrupt: false);
			_nextRepeatTimestamp += RepeatIntervalTicks;
			steps++;
		}
		while (now >= _nextRepeatTimestamp && steps < 4);

		SoundStyle quietStep = SoundID.MenuTick;
		quietStep.Volume *= 0.25f;
		for (int index = 0; index < steps; index++)
		{
			SoundEngine.PlaySound(quietStep);
		}

	}

	internal void Reset()
	{
		_heldDirection = Point.Zero;
		_nextRepeatTimestamp = 0;
	}

	private static void Announce(WorldCursorState state, Player player, bool interrupt)
	{
		WorldTargetDescription description = WorldTargetDescriber.Describe(state.PrecisionTile, player);
		TerrariumMod.ScreenReader.Output(description.DetailedText, interrupt);
	}

	private static long MillisecondsToStopwatchTicks(int milliseconds)
	{
		return (long)(Stopwatch.Frequency * (milliseconds / 1000d));
	}
}
