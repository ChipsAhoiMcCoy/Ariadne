#nullable enable

using System;
using Terraria;
using Terraria.ModLoader;
using Ariadne.Audio;
using Ariadne.Configs;

namespace Ariadne.Ingame;

[Autoload(Side = ModSide.Client)]
internal sealed class ElevationMovementCueSystem : ModSystem
{
	private const float TileSize = 16f;
	private const float TeleportThresholdPixels = TileSize * 6f;
	private const ulong MinimumCueIntervalTicks = 6;

	private NavigationCueSoundBank? _sounds;
	private float _previousGravityRelativeCenter;
	private int _previousTile;
	private float _previousGravityDirection;
	private ulong _lastCueTick;
	private bool _isTracking;

	public override void Load()
	{
		_sounds = NavigationCueSoundBank.Create(Mod);
	}

	public override void OnWorldLoad() => ResetTracking();

	public override void OnWorldUnload() => ResetTracking();

	public override void PostUpdatePlayers()
	{
		AriadneClientConfig config = ModContent.GetInstance<AriadneClientConfig>();
		if (!config.ElevationMovementCuesEnabled ||
			config.ElevationMovementCueVolumePercent <= 0 ||
			!GameplayAudioGate.CanListen())
		{
			ResetTracking();
			return;
		}

		Player player = Main.LocalPlayer;
		if (!player.pulley)
		{
			// Terraria sets pulley only while the player is attached to a rope or
			// similar climbable tile. Resetting here keeps ordinary jumps, falls,
			// mounts, grapples, and later rope grabs from inheriting a crossing.
			ResetTracking();
			return;
		}

		if (player.teleporting || player.teleportTime > 0f)
		{
			// Terraria leaves teleportTime active for the entry/exit visual window, so
			// this catches teleports even when their displacement happens to be shorter
			// than the generic discontinuity threshold below.
			ResetTracking();
			return;
		}

		float gravityDirection = player.gravDir < 0f ? -1f : 1f;
		float gravityRelativeCenter = player.Center.Y * gravityDirection;
		int tile = (int)MathF.Floor(gravityRelativeCenter / TileSize);
		if (!_isTracking || gravityDirection != _previousGravityDirection)
		{
			BeginTracking(gravityRelativeCenter, tile, gravityDirection);
			return;
		}

		float movement = gravityRelativeCenter - _previousGravityRelativeCenter;
		int crossedTiles = Math.Abs(tile - _previousTile);
		_previousGravityRelativeCenter = gravityRelativeCenter;
		_previousTile = tile;
		if (MathF.Abs(movement) > TeleportThresholdPixels)
		{
			_lastCueTick = Main.GameUpdateCount;
			return;
		}

		if (crossedTiles == 0 || MathF.Abs(movement) < 0.01f)
		{
			return;
		}

		ulong now = Main.GameUpdateCount;
		if (now - _lastCueTick < MinimumCueIntervalTicks)
		{
			return;
		}

		_lastCueTick = now;
		_sounds?.Play(
			movement < 0f ? NavigationCueKind.Ascending : NavigationCueKind.Descending,
			config.ElevationMovementCueVolumePercent / 100f);
	}

	public override void Unload()
	{
		_sounds?.Dispose();
		_sounds = null;
		ResetTracking();
	}

	private void BeginTracking(float center, int tile, float gravityDirection)
	{
		_previousGravityRelativeCenter = center;
		_previousTile = tile;
		_previousGravityDirection = gravityDirection;
		_lastCueTick = Main.GameUpdateCount;
		_isTracking = true;
	}

	private void ResetTracking()
	{
		_previousGravityRelativeCenter = 0f;
		_previousTile = 0;
		_previousGravityDirection = 0f;
		_lastCueTick = 0;
		_isTracking = false;
	}
}
