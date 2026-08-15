#nullable enable

using System;
using Microsoft.Xna.Framework;
using Terraria;
using Ariadne.Logic;

namespace Ariadne.Ingame.WallTones;

internal static class WallToneTerrainProbe
{
	private const int ProbeCount = 7;
	private const float TileSize = 16f;
	private const float MarchStepPixels = 2f;
	private const float FirstProbeOffsetPixels = 0.25f;
	private const int RefinementSteps = 6;
	// A flat surface answers every vertical probe at the same range, so the winner
	// would otherwise be whichever probe the loop reached first - always the leftmost.
	private const float VerticalTieTolerancePixels = MarchStepPixels;

	internal static WallToneSnapshot Sample(
		SpatialObserverSnapshot observer,
		int rangeTiles)
	{
		float maximumDistance = Math.Clamp(rangeTiles, 4, 30) * TileSize;
		Vector2 halfSize = new(observer.Width * 0.5f, observer.Height * 0.5f);
		float gravityDirection = observer.GravityDirection;

		return new(
			SampleSideRegion(observer, halfSize, -1f, maximumDistance),
			SampleSideRegion(observer, halfSize, 1f, maximumDistance),
			SampleVerticalRegion(observer, halfSize, -gravityDirection, maximumDistance));
	}

	private static WallToneRegionSnapshot SampleSideRegion(
		SpatialObserverSnapshot observer,
		Vector2 playerHalfSize,
		float horizontalDirection,
		float maximumDistance)
	{
		int width = Math.Max(1, (int)MathF.Round(playerHalfSize.X * 2f));
		int height = Math.Max(1, (int)MathF.Round(playerHalfSize.Y * 2f));
		Vector2 position = observer.Center - playerHalfSize;
		Vector2 originalPosition = position;
		float stepSpeed = 0f;
		float gfxOffY = 0f;
		int gravity = observer.GravityDirection < 0f ? -1 : 1;
		for (float travelled = 0f; travelled < maximumDistance; travelled += MarchStepPixels)
		{
			Vector2 velocity = new(horizontalDirection * MarchStepPixels, 0f);
			Collision.StepUp(
				ref position,
				ref velocity,
				width,
				height,
				ref stepSpeed,
				ref gfxOffY,
				gravity,
				holdsMatching: false,
				specialChecksMode: 0);
			Vector2 allowed = Collision.TileCollision(
				position,
				velocity,
				width,
				height,
				fallThrough: false,
				fall2: false,
				gravDir: gravity);
			if (TraversalLogic.IsImpassable(MarchStepPixels, allowed.X))
			{
				float distance = MathF.Abs(position.X - originalPosition.X) + MathF.Abs(allowed.X);
				Vector2 surface = FindBlockingSurfacePoint(
					position,
					width,
					height,
					horizontalDirection,
					allowed.X);
				return new(
					true,
					Math.Clamp(distance, 0f, maximumDistance),
					maximumDistance,
					observer.NormalizeToField(surface));
			}

			position += allowed;
		}

		return WallToneRegionSnapshot.Empty(maximumDistance);
	}

	private static Vector2 FindBlockingSurfacePoint(
		Vector2 bodyPosition,
		int width,
		int height,
		float horizontalDirection,
		float allowedMovement)
	{
		float surfaceX = horizontalDirection > 0f
			? bodyPosition.X + width + allowedMovement
			: bodyPosition.X + allowedMovement;
		float sampleX = surfaceX + horizontalDirection * FirstProbeOffsetPixels;
		float totalY = 0f;
		int hits = 0;
		for (int probeIndex = 0; probeIndex < ProbeCount; probeIndex++)
		{
			float amount = probeIndex / (float)(ProbeCount - 1);
			float y = bodyPosition.Y + FirstProbeOffsetPixels +
				amount * MathF.Max(0f, height - FirstProbeOffsetPixels * 2f);
			if (IsBlockingPoint(new(sampleX, y)))
			{
				totalY += y;
				hits++;
			}
		}

		return new(surfaceX, hits > 0 ? totalY / hits : bodyPosition.Y + height * 0.5f);
	}

