#nullable enable

using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;

namespace Ariadne.Ingame.Teleport;

/// <summary>
/// Performs the teleport itself and, on a multiplayer client, watches for the server rejecting
/// or correcting it. The server is authoritative for
/// <see cref="MessageID.TeleportEntity"/>, so a silent snap back would otherwise leave the
/// player somewhere they were not told about.
/// </summary>
internal sealed class TeleportExecutor
{
	// This style retains teleport dust but Main.TeleportEffect does not play audio for it.
	private const int TeleportStyle = TeleportationStyleID.TeleportationPotion;
	private const int VerificationTicks = 30;

	private PendingVerification? _pending;

	private sealed record PendingVerification(Vector2 ExpectedPosition, string MovementDescription, int TicksRemaining);

	/// <param name="movementDescription">
	/// Slots into "The server rejected or corrected the {description}." Pass something like
	/// "scanner teleport near Guide".
	/// </param>
	internal void Execute(Player player, Vector2 destination, string movementDescription)
	{
		player.velocity = Vector2.Zero;
		player.Teleport(destination, TeleportStyle);
		player.velocity = Vector2.Zero;
		if (Main.netMode != NetmodeID.MultiplayerClient)
		{
			return;
		}

		NetMessage.SendData(
			MessageID.TeleportEntity,
			remoteClient: -1,
			ignoreClient: -1,
			text: null,
			number: 0,
			number2: player.whoAmI,
			number3: destination.X,
			number4: destination.Y,
			number5: TeleportStyle);
		_pending = new PendingVerification(destination, movementDescription, VerificationTicks);
	}

	internal void UpdateVerification()
	{
		if (_pending is not PendingVerification pending)
		{
			return;
		}

		if (Main.gameMenu || !Main.LocalPlayer.active || Main.LocalPlayer.dead)
		{
			_pending = null;
			return;
		}

		int elapsedTicks = VerificationTicks - pending.TicksRemaining;
		if (elapsedTicks >= 4 && Vector2.DistanceSquared(Main.LocalPlayer.position, pending.ExpectedPosition) > 8f * 16f * 8f * 16f)
		{
			_pending = null;
			AriadneMod.ScreenReader.Output($"The server rejected or corrected the {pending.MovementDescription}.");
			return;
		}

		int ticksRemaining = pending.TicksRemaining - 1;
		_pending = ticksRemaining > 0 ? pending with { TicksRemaining = ticksRemaining } : null;
	}

	internal void Reset()
	{
		_pending = null;
	}
}
