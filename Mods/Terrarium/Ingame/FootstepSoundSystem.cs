#nullable enable

using System;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;
using Terrarium.Audio;

namespace Terrarium.Ingame;

[Autoload(Side = ModSide.Client)]
internal sealed class FootstepSoundSystem : ModSystem
{
	private const float TileWidth = 16f;
	private const float FootstepVolume = 0.48f;
	private const float GroundProbeDistance = 0.1f;
	private const float TeleportThreshold = TileWidth * 1.5f;

	private FootstepSoundBank? _sounds;
	private int _previousTileX;
	private float _previousCenterX;
	private bool _isTracking;

	public override void Load()
	{
		_sounds = FootstepSoundBank.Create();
	}

	public override void OnWorldLoad()
	{
		ResetTracking();
	}

	public override void OnWorldUnload()
	{
		ResetTracking();
	}

	public override void PostUpdatePlayers()
	{
		if (!CanTrackLocalPlayer())
		{
			ResetTracking();
			return;
		}

		Player player = Main.LocalPlayer;
		float centerX = player.Center.X;
		int tileX = (int)MathF.Floor(centerX / TileWidth);
		if (!_isTracking)
		{
			_previousCenterX = centerX;
			_previousTileX = tileX;
			_isTracking = true;
			return;
		}

		float horizontalMovement = centerX - _previousCenterX;
		int crossedTileCount = Math.Abs(tileX - _previousTileX);
		_previousCenterX = centerX;
		_previousTileX = tileX;

		bool isWalking = MathF.Abs(horizontalMovement) > 0.01f && !player.mount.Active;
		bool teleported = MathF.Abs(horizontalMovement) > TeleportThreshold || crossedTileCount > 1;
		if (crossedTileCount == 1 && isWalking && !teleported && IsTouchingGround(player))
		{
			_sounds?.Play(FootstepVolume);
		}
	}

	private static bool IsTouchingGround(Player player)
	{
		int gravityDirection = Math.Sign(player.gravDir);
		int collisionDirection = gravityDirection > 0 ? 2 : 3;
		Vector2 probeOffset = Vector2.UnitY * (GroundProbeDistance * gravityDirection);

		return Collision.FindCollisionTile(
			collisionDirection,
			player.position + probeOffset,
			GroundProbeDistance,
			player.width,
			player.height,
			gravDir: gravityDirection,
			checkSlopes: true).Count > 0;
	}

	public override void Unload()
	{
		_sounds?.Dispose();
		_sounds = null;
		ResetTracking();
	}

	private static bool CanTrackLocalPlayer()
	{
		if (Main.dedServ || Main.gameMenu || Main.gamePaused || !Main.hasFocus)
		{
			return false;
		}

		Player player = Main.LocalPlayer;
		return player.active && !player.dead && !player.ghost;
	}

	private void ResetTracking()
	{
		_previousTileX = 0;
		_previousCenterX = 0f;
		_isTracking = false;
	}
}
