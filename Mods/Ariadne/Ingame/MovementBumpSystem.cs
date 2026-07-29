#nullable enable

using System;
using Terraria;
using Terraria.ModLoader;
using Ariadne.Audio;
using Ariadne.Configs;
using Ariadne.Ingame.Freecam;

namespace Ariadne.Ingame;

/// <summary>
/// Sounds a low thud whenever a movement request stops making progress. The
/// live player is watched here, and freecam feeds its own collision result
/// through <see cref="Play"/> so both report a blocked direction identically.
/// </summary>
[Autoload(Side = ModSide.Client)]
internal sealed class MovementBumpSystem : ModSystem
{
	private const float VerticalPitch = 0.30f;
	private const float ProgressThresholdPixels = 0.05f;
	private const float RestingSpeedThreshold = 0.05f;
	private const float TeleportThresholdPixels = 24f;
	private const int PlayerOnsetTicks = 4;
	private const float TileSize = 16f;
	// A short drop should still be audible, and beyond this a landing is already as
	// heavy as the cue can usefully report.
	private const float FullWeightFallTiles = 15f;
	private const float MinimumLandingWeight = 0.55f;

	private static MovementBumpSystem? _instance;

	private readonly MovementBumpCadence _cadence =
		new(PlayerOnsetTicks, MovementBumpCadence.DefaultRepeatTicks);
	private WallBumpSoundBank? _sounds;
	private float _previousPositionX;
	private bool _isTracking;
	private bool _wasGrappling;

	public override void Load()
	{
		_instance = this;
		_sounds = WallBumpSoundBank.Create(Mod);
	}

	public override void OnWorldLoad()
	{
		ResetTracking();
	}

	public override void OnWorldUnload()
	{
		ResetTracking();
	}

	public override void PreUpdatePlayers()
	{
		// Terraria clears the hook list at the end of every player update and the
		// grapple projectiles refill it later in the frame, so this boundary is the
		// only one where it still reads as attached.
		_wasGrappling = Main.LocalPlayer.grappling[0] >= 0;
	}

	public override void PostUpdatePlayers()
	{
		if (FreecamSystem.IsActive || !CanTrackLocalPlayer())
		{
			ResetTracking();
			return;
		}

		Player player = Main.LocalPlayer;
		ReportVerticalContact();
		float positionX = player.position.X;
		if (!_isTracking)
		{
			_previousPositionX = positionX;
			_isTracking = true;
			return;
		}

		float progress = positionX - _previousPositionX;
		_previousPositionX = positionX;
		if (MathF.Abs(progress) > TeleportThresholdPixels)
		{
			_cadence.Reset();
			return;
		}

		int requested = (player.controlRight ? 1 : 0) - (player.controlLeft ? 1 : 0);
		// A blocked axis is zeroed by Terraria's own tile collision, so requiring
		// both a settled speed and no progress keeps momentum on ice and knockback
		// from reading as contact.
		bool blocked = requested != 0 &&
			MathF.Abs(player.velocity.X) <= RestingSpeedThreshold &&
			progress * requested <= ProgressThresholdPixels;
		if (_cadence.Advance(blocked ? requested : 0))
		{
			Play(verticalDirection: 0, surface: SurfaceAhead(player, requested));
		}
	}

	/// <summary>
	/// Sounds the landing and head-contact cues. Both designs already existed but
	/// only freecam could ever reach them, because the live player was tracked on
	/// the horizontal axis alone and always reported a vertical direction of zero.
	/// </summary>
	private static void ReportVerticalContact()
	{
		if (PlayerFallTracker.JustLanded)
		{
			float weight = Math.Clamp(PlayerFallTracker.LandingFallTiles / FullWeightFallTiles, 0f, 1f);
			Play(
				verticalDirection: 1,
				volumeScale: MinimumLandingWeight + (1f - MinimumLandingWeight) * weight);
		}
		else if (PlayerFallTracker.JustStruckCeiling)
		{
			Play(verticalDirection: -1);
		}
	}

	/// <summary>
	/// Identifies what a horizontal bump ran into. A door is checked across the body
	/// because a door stands three tiles tall and the player meets it at any height.
	/// </summary>
	private static MovementBumpSurface SurfaceAhead(Player player, int direction)
	{
		if (direction == 0)
		{
			return MovementBumpSurface.Terrain;
		}

		float probeX = player.Center.X + direction * (player.width * 0.5f + TileSize * 0.5f);
		int tileX = (int)MathF.Floor(probeX / TileSize);
		int topTileY = (int)MathF.Floor(player.position.Y / TileSize);
		int bottomTileY = (int)MathF.Floor((player.position.Y + player.height - 1f) / TileSize);
		for (int tileY = topTileY; tileY <= bottomTileY; tileY++)
		{
			if (!WorldGen.InWorld(tileX, tileY, 1))
			{
				continue;
			}

			Tile tile = Framing.GetTileSafely(tileX, tileY);
			if (tile.HasTile && TileLoader.IsClosedDoor(tile))
			{
				return MovementBumpSurface.Door;
			}
		}

		return MovementBumpSurface.Terrain;
	}

	public override void Unload()
	{
		ResetTracking();
		_sounds?.Dispose();
		_sounds = null;
		if (ReferenceEquals(_instance, this))
		{
			_instance = null;
		}
	}

	/// <summary>
	/// Plays one bump cue. The tone stays centered like a footstep so it is heard
	/// from the player rather than from the surface; vertical contact is pitched so
	/// a floor or ceiling still reads apart from a wall.
	/// </summary>
	internal static void Play(
		int verticalDirection,
		float volumeScale = 1f,
		MovementBumpSurface surface = MovementBumpSurface.Terrain)
	{
		if (_instance is not MovementBumpSystem instance ||
			instance._sounds is not WallBumpSoundBank sounds)
		{
			return;
		}

		AriadneClientConfig config = ModContent.GetInstance<AriadneClientConfig>();
		if (!config.MovementBumpTonesEnabled ||
			config.MovementBumpVolumePercent <= 0 ||
			!GameplayAudioGate.CanListen())
		{
			return;
		}

		sounds.Play(
			config.MovementBumpVolumePercent / 100f * Math.Clamp(volumeScale, 0f, 1f),
			-Math.Sign(verticalDirection) * VerticalPitch,
			surface);
	}

	private bool CanTrackLocalPlayer()
	{
		if (!GameplayAudioGate.CanListen())
		{
			return false;
		}

		// Mounts, ropes, grapples, seats, and holds move the player under rules that
		// do not answer to the walk controls, so contact there is not a wall bump.
		Player player = Main.LocalPlayer;
		return !player.mount.Active &&
			!player.pulley &&
			!player.isLockedToATile &&
			!player.frozen &&
			!player.stoned &&
			!player.webbed &&
			!player.tongued &&
			!_wasGrappling;
	}

	private void ResetTracking()
	{
		_previousPositionX = 0f;
		_isTracking = false;
		_wasGrappling = false;
		_cadence.Reset();
	}
}
