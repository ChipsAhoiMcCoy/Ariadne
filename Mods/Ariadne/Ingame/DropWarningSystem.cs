#nullable enable

using System;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.GameInput;
using Terraria.ID;
using Terraria.ModLoader;
using Ariadne.Audio;
using Ariadne.Configs;
using Ariadne.Ingame.Teleport;
using Ariadne.Logic;

namespace Ariadne.Ingame;

internal enum DropAssessmentKind
{
	None,
	Safe,
	Unsafe,
}

internal readonly record struct DropAssessment(DropAssessmentKind Kind, Point Edge)
{
	internal static DropAssessment None => new(DropAssessmentKind.None, Point.Zero);
}

[Autoload(Side = ModSide.Client)]
internal sealed class DropWarningSystem : ModSystem
{
	private const float TileSize = 16f;
	private const float ScanStepPixels = 2f;
	private const float SupportProbePixels = 3f;

	private NavigationCueSoundBank? _sounds;
	private DropAssessment _latchedAssessment;
	private int _latchedDirection;

	public override void Load()
	{
		_sounds = NavigationCueSoundBank.Create(Mod);
	}

	public override void OnWorldLoad() => ResetTracking();

	public override void OnWorldUnload() => ResetTracking();

	public override void PostUpdatePlayers()
	{
		AriadneClientConfig config = ModContent.GetInstance<AriadneClientConfig>();
		if (!config.DropWarningsEnabled ||
			config.DropWarningVolumePercent <= 0 ||
			!GameplayAudioGate.CanListen())
		{
			ResetTracking();
			return;
		}

		Player player = Main.LocalPlayer;
		int direction = ReadHorizontalIntent();
		if (direction == 0)
		{
			return;
		}

		if (!CanProbeFrom(player))
		{
			ResetTracking();
			return;
		}

		DropAssessment assessment = AssessForwardDrop(
			player,
			direction,
			config.DropDetectionRangeTiles,
			config.DropWarningLookaheadTiles);
		if (assessment.Kind == DropAssessmentKind.None)
		{
			// Supported terrain, a short step-down, or a blocking wall means the old
			// edge has been left and may sound again if approached later.
			ResetTracking();
			return;
		}

		bool sameEdge = assessment.Edge == _latchedAssessment.Edge &&
			direction == _latchedDirection;
		if (sameEdge && assessment.Kind == _latchedAssessment.Kind)
		{
			return;
		}

		_latchedAssessment = assessment;
		_latchedDirection = direction;
		_sounds?.PlaySpatial(
			assessment.Kind == DropAssessmentKind.Safe
				? NavigationCueKind.SafeDrop
				: NavigationCueKind.UnsafeDrop,
			new Vector2(assessment.Edge.X * TileSize + TileSize * 0.5f, assessment.Edge.Y * TileSize + TileSize * 0.5f),
			config.DropWarningVolumePercent / 100f,
			config);
	}

	public override void Unload()
	{
		_sounds?.Dispose();
		_sounds = null;
		ResetTracking();
	}

