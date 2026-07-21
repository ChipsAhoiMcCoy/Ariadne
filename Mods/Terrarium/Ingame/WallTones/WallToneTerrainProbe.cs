#nullable enable

using System;
using Microsoft.Xna.Framework;
using Terraria;

namespace Terrarium.Ingame.WallTones;

internal static class WallToneTerrainProbe
{
	private const int ProbeCount = 7;
	private const int MinimumAlignedProbeCount = 4;
	// Require a side surface to continue through three tile-spaced samples above the player.
	private const int JumpClearanceProbeCount = 3;
	private const float TileSize = 16f;
	private const float MarchStepPixels = 2f;
	private const float FirstProbeOffsetPixels = 0.25f;
	private const int RefinementSteps = 6;
	private const float BodyProbeSpan = 0.85f;
	private const float SurfaceAlignmentTolerancePixels = TileSize * 1.25f;

	internal static WallToneSnapshot Sample(Player player, int rangeTiles)
	{
		float maximumDistance = Math.Clamp(rangeTiles, 4, 30) * TileSize;
		Vector2 center = player.Center;
		Vector2 halfSize = new(player.width * 0.5f, player.height * 0.5f);
		float gravityDirection = player.gravDir < 0f ? -1f : 1f;

		return new(
			SampleSideRegion(center, halfSize, -1f, gravityDirection, maximumDistance),
			SampleSideRegion(center, halfSize, 1f, gravityDirection, maximumDistance),
			SampleCeilingRegion(center, halfSize, gravityDirection, maximumDistance));
	}

	private static WallToneRegionSnapshot SampleSideRegion(
		Vector2 playerCenter,
		Vector2 playerHalfSize,
		float horizontalDirection,
		float gravityDirection,
		float maximumDistance)
	{
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
		if (!TrySelectAlignedSurface(hits, distances, alignedHits, out float surfaceDistance) ||
			!BlocksJumpClearance(
				playerCenter,
				playerHalfSize,
				horizontalDirection,
				gravityDirection,
				maximumDistance,
				surfaceDistance))
		{
			return WallToneRegionSnapshot.Empty(maximumDistance);
		}

		return CreateSnapshot(
			playerCenter,
			maximumDistance,
			alignedHits,
			distances,
			hitPoints);
	}

	private static WallToneRegionSnapshot SampleCeilingRegion(
		Vector2 playerCenter,
		Vector2 playerHalfSize,
		float gravityDirection,
		float maximumDistance)
	{
		Span<float> distances = stackalloc float[ProbeCount];
		Span<Vector2> hitPoints = stackalloc Vector2[ProbeCount];
		Span<bool> hits = stackalloc bool[ProbeCount];
		hits.Clear();
		Vector2 direction = new(0f, -gravityDirection);
		float horizontalSpan = MathF.Max(playerHalfSize.X * BodyProbeSpan, TileSize * 1.5f);

		for (int probeIndex = 0; probeIndex < ProbeCount; probeIndex++)
		{
			float horizontalAmount = probeIndex / (float)(ProbeCount - 1) * 2f - 1f;
			Vector2 origin = playerCenter + new Vector2(
				horizontalAmount * horizontalSpan,
				-gravityDirection * playerHalfSize.Y);
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
			playerCenter,
			maximumDistance,
			alignedHits,
			distances,
			hitPoints);
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

	private static bool BlocksJumpClearance(
		Vector2 playerCenter,
		Vector2 playerHalfSize,
		float horizontalDirection,
		float gravityDirection,
		float maximumDistance,
		float surfaceDistance)
	{
		Vector2 direction = new(horizontalDirection, 0f);
		for (int probeIndex = 1; probeIndex <= JumpClearanceProbeCount; probeIndex++)
		{
			Vector2 origin = playerCenter + new Vector2(
				horizontalDirection * playerHalfSize.X,
				-gravityDirection * (playerHalfSize.Y + probeIndex * TileSize));
			if (IsBlockingPoint(origin))
			{
				return true;
			}

			if (!TryRaycast(origin, direction, maximumDistance, out float distance, out _))
			{
				return false;
			}

			if (distance <= MarchStepPixels)
			{
				return true;
			}

			if (MathF.Abs(distance - surfaceDistance) > SurfaceAlignmentTolerancePixels)
			{
				return false;
			}
		}

		return true;
	}

	private static WallToneRegionSnapshot CreateSnapshot(
		Vector2 playerCenter,
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
			NormalizeToScanSquare(centroid, playerCenter, maximumDistance));
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

	private static Vector2 NormalizeToScanSquare(
		Vector2 point,
		Vector2 playerCenter,
		float maximumDistance)
	{
		Vector2 offset = point - playerCenter;
		float extent = MathF.Max(1f, maximumDistance);

		return new(
			MathHelper.Clamp(offset.X / extent, -1f, 1f),
			MathHelper.Clamp(offset.Y / extent, -1f, 1f));
	}
}
