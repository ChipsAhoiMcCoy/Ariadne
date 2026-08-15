#nullable enable

using System;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;
using Ariadne.Audio;
using Ariadne.Configs;
using Ariadne.Logic;

namespace Ariadne.Ingame;

internal enum TraversalLandmarkKind
{
	Platform,
	MinecartTrack,
	Rope,
}

internal enum TraversalLandmarkDirection
{
	Above,
	Below,
}

internal readonly record struct TraversalLandmark(
	TraversalLandmarkKind Kind,
	TraversalLandmarkDirection Direction,
	Point Tile,
	int DistanceTiles)
{
	internal Vector2 WorldPosition => new(Tile.X * 16f + 8f, Tile.Y * 16f + 8f);
}

[Autoload(Side = ModSide.Client)]
internal sealed class TraversalLandmarkCueSystem : ModSystem
{
	private const int PlatformRangeTiles = 4;
	private const int MinecartRangeTiles = 4;
	private const int RopeRangeTiles = 7;

	private NavigationCueSoundBank? _sounds;
	private float _lastFootX = float.NaN;
	private TraversalLandmark? _spokenRun;

	public override void Load() => _sounds = NavigationCueSoundBank.Create(Mod);

	public override void OnWorldLoad() => ResetTracking();

	public override void OnWorldUnload() => ResetTracking();

	public override void PostUpdatePlayers()
	{
		AriadneClientConfig config = ModContent.GetInstance<AriadneClientConfig>();
		Player player = Main.LocalPlayer;
		if (!config.TraversalLandmarkCuesEnabled ||
			config.TraversalLandmarkCueVolumePercent <= 0 ||
			!GameplayAudioGate.CanListen() ||
			!CanSample(player))
		{
			ResetTracking();
			return;
		}

		float footX = player.Hitbox.Center.X;
		int tileX = Math.Clamp((int)(footX / 16f), 1, Main.maxTilesX - 2);
		float tileCenter = tileX * 16f + 8f;
		bool crossed = !float.IsNaN(_lastFootX) && _lastFootX != footX &&
			(_lastFootX < tileCenter && footX >= tileCenter ||
			 _lastFootX > tileCenter && footX <= tileCenter);
		_lastFootX = footX;
		if (!crossed)
		{
			return;
		}

		TraversalLandmark? landmark = Scan(player, tileX);
		if (landmark is null)
		{
			_spokenRun = null;
			return;
		}

		TraversalLandmark current = landmark.Value;
		_sounds?.PlaySpatial(
			CueKind(current.Kind),
			current.WorldPosition,
			config.TraversalLandmarkCueVolumePercent / 100f,
			config);

		bool shouldSpeak = TraversalLogic.ShouldSpeakLandmark(
			_spokenRun is TraversalLandmark previous ? (int)previous.Kind : null,
			(int)current.Kind,
			_spokenRun is TraversalLandmark prior ? (int)prior.Direction : 0,
			(int)current.Direction);
		if (shouldSpeak)
		{
			AriadneMod.ScreenReader.Output(Describe(current), interrupt: false);
		}
		_spokenRun = current;
	}

	public override void Unload()
	{
		_sounds?.Dispose();
		_sounds = null;
		ResetTracking();
	}

	internal static TraversalLandmark? Scan(Player player, int tileX)
	{
		int gravity = player.gravDir < 0f ? -1 : 1;
		int headSideY = gravity > 0 ? player.Hitbox.Top / 16 : player.Hitbox.Bottom / 16;
		int footSideY = gravity > 0 ? player.Hitbox.Bottom / 16 : player.Hitbox.Top / 16;
		TraversalLandmark? best = null;

		for (int distance = 1; distance <= RopeRangeTiles; distance++)
		{
			int tileY = headSideY - gravity * distance;
			if (!WorldGen.InWorld(tileX, tileY, 1))
			{
				break;
			}

			Tile tile = Framing.GetTileSafely(tileX, tileY);
			if (tile.HasTile && !tile.IsActuated)
			{
				Consider(ref best, tile, tileX, tileY, distance, TraversalLandmarkDirection.Above);
				if (IsBlocking(tile))
				{
					break;
				}
			}
			if (best is TraversalLandmark found && found.DistanceTiles == distance)
			{
				break;
			}
		}

		for (int distance = 1; distance <= RopeRangeTiles; distance++)
		{
			if (best is TraversalLandmark found && found.DistanceTiles < distance)
			{
				break;
			}
			int tileY = footSideY + gravity * distance;
			if (!WorldGen.InWorld(tileX, tileY, 1))
			{
				break;
			}

			Tile tile = Framing.GetTileSafely(tileX, tileY);
			if (!tile.HasTile || tile.IsActuated)
			{
				continue;
			}
			if (Main.tileRope[tile.TileType])
			{
				TraversalLandmark candidate = new(
					TraversalLandmarkKind.Rope,
					TraversalLandmarkDirection.Below,
					new(tileX, tileY),
					distance);
				if (IsBetter(candidate, best)) best = candidate;
			}
			if (IsBlocking(tile))
			{
				break;
			}
		}

		return best;
	}

