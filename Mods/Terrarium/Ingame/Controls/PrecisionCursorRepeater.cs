#nullable enable

using System;
using System.Diagnostics;
using Microsoft.Xna.Framework;

namespace Terrarium.Ingame.Controls;

internal sealed class PrecisionCursorRepeater
{
	private static readonly long InitialDelayTicks = MillisecondsToStopwatchTicks(450);
	private static readonly long RepeatIntervalTicks = MillisecondsToStopwatchTicks(85);

	private Point _heldDirection;
	private long _nextRepeatTimestamp;

	internal void Update(
		WorldCursorState state,
		Point currentDirection,
		bool anyJustPressed,
		bool interruptInitialAnnouncement,
		Action<Point, bool> reportStep)
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
			reportStep(state.PrecisionTile, interruptInitialAnnouncement);
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
			reportStep(state.PrecisionTile, false);
			_nextRepeatTimestamp += RepeatIntervalTicks;
			steps++;
		}
		while (now >= _nextRepeatTimestamp && steps < 4);
	}

	internal void Reset()
	{
		_heldDirection = Point.Zero;
		_nextRepeatTimestamp = 0;
	}

	private static long MillisecondsToStopwatchTicks(int milliseconds)
	{
		return (long)(Stopwatch.Frequency * (milliseconds / 1000d));
	}
}