	internal static DropAssessment AssessForwardDrop(
		Player player,
		int horizontalDirection,
		int rangeTiles,
		int lookaheadTiles)
	{
		float gravityDirection = player.gravDir < 0f ? -1f : 1f;
		if (!TryFindReachableEdge(
			player,
			horizontalDirection,
			TraversalLogic.ClampLookahead(lookaheadTiles),
			gravityDirection,
			out Vector2 probePosition))
		{
			return DropAssessment.None;
		}

		Point edge = new(
			(int)MathF.Floor((horizontalDirection > 0
				? probePosition.X
				: probePosition.X + player.width - 1f) / TileSize),
			(int)MathF.Floor((gravityDirection > 0
				? probePosition.Y + player.height
				: probePosition.Y - 1f) / TileSize));

		if (!IsPlayerBoxInsideWorld(player, probePosition) ||
			Collision.SolidCollision(probePosition, player.width, player.height))
		{
			return DropAssessment.None;
		}

		float maximumDistance = Math.Clamp(rangeTiles, 3, 60) * TileSize;
		bool encounteredHazard = false;
		bool enteredNonHazardousLiquid = false;
		for (float distance = 0f; distance <= maximumDistance; distance += ScanStepPixels)
		{
			Vector2 candidate = probePosition + Vector2.UnitY * (distance * gravityDirection);
			if (!IsPlayerBoxInsideWorld(player, candidate))
			{
				return new(DropAssessmentKind.Unsafe, edge);
			}

			bool lava = Collision.LavaCollision(candidate, player.width, player.height);
			bool shimmer = SafeLandingProbe.ContainsShimmer(candidate, player.width, player.height);
			bool hurting = ContainsHurtingTileAtContact(player, candidate, gravityDirection);
			encounteredHazard |= lava || shimmer || hurting;
			if (!lava && !shimmer && Collision.WetCollision(candidate, player.width, player.height))
			{
				enteredNonHazardousLiquid = true;
			}

			if (!TryGetGravitySupport(
				player,
				candidate,
				gravityDirection,
				out Vector2 landingPosition))
			{
				continue;
			}

			// TileCollision can see support up to SupportProbePixels before the player
			// actually reaches it. Include the allowed part of that probe so an exact
			// three-tile landing is classified as three tiles, not 2.875 tiles.
			float landingDistance = distance + MathF.Abs(landingPosition.Y - candidate.Y);
			float dropTiles = landingDistance / TileSize;
			if (TraversalLogic.AssessDrop(dropTiles, landingFound: true, hazardous: false, fallDamageSafe: true) == 0)
			{
				return DropAssessment.None;
			}

			bool landingHazard = Collision.LavaCollision(landingPosition, player.width, player.height) ||
				SafeLandingProbe.ContainsShimmer(landingPosition, player.width, player.height) ||
				ContainsHurtingTileAtContact(player, landingPosition, gravityDirection);
			bool safeLanding = !encounteredHazard &&
				!landingHazard &&
				!Collision.SolidCollision(landingPosition, player.width, player.height);
			bool fallDamageSafe = !player.stoned &&
				(enteredNonHazardousLiquid ||
					player.noFallDmg ||
					player.equippedWings is not null ||
					LandingNegatesFallDamage(player, landingPosition, gravityDirection) ||
					dropTiles <= 25 + player.extraFall);
			int result = TraversalLogic.AssessDrop(
				dropTiles,
				landingFound: true,
				hazardous: !safeLanding,
				fallDamageSafe);
			return new(result == 1 ? DropAssessmentKind.Safe : DropAssessmentKind.Unsafe, edge);
		}

		return new(DropAssessmentKind.Unsafe, edge);
	}

	private static bool TryFindReachableEdge(
		Player player,
		int horizontalDirection,
		int lookaheadTiles,
		float gravityDirection,
		out Vector2 edgePosition)
	{
		edgePosition = player.position;
		Vector2 position = player.position;
		float stepSpeed = 0f;
		float gfxOffY = 0f;
		int gravity = gravityDirection < 0f ? -1 : 1;
		float maximumDistance = lookaheadTiles * TileSize;
		for (float travelled = 0f; travelled < maximumDistance; travelled += ScanStepPixels)
		{
			Vector2 velocity = new(horizontalDirection * ScanStepPixels, 0f);
			Collision.StepUp(
				ref position,
				ref velocity,
				player.width,
				player.height,
				ref stepSpeed,
				ref gfxOffY,
				gravity,
				holdsMatching: false,
				specialChecksMode: 0);
			Vector2 allowed = Collision.TileCollision(
				position,
				velocity,
				player.width,
				player.height,
				fallThrough: false,
				fall2: false,
				gravDir: gravity);
			if (TraversalLogic.IsImpassable(ScanStepPixels, allowed.X))
			{
				return false;
			}

			position += allowed;
			if (!HasLeadingEdgeSupport(player, position, horizontalDirection, gravityDirection))
			{
				edgePosition = position + new Vector2(
					horizontalDirection * player.width,
					0f);
				return true;
			}
		}

		return false;
	}

