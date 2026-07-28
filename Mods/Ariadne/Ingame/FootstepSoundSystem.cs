#nullable enable

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Ariadne.Audio;
using Ariadne.Configs;

namespace Ariadne.Ingame;

[Autoload(Side = ModSide.Client)]
internal sealed class FootstepSoundSystem : ModSystem
{
	private const float TileWidth = 16f;
	private const float GroundProbeDistance = 0.1f;
	private const float TeleportThreshold = TileWidth * 1.5f;

	// A perfect fourth above the solid-ground step: far enough to name without
	// listening for the interval, and close enough to still read as a footstep.
	private const float PlatformPitch = 0.4f;

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
		AriadneClientConfig config = ModContent.GetInstance<AriadneClientConfig>();
		if (!config.FootstepSoundsEnabled ||
			config.FootstepVolumePercent <= 0 ||
			!CanTrackLocalPlayer())
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
		if (crossedTileCount == 1 && isWalking && !teleported && IsTouchingGround(player, out bool onPlatform))
		{
			_sounds?.Play(
				config.FootstepVolumePercent / 100f,
				onPlatform ? PlatformPitch : 0f);
		}
	}

	private static bool IsTouchingGround(Player player, out bool onPlatform)
	{
		onPlatform = false;

		int gravityDirection = Math.Sign(player.gravDir);
		int collisionDirection = gravityDirection > 0 ? 2 : 3;
		Vector2 probeOffset = Vector2.UnitY * (GroundProbeDistance * gravityDirection);

		List<Point> footing = Collision.FindCollisionTile(
			collisionDirection,
			player.position + probeOffset,
			GroundProbeDistance,
			player.width,
			player.height,
			gravDir: gravityDirection,
			checkSlopes: true);
		if (footing.Count == 0)
		{
			return false;
		}

		// Every supporting tile has to be a platform, because one solid tile under
		// the player is enough to stop a descent the raised pitch would promise.
		onPlatform = footing.TrueForAll(IsPlatformTile);
		return true;
	}

	private static bool IsPlatformTile(Point coordinates)
	{
		if (!WorldGen.InWorld(coordinates.X, coordinates.Y, 1))
		{
			return false;
		}

		Tile tile = Main.tile[coordinates.X, coordinates.Y];
		return tile.HasTile && TileID.Sets.Platforms[tile.TileType];
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
