#nullable enable

using System;
using Microsoft.Xna.Framework;
using Terraria;
using Ariadne.Audio;
using Ariadne.Ingame.Freecam;

namespace Ariadne.Ingame;

/// <summary>
/// The body and viewport used by spatial awareness. Gameplay and semantic
/// interaction systems deliberately continue to use <see cref="Main.LocalPlayer"/>.
/// </summary>
internal readonly record struct SpatialObserverSnapshot(
	Vector2 Center,
	int Width,
	int Height,
	float GravityDirection,
	Vector2 ViewportPosition,
	Vector2 ViewportSize,
	bool IsVirtual)
{
	/// <summary>
	/// Places a world position on the observer's own screen: the body reads centred and
	/// either visible edge reads hard over. The viewport rectangle is a poor origin
	/// because Terraria clamps the camera near world boundaries, leaving the body
	/// off center and every cue biased toward one ear for as long as it stays there,
	/// so the body supplies the origin and the rectangle supplies each side's scale.
	/// </summary>
	internal Vector2 NormalizeToViewport(Vector2 worldPosition)
	{
		return ViewportSpatialPosition.Normalize(worldPosition, Center, ViewportPosition, ViewportSize);
	}
}

internal static class SpatialObserverContext
{
	internal static uint Revision => FreecamSystem.ObserverRevision;

	internal static SpatialObserverSnapshot Current
	{
		get
		{
			if (FreecamSystem.TryGetVirtualBody(
				out Vector2 center,
				out int width,
				out int height,
				out float gravityDirection))
			{
				(Vector2 viewportPosition, Vector2 viewportSize) = CalculateCenteredViewport(center);
				return new(
					center,
					width,
					height,
					gravityDirection,
					viewportPosition,
					viewportSize,
					IsVirtual: true);
			}

			Player player = Main.LocalPlayer;
			return new(
				player.Center,
				player.width,
				player.height,
				player.gravDir < 0f ? -1f : 1f,
				Main.Camera.ScaledPosition,
				Main.Camera.ScaledSize,
				IsVirtual: false);
		}
	}

	internal static (Vector2 Position, Vector2 Size) CalculateCenteredViewport(Vector2 center)
	{
		Vector2 size = Main.Camera.ScaledSize;
		if (size.X <= 0f || size.Y <= 0f)
		{
			size = new(Math.Max(1, Main.screenWidth), Math.Max(1, Main.screenHeight));
		}

		Vector2 desired = center - size * 0.5f;
		float minimumX = Main.leftWorld + 656f;
		float minimumY = Main.topWorld + 656f;
		float maximumX = Main.rightWorld - size.X - 672f;
		float maximumY = Main.bottomWorld - size.Y - 672f;
		if (maximumX < minimumX)
		{
			maximumX = minimumX;
		}
		if (maximumY < minimumY)
		{
			maximumY = minimumY;
		}

		return (
			new(
				MathHelper.Clamp(desired.X, minimumX, maximumX),
				MathHelper.Clamp(desired.Y, minimumY, maximumY)),
			size);
	}
}