	private static bool HasLeadingEdgeSupport(
		Player player,
		Vector2 position,
		int horizontalDirection,
		float gravityDirection)
	{
		const int probeWidth = 2;
		Vector2 probePosition = new(
			horizontalDirection > 0 ? position.X + player.width - probeWidth : position.X,
			position.Y);
		Vector2 wanted = Vector2.UnitY * (SupportProbePixels * gravityDirection);
		Vector2 allowed = Collision.TileCollision(
			probePosition,
			wanted,
			probeWidth,
			player.height,
			fallThrough: false,
			fall2: false,
			gravDir: gravityDirection < 0f ? -1 : 1);
		return MathF.Abs(allowed.Y) < MathF.Abs(wanted.Y) - 0.01f;
	}

	private static int ReadHorizontalIntent()
	{
		bool left = PlayerInput.Triggers.Current.Left;
		bool right = PlayerInput.Triggers.Current.Right;
		return left == right ? 0 : right ? 1 : -1;
	}

	private static bool CanProbeFrom(Player player)
	{
		if (player.mount.Active || player.pulley || player.grappling[0] >= 0)
		{
			return false;
		}

		float gravityDirection = player.gravDir < 0f ? -1f : 1f;
		return MathF.Abs(player.velocity.Y) < 0.01f &&
			HasGravitySupport(player, player.position, gravityDirection);
	}

	private static bool HasGravitySupport(
		Player player,
		Vector2 position,
		float gravityDirection)
	{
		return TryGetGravitySupport(player, position, gravityDirection, out _);
	}

	private static bool TryGetGravitySupport(
		Player player,
		Vector2 position,
		float gravityDirection,
		out Vector2 landingPosition)
	{
		Vector2 wanted = Vector2.UnitY * (SupportProbePixels * gravityDirection);
		Vector2 allowed = Collision.TileCollision(
			position,
			wanted,
			player.width,
			player.height,
			fallThrough: false,
			fall2: false,
			gravDir: gravityDirection < 0f ? -1 : 1);
		landingPosition = position + allowed;
		return MathF.Abs(allowed.Y) < MathF.Abs(wanted.Y) - 0.01f;
	}

	private static bool ContainsHurtingTileAtContact(
		Player player,
		Vector2 position,
		float gravityDirection)
	{
		Vector2 hazardPosition = gravityDirection > 0f
			? position
			: position - new Vector2(0f, 2f);
		return Collision.AnyHurtingTiles(hazardPosition, player.width, player.height + 2);
	}

	private static bool LandingNegatesFallDamage(
		Player player,
		Vector2 position,
		float gravityDirection)
	{
		int firstX = Math.Clamp((int)MathF.Floor(position.X / TileSize), 0, Main.maxTilesX - 1);
		int lastX = Math.Clamp(
			(int)MathF.Floor((position.X + player.width) / TileSize),
			0,
			Main.maxTilesX - 1);
		int supportY = Math.Clamp(
			(int)MathF.Floor((gravityDirection > 0f
				? position.Y + player.height + 1f
				: position.Y - 1f) / TileSize),
			0,
			Main.maxTilesY - 1);
		for (int x = firstX; x <= lastX; x++)
		{
			Tile tile = Main.tile[x, supportY];
			if (tile.HasTile && TileID.Sets.NegatesFallDamage[tile.TileType])
			{
				return true;
			}
		}

		return false;
	}

	private static bool IsPlayerBoxInsideWorld(Player player, Vector2 position)
	{
		return position.X >= TileSize &&
			position.Y >= TileSize &&
			position.X + player.width < Main.maxTilesX * TileSize - TileSize &&
			position.Y + player.height < Main.maxTilesY * TileSize - TileSize;
	}

	private void ResetTracking()
	{
		_latchedAssessment = DropAssessment.None;
		_latchedDirection = 0;
	}
}
