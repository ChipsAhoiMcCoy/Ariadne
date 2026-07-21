#nullable enable

using Microsoft.Xna.Framework;

namespace Terrarium.Ingame.WallTones;

internal enum WallToneRegion
{
	Left,
	Right,
	Ceiling,
}

internal readonly record struct WallToneRegionSnapshot(
	bool HasHit,
	float DistancePixels,
	float MaximumDistancePixels,
	Vector2 NormalizedPosition,
	float Roughness)
{
	internal static WallToneRegionSnapshot Empty(float maximumDistancePixels)
	{
		return new(false, maximumDistancePixels, maximumDistancePixels, Vector2.Zero, 0f);
	}

	internal float Proximity => HasHit && MaximumDistancePixels > 0f
		? MathHelper.Clamp(1f - DistancePixels / MaximumDistancePixels, 0f, 1f)
		: 0f;
}

internal readonly record struct WallToneSnapshot(
	WallToneRegionSnapshot Left,
	WallToneRegionSnapshot Right,
	WallToneRegionSnapshot Ceiling)
{
	internal static WallToneSnapshot Empty(float maximumDistancePixels)
	{
		WallToneRegionSnapshot empty = WallToneRegionSnapshot.Empty(maximumDistancePixels);
		return new(empty, empty, empty);
	}
}