	private static void Consider(
		ref TraversalLandmark? best,
		Tile tile,
		int x,
		int y,
		int distance,
		TraversalLandmarkDirection direction)
	{
		TraversalLandmarkKind? kind = Main.tileRope[tile.TileType]
			? TraversalLandmarkKind.Rope
			: distance <= MinecartRangeTiles && tile.TileType == TileID.MinecartTrack
				? TraversalLandmarkKind.MinecartTrack
				: distance <= PlatformRangeTiles && TileID.Sets.Platforms[tile.TileType]
					? TraversalLandmarkKind.Platform
					: null;
		if (kind is null)
		{
			return;
		}

		TraversalLandmark candidate = new(kind.Value, direction, new(x, y), distance);
		if (IsBetter(candidate, best)) best = candidate;
	}

	private static bool IsBetter(TraversalLandmark candidate, TraversalLandmark? current)
	{
		return current is null ||
			candidate.DistanceTiles < current.Value.DistanceTiles ||
			candidate.DistanceTiles == current.Value.DistanceTiles && Priority(candidate.Kind) > Priority(current.Value.Kind);
	}

	private static int Priority(TraversalLandmarkKind kind) =>
		TraversalLogic.LandmarkPriority((int)kind);

	private static bool IsBlocking(Tile tile) =>
		tile.HasTile && !tile.IsActuated &&
		Main.tileSolid[tile.TileType] && !Main.tileSolidTop[tile.TileType] &&
		!Main.tileRope[tile.TileType];

	private static NavigationCueKind CueKind(TraversalLandmarkKind kind) => kind switch
	{
		TraversalLandmarkKind.Rope => NavigationCueKind.Rope,
		TraversalLandmarkKind.MinecartTrack => NavigationCueKind.MinecartTrack,
		_ => NavigationCueKind.Platform,
	};

	private static string Describe(in TraversalLandmark landmark)
	{
		string name = Language.GetTextValue(landmark.Kind switch
		{
			TraversalLandmarkKind.Rope => "Mods.Ariadne.TraversalLandmarks.Rope",
			TraversalLandmarkKind.MinecartTrack => "Mods.Ariadne.TraversalLandmarks.MinecartTrack",
			_ => "Mods.Ariadne.TraversalLandmarks.Platform",
		});
		string direction = Language.GetTextValue(landmark.Direction == TraversalLandmarkDirection.Above
			? "Mods.Ariadne.TraversalLandmarks.Above"
			: "Mods.Ariadne.TraversalLandmarks.Below");
		return Language.GetTextValue("Mods.Ariadne.TraversalLandmarks.Announcement", name, direction);
	}

	private static bool CanSample(Player player)
	{
		if (!player.active || player.dead || player.ghost || player.mount.Active ||
			MathF.Abs(player.velocity.Y) >= 0.02f || MathF.Abs(player.velocity.X) <= 0.01f)
		{
			return false;
		}

		float gravity = player.gravDir < 0f ? -1f : 1f;
		Vector2 wanted = Vector2.UnitY * (2f * gravity);
		Vector2 allowed = Collision.TileCollision(
			player.position,
			wanted,
			player.width,
			player.height,
			fallThrough: false,
			fall2: false,
			gravDir: gravity < 0f ? -1 : 1);
		return MathF.Abs(allowed.Y) < MathF.Abs(wanted.Y) - 0.01f;
	}

	private void ResetTracking()
	{
		_lastFootX = float.NaN;
		_spokenRun = null;
	}
}
