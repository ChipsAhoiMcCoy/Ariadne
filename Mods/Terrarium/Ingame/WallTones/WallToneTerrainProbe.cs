#nullable enable

using System;
using Microsoft.Xna.Framework;
using Terraria;

namespace Terrarium.Ingame.WallTones;

internal static class WallToneTerrainProbe
{
	private const int ProbeCount = 7;
	private const float TileSize = 16f;
	private const float MarchStepPixels = 2f;
	private const float FirstProbeOffsetPixels = 0.25f;
	private const int RefinementSteps = 6;
	private const float FanHalfAngleRadians = MathF.PI / 6f;

	internal static WallToneSnapshot Sample(Player player, int rangeTiles)
	{
		float maximumDistance = Math.Clamp(rangeTiles, 4, 30) * TileSize;
		Vector2 center = player.Center;
		Vector2 halfSize = new(player.width * 0.5f, player.height * 0.5f);
		float gravityDirection = player.gravDir < 0f ? -1f : 1f;

		return new(
			SampleRegion(center, halfSize, MathF.PI, maximumDistance),
			SampleRegion(center, halfSize, 0f, maximumDistance),
			SampleRegion(center, halfSize, -MathHelper.PiOver2 * gravityDirection, maximumDistance));
	}

	private static WallToneRegionSnapshot SampleRegion(
		Vector2 playerCenter,
		Vector2 playerHalfSize,
		float centerAngle,
		float maximumDistance)
	{
		Span<float> distances = stackalloc float[ProbeCount];
		Span<Vector2> hitPoints = stackalloc Vector2[ProbeCount];
		Span<bool> hits = stackalloc bool[ProbeCount];

		float weightedDistance = 0f;
		Vector2 weightedCentroid = Vector2.Zero;
		float totalWeight = 0f;
		for (int probeIndex = 0; probeIndex < ProbeCount; probeIndex++)
		{
			float fanAmount = probeIndex / (float)(ProbeCount - 1) * 2f - 1f;
			float angle = centerAngle + fanAmount * FanHalfAngleRadians;
			Vector2 direction = new(MathF.Cos(angle), MathF.Sin(angle));
			Vector2 origin = PlayerBoundaryPoint(playerCenter, playerHalfSize, direction);
			if (!TryRaycast(origin, direction, maximumDistance, out float distance, out Vector2 hitPoint))
			{
				distances[probeIndex] = maximumDistance;
				continue;
			}

			hits[probeIndex] = true;
			distances[probeIndex] = distance;
			hitPoints[probeIndex] = hitPoint;
			float proximity = 1f - distance / maximumDistance;
			float weight = 0.05f + 4f * proximity * proximity;
			weightedDistance += distance * weight;
			weightedCentroid += hitPoint * weight;
			totalWeight += weight;
		}

		if (totalWeight <= 0f)
		{
			return WallToneRegionSnapshot.Empty(maximumDistance);
		}

		float roughness = CalculateRoughness(hits, distances, maximumDistance);
		Vector2 centroid = weightedCentroid / totalWeight;
		return new(
			true,
			weightedDistance / totalWeight,
			maximumDistance,
			NormalizeToVisibleRectangle(centroid, playerCenter),
			roughness);
	}

	private static Vector2 PlayerBoundaryPoint(Vector2 center, Vector2 halfSize, Vector2 direction)
	{
		float horizontalScale = MathF.Abs(direction.X) > 0.0001f
			? halfSize.X / MathF.Abs(direction.X)
			: float.PositiveInfinity;
		float verticalScale = MathF.Abs(direction.Y) > 0.0001f
			? halfSize.Y / MathF.Abs(direction.Y)
			: float.PositiveInfinity;
		return center + direction * MathF.Min(horizontalScale, verticalScale);
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

	private static float CalculateRoughness(
		ReadOnlySpan<bool> hits,
		ReadOnlySpan<float> distances,
		float maximumDistance)
	{
		float total = 0f;
		for (int index = 1; index < ProbeCount; index++)
		{
			if (hits[index] != hits[index - 1])
			{
				total += 1f;
			}
			else if (hits[index])
			{
				float difference = MathF.Abs(distances[index] - distances[index - 1]);
				total += MathHelper.Clamp(difference / (maximumDistance * 0.22f), 0f, 1f);
			}
		}

		return MathHelper.Clamp(total / (ProbeCount - 1), 0f, 1f);
	}

	private static Vector2 NormalizeToVisibleRectangle(Vector2 point, Vector2 playerCenter)
	{
		Vector2 viewportPosition = Main.Camera.ScaledPosition;
		Vector2 viewportSize = Main.Camera.ScaledSize;
		Vector2 offset = point - playerCenter;
		float horizontalExtent = offset.X < 0f
			? MathF.Max(1f, playerCenter.X - viewportPosition.X)
			: MathF.Max(1f, viewportPosition.X + viewportSize.X - playerCenter.X);
		float verticalExtent = offset.Y < 0f
			? MathF.Max(1f, playerCenter.Y - viewportPosition.Y)
			: MathF.Max(1f, viewportPosition.Y + viewportSize.Y - playerCenter.Y);

		return new(
			MathHelper.Clamp(offset.X / horizontalExtent, -1f, 1f),
			MathHelper.Clamp(offset.Y / verticalExtent, -1f, 1f));
	}
}