	/// <summary>
	/// Samples the surface above or below the body. Unlike a side, a single block
	/// overhead already blocks upward movement and a single block underfoot already
	/// ends a fall, so the nearest hit wins outright with no alignment requirement.
	/// Demanding agreement here silenced both a lone block and two blocks at
	/// different heights, which is the whole obstacle in either case.
	/// </summary>
	private static WallToneRegionSnapshot SampleVerticalRegion(
		SpatialObserverSnapshot observer,
		Vector2 playerHalfSize,
		float verticalDirection,
		float maximumDistance)
	{
		Vector2 playerCenter = observer.Center;
		Vector2 direction = new(0f, verticalDirection);
		// Only terrain overlapping the body's collision footprint can stop vertical
		// movement. The former three-tile minimum reached well past both shoulders,
		// so a block two tiles diagonally away was reported as a ceiling. Pulling the
		// outer samples just inside the hitbox also avoids assigning a boundary point
		// to the neighbouring tile while retaining several samples across a normal
		// player's width for lone blocks and uneven ceilings.
		float horizontalSpan = MathF.Max(0f, playerHalfSize.X - FirstProbeOffsetPixels);
		float nearestDistance = float.PositiveInfinity;
		float nearestLateralOffset = float.PositiveInfinity;
		Vector2 nearestPoint = Vector2.Zero;

		for (int probeIndex = 0; probeIndex < ProbeCount; probeIndex++)
		{
			float horizontalAmount = probeIndex / (float)(ProbeCount - 1) * 2f - 1f;
			Vector2 origin = playerCenter + new Vector2(
				horizontalAmount * horizontalSpan,
				verticalDirection * playerHalfSize.Y);
			if (!TryRaycast(origin, direction, maximumDistance, out float distance, out Vector2 hitPoint))
			{
				continue;
			}

			float lateralOffset = MathF.Abs(horizontalAmount) * horizontalSpan;
			bool isCloser = distance < nearestDistance - VerticalTieTolerancePixels;
			bool isTiedButMoreCentered = distance < nearestDistance + VerticalTieTolerancePixels &&
				lateralOffset < nearestLateralOffset;
			if (!isCloser && !isTiedButMoreCentered)
			{
				continue;
			}

			nearestDistance = MathF.Min(distance, nearestDistance);
			nearestLateralOffset = lateralOffset;
			nearestPoint = hitPoint;
		}

		if (!float.IsFinite(nearestDistance))
		{
			return WallToneRegionSnapshot.Empty(maximumDistance);
		}

		return new(
			true,
			nearestDistance,
			maximumDistance,
			observer.NormalizeToField(nearestPoint));
	}

	private static bool TryRaycast(
		Vector2 origin,
		Vector2 direction,
		float maximumDistance,
		out float hitDistance,
		out Vector2 hitPoint)
	{
		float previousDistance = 0f;
		for (float distance = FirstProbeOffsetPixels; distance <= maximumDistance; distance += MarchStepPixels)
		{
			Vector2 point = origin + direction * distance;
			if (!IsBlockingPoint(point))
			{
				previousDistance = distance;
				continue;
			}

			float low = previousDistance;
			float high = distance;
			for (int step = 0; step < RefinementSteps; step++)
			{
				float middle = (low + high) * 0.5f;
				if (IsBlockingPoint(origin + direction * middle))
				{
					high = middle;
				}
				else
				{
					low = middle;
				}
			}

			hitDistance = high;
			hitPoint = origin + direction * high;
			return true;
		}

		hitDistance = maximumDistance;
		hitPoint = origin + direction * maximumDistance;
		return false;
	}

	private static bool IsBlockingPoint(Vector2 point)
	{
		int tileX = (int)MathF.Floor(point.X / TileSize);
		int tileY = (int)MathF.Floor(point.Y / TileSize);
		if (!WorldGen.InWorld(tileX, tileY, 1))
		{
			return true;
		}

		return Collision.IsWorldPointSolid(point, treatPlatformsAsNonSolid: true);
	}
}
