#nullable enable

namespace Ariadne.Ingame;

/// <summary>
/// Turns a sustained blocked direction into one cue on contact followed by a
/// steady repeat, so leaning on a surface keeps reporting without sounding every
/// frame. A different blocked direction restarts the cadence immediately.
/// </summary>
internal sealed class MovementBumpCadence
{
	internal const int DefaultRepeatTicks = 12;

	private readonly int _onsetTicks;
	private readonly int _repeatTicks;
	private int _direction;
	private int _blockedTicks;

	internal MovementBumpCadence(int onsetTicks, int repeatTicks)
	{
		_onsetTicks = onsetTicks;
		_repeatTicks = repeatTicks;
	}

	/// <summary>
	/// Advances one update and reports whether the cue should sound now.
	/// <paramref name="direction"/> is zero when nothing is blocked; any other
	/// value identifies which direction is blocked.
	/// </summary>
	internal bool Advance(int direction)
	{
		if (direction == 0)
		{
			Reset();
			return false;
		}

		if (direction != _direction)
		{
			_direction = direction;
			_blockedTicks = 0;
		}

		_blockedTicks++;
		return _blockedTicks == _onsetTicks ||
			(_blockedTicks > _onsetTicks &&
				(_blockedTicks - _onsetTicks) % _repeatTicks == 0);
	}

	internal void Reset()
	{
		_direction = 0;
		_blockedTicks = 0;
	}
}
