#nullable enable

using Microsoft.Xna.Framework;
using Terraria.Localization;

namespace Ariadne.Ingame.Freecam;

internal interface IFreecamContactFeedback
{
	void Update(Vector2 inputDirection, in FreecamMovementResult movement);

	void Reset();
}

/// <summary>
/// Keeps contact edge detection separate from movement so feedback can change
/// without altering collision behavior. Terrain contact uses the same bump cue
/// the live player gets, panned and pitched toward the blocked direction. The
/// range limit stays spoken because it is a freecam boundary, not terrain.
/// </summary>
internal sealed class AudibleFreecamContactFeedback : IFreecamContactFeedback
{
	private readonly MovementBumpCadence _cadence =
		new(onsetTicks: 1, MovementBumpCadence.DefaultRepeatTicks);
	private bool _rangeContact;

	public void Update(Vector2 inputDirection, in FreecamMovementResult movement)
	{
		int horizontal =
			(inputDirection.X > 0f && movement.BlockedRight ? 1 : 0) -
			(inputDirection.X < 0f && movement.BlockedLeft ? 1 : 0);
		int vertical =
			(inputDirection.Y > 0f && movement.BlockedDown ? 1 : 0) -
			(inputDirection.Y < 0f && movement.BlockedUp ? 1 : 0);
		if (_cadence.Advance(DirectionKey(horizontal, vertical)))
		{
			MovementBumpSystem.Play(horizontal, vertical);
		}

		bool rangeContact = inputDirection.LengthSquared() > 0f && movement.RangeLimited;
		if (rangeContact && !_rangeContact)
		{
			AriadneMod.ScreenReader.Output(
				Language.GetTextValue("Mods.Ariadne.Announcements.FreecamRangeLimit"),
				interrupt: false);
		}
		_rangeContact = rangeContact;
	}

	public void Reset()
	{
		_cadence.Reset();
		_rangeContact = false;
	}

	private static int DirectionKey(int horizontal, int vertical)
	{
		return horizontal == 0 && vertical == 0
			? 0
			: (horizontal + 2) * 8 + vertical + 2;
	}
}
