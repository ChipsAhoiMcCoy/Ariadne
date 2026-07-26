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
/// Keeps contact edge detection separate from movement so a future shared
/// bump-tone service can replace speech without altering collision behavior.
/// </summary>
internal sealed class SpokenFreecamContactFeedback : IFreecamContactFeedback
{
	private bool _leftContact;
	private bool _rightContact;
	private bool _upContact;
	private bool _downContact;
	private bool _rangeContact;

	public void Update(Vector2 inputDirection, in FreecamMovementResult movement)
	{
		UpdateDirection(
			ref _leftContact,
			inputDirection.X < 0f && movement.BlockedLeft,
			"left");
		UpdateDirection(
			ref _rightContact,
			inputDirection.X > 0f && movement.BlockedRight,
			"right");
		UpdateDirection(
			ref _upContact,
			inputDirection.Y < 0f && movement.BlockedUp,
			"up");
		UpdateDirection(
			ref _downContact,
			inputDirection.Y > 0f && movement.BlockedDown,
			"down");

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
		_leftContact = false;
		_rightContact = false;
		_upContact = false;
		_downContact = false;
		_rangeContact = false;
	}

	private static void UpdateDirection(ref bool previousContact, bool contact, string direction)
	{
		if (contact && !previousContact)
		{
			AriadneMod.ScreenReader.Output(
				Language.GetTextValue(
					"Mods.Ariadne.Announcements.FreecamBlocked",
					direction),
				interrupt: false);
		}
		previousContact = contact;
	}
}
