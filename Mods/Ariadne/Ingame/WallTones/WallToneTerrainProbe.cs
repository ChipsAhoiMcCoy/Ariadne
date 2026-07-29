#nullable enable

using System;
using Microsoft.Xna.Framework;
using Terraria;

namespace Ariadne.Ingame.WallTones;

internal static class WallToneTerrainProbe
{
	private const int ProbeCount = 7;
	// Four aligned body-height samples distinguish a movement-blocking side from
	// one-tile step-up terrain without requiring the surface to rise above the player.
	private const int MinimumAlignedProbeCount = 4;
	private const float TileSize = 16f;
	private const float MarchStepPixels = 2f;
	private const float FirstProbeOffsetPixels = 0.25f;
	private const int RefinementSteps = 6;
	private const float BodyProbeSpan = 0.85f;
	// Step-up terrain is rejected because its upper probes miss the surface entirely,
	// not because the hits disagree, so this tolerance only has to admit an uneven
	// face. Half a tile keeps rough cave walls audible without merging separate ledges.
	private const float SurfaceAlignmentTolerancePixels = TileSize * 0.5f;
	// A flat surface answers every vertical probe at the same range, so the winner
	// would otherwise be whichever probe the loop reached first - always the leftmost.
	private const float VerticalTieTolerancePixels = MarchStepPixels;

	internal static WallToneSnapshot Sample(
		SpatialObserverSnapshot observer,
		int rangeTiles,
		bool includeFloor)
	{
		float maximumDistance = Math.Clamp(rangeTiles, 4, 30) * TileSize;
		Vector2 halfSize = new(observer.Width * 0.5f, observer.Height * 0.5f);
		float gravityDirection = observer.GravityDirection;

		return new(
			SampleSideRegion(observer, halfSize, -1f, maximumDistance),
			SampleSideRegion(observer, halfSize, 1f, maximumDistance),
			SampleVerticalRegion(observer, halfSize, -gravityDirection, maximumDistance),
			includeFloor
				? SampleVerticalRegion(observer, halfSize, gravityDirection, maximumDistance)
				: WallToneRegionSnapshot.Empty(maximumDistance));
	}

	private static WallToneRegionSnapshot SampleSideRegion(
		SpatialObserverSnapshot observer,
		Vector2 playerHalfSize,
		float horizontalDirection,
		float maximumDistance)
	{
		Vector2 playerCenter = observer.Center;
		Span<float> distances = stackalloc float[ProbeCount];
		Span<Vector2> hitPoints = stackalloc Vector2[ProbeCount];
		Span<bool> hits = stackalloc bool[ProbeCount];
		hits.Clear();
		Vector2 direction = new(horizontalDirection, 0f);

		for (int probeIndex = 0; probeIndex < ProbeCount; probeIndex++)
		{
			float verticalAmount = probeIndex / (float)(ProbeCount - 1) * 2f - 1f;
			Vector2 origin = playerCenter + new Vector2(
				horizontalDirection * playerHalfSize.X,
				verticalAmount * playerHalfSize.Y * BodyProbeSpan);
			if (!TryRaycast(origin, direction, maximumDistance, out float distance, out Vector2 hitPoint))
			{
				distances[probeIndex] = maximumDistance;
				continue;
			}

			hits[probeIndex] = true;
			distances[probeIndex] = distance;
			hitPoints[probeIndex] = hitPoint;
		}

		Span<bool> alignedHits = stackalloc bool[ProbeCount];
		if (!TrySelectAlignedSurface(hits, distances, alignedHits, out _))
		{
			return WallToneRegionSnapshot.Empty(maximumDistance);
		}

		return CreateSnapshot(
			observer,
			maximumDistance,
			alignedHits,
			distances,
			hitPoints);
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
		float horizontalSpan = MathF.Max(playerHalfSize.X * BodyProbeSpan, TileSize * 1.5f);
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

	private static bool TrySelectAlignedSurface(
		ReadOnlySpan<bool> hits,
		ReadOnlySpan<float> distances,
		Span<bool> alignedHits,
		out float surfaceDistance)
	{
		alignedHits.Clear();
		int bestCount = 0;
		float bestCandidate = float.PositiveInfinity;
		for (int candidateIndex = 0; candidateIndex < ProbeCount; candidateIndex++)
		{
			if (!hits[candidateIndex])
			{
				continue;
			}

			float candidate = distances[candidateIndex];
			int alignedCount = 0;
			for (int probeIndex = 0; probeIndex < ProbeCount; probeIndex++)
			{
				if (hits[probeIndex] &&
					MathF.Abs(distances[probeIndex] - candidate) <= SurfaceAlignmentTolerancePixels)
				{
					alignedCount++;
				}
			}

			if (alignedCount > bestCount ||
				(alignedCount == bestCount && candidate < bestCandidate))
			{
				bestCount = alignedCount;
				bestCandidate = candidate;
			}
		}

		if (bestCount < MinimumAlignedProbeCount)
		{
			surfaceDistance = 0f;
			return false;
		}

		float alignedDistanceTotal = 0f;
		int selectedCount = 0;
		for (int probeIndex = 0; probeIndex < ProbeCount; probeIndex++)
		{
			bool isAligned = hits[probeIndex] &&
				MathF.Abs(distances[probeIndex] - bestCandidate) <= SurfaceAlignmentTolerancePixels;
			alignedHits[probeIndex] = isAligned;
			if (isAligned)
			{
				alignedDistanceTotal += distances[probeIndex];
				selectedCount++;
			}
		}

		surfaceDistance = alignedDistanceTotal / selectedCount;
		return true;
	}

	private static WallToneRegionSnapshot CreateSnapshot(
		SpatialObserverSnapshot observer,
		float maximumDistance,
		ReadOnlySpan<bool> alignedHits,
		ReadOnlySpan<float> distances,
		ReadOnlySpan<Vector2> hitPoints)
	{
		float weightedDistance = 0f;
		Vector2 weightedCentroid = Vector2.Zero;
		float totalWeight = 0f;
		for (int probeIndex = 0; probeIndex < ProbeCount; probeIndex++)
		{
			if (!alignedHits[probeIndex])
			{
				continue;
			}

			float proximity = 1f - distances[probeIndex] / maximumDistance;
			float weight = 0.05f + 4f * proximity * proximity;
			weightedDistance += distances[probeIndex] * weight;
			weightedCentroid += hitPoints[probeIndex] * weight;
			totalWeight += weight;
		}

		Vector2 centroid = weightedCentroid / totalWeight;
		return new(
			true,
			weightedDistance / totalWeight,
			maximumDistance,
			observer.NormalizeToField(centroid));
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
