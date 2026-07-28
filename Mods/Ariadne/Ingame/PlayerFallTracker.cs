#nullable enable

using System;
using Terraria;
using Terraria.ModLoader;
using Ariadne.Ingame.Freecam;

namespace Ariadne.Ingame;

/// <summary>
/// Watches the local player's vertical contact so the falling drone and the impact
/// cue agree on when a descent is underway and how far it ran. Distance is measured
/// here rather than read from <c>Player.fallStart</c> because vanilla resets that
/// field while applying fall damage, which happens before this observation point.
/// </summary>
[Autoload(Side = ModSide.Client)]
internal sealed class PlayerFallTracker : ModSystem
{
	// A hop or a one-tile step down should stay silent, so a descent has to persist
	// before it counts. A genuine fall still reports almost at once, because the
	// ground is near and the drone opens loud and high the moment it does.
	private const int DescentGraceTicks = 9;
	private const float DescentSpeedThreshold = 0.35f;
	private const float TileSize = 16f;
	private const float TeleportThresholdPixels = 160f;

	private static PlayerFallTracker? _instance;

	private float _previousDescentVelocity;
	private float _fallStartPositionY;
	private int _descendingTicks;
	private bool _isTracking;
	private bool _hasFallOrigin;
	private bool _justLanded;
	private bool _justStruckCeiling;
	private float _landingFallTiles;

	/// <summary>Whether a descent has lasted long enough to be worth sounding.</summary>
	internal static bool IsDescending => _instance is { } tracker &&
		tracker._descendingTicks >= DescentGraceTicks;

	/// <summary>True for the single tick a tracked descent reaches the ground.</summary>
	internal static bool JustLanded => _instance?._justLanded == true;

	/// <summary>True for the single tick upward movement is stopped by a ceiling.</summary>
	internal static bool JustStruckCeiling => _instance?._justStruckCeiling == true;

	/// <summary>How far the descent that just landed actually fell, in tiles.</summary>
	internal static float LandingFallTiles => _instance?._landingFallTiles ?? 0f;

	public override void Load()
	{
		_instance = this;
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
		_justLanded = false;
		_justStruckCeiling = false;
		if (FreecamSystem.IsActive || !CanTrackLocalPlayer())
		{
			ResetTracking();
			return;
		}

		Player player = Main.LocalPlayer;
		float gravityDirection = player.gravDir < 0f ? -1f : 1f;
		// Reverse gravity flips which way "down" runs, so both the velocity and the
		// distance are expressed along the player's own gravity instead of world Y.
		float descentVelocity = player.velocity.Y * gravityDirection;
		float positionY = player.position.Y;
		if (!_isTracking)
		{
			_previousDescentVelocity = descentVelocity;
			_isTracking = true;
			return;
		}

		if (MathF.Abs(positionY - _fallStartPositionY) > TeleportThresholdPixels && !_hasFallOrigin)
		{
			_fallStartPositionY = positionY;
		}

		bool airborne = descentVelocity != 0f;
		if (descentVelocity > DescentSpeedThreshold)
		{
			if (!_hasFallOrigin)
			{
				_fallStartPositionY = positionY;
				_hasFallOrigin = true;
			}
			_descendingTicks++;
		}
		else if (!airborne || descentVelocity < 0f)
		{
			_descendingTicks = 0;
		}

		if (!airborne)
		{
			// Terraria zeroes the axis on contact, so the sign carried in from the
			// previous tick is what separates a landing from a struck ceiling.
			if (_previousDescentVelocity > 0f)
			{
				_landingFallTiles = _hasFallOrigin
					? MathF.Max(0f, (positionY - _fallStartPositionY) * gravityDirection / TileSize)
					: 0f;
				_justLanded = _descendingTicks >= DescentGraceTicks;
			}
			else if (_previousDescentVelocity < 0f)
			{
				_justStruckCeiling = true;
			}

			_descendingTicks = 0;
			_hasFallOrigin = false;
		}

		_previousDescentVelocity = descentVelocity;
	}

	public override void Unload()
	{
		ResetTracking();
		if (ReferenceEquals(_instance, this))
		{
			_instance = null;
		}
	}

	private static bool CanTrackLocalPlayer()
	{
		// Mounts, ropes, pulleys, seats and holds move the player under rules that do
		// not answer to gravity, so their vertical motion is not a fall.
		Player player = Main.LocalPlayer;
		return !player.dead &&
			!player.mount.Active &&
			!player.pulley &&
			!player.isLockedToATile &&
			!player.frozen &&
			!player.stoned &&
			!player.tongued &&
			player.grappling[0] < 0;
	}

	private void ResetTracking()
	{
		_previousDescentVelocity = 0f;
		_fallStartPositionY = 0f;
		_descendingTicks = 0;
		_isTracking = false;
		_hasFallOrigin = false;
		_justLanded = false;
		_justStruckCeiling = false;
		_landingFallTiles = 0f;
	}
}
