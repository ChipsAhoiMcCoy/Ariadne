#nullable enable

using System;
using Microsoft.Xna.Framework;
using Terraria;

namespace Ariadne.Ingame.Freecam;

internal readonly record struct FreecamMovementResult(
	Vector2 Position,
	bool BlockedLeft,
	bool BlockedRight,
	bool BlockedUp,
	bool BlockedDown,
	bool RangeLimited);

/// <summary>
/// Moves the virtual body through Terraria's native cardinal and slope
/// collision paths without touching the real player.
/// </summary>
internal static class FreecamCollisionMover
{
	internal const float MaximumRangePixels = 60f * 16f;
	private const float MaximumSubstepPixels = 4f;
	private const float CollisionEpsilon = 0.01f;

	internal static FreecamMovementResult Move(
		Vector2 position,
		int width,
		int height,
		int gravityDirection,
		Vector2 displacement,
		Vector2 livePlayerCenter,
		bool fallThroughPlatforms)
	{
		bool blockedLeft = false;
		bool blockedRight = false;
		bool blockedUp = false;
		bool blockedDown = false;
		bool rangeLimited = false;
		float distance = displacement.Length();
		int stepCount = Math.Max(1, (int)MathF.Ceiling(distance / MaximumSubstepPixels));
		Vector2 requestedStep = displacement / stepCount;

		for (int step = 0; step < stepCount; step++)
		{
			Vector2 start = position;
			Vector2 tileVelocity = Collision.TileCollision(
				position,
				requestedStep,
				width,
				height,
				fallThroughPlatforms,
				fall2: false,
				gravityDirection);
			Vector2 tilePosition = position + tileVelocity;
			Vector4 slopeResult = Collision.SlopeCollision(
				tilePosition,
				tileVelocity,
				width,
				height,
				gravity: 0f,
				fall: fallThroughPlatforms);
			Vector2 resolvedPosition = new(slopeResult.X, slopeResult.Y);
			resolvedPosition = ClampToWorld(resolvedPosition, width, height);
			Vector2 collisionDisplacement = resolvedPosition - start;

			if (requestedStep.X < 0f &&
				-collisionDisplacement.X < -requestedStep.X - CollisionEpsilon)
			{
				blockedLeft = true;
			}
			if (requestedStep.X > 0f &&
				collisionDisplacement.X < requestedStep.X - CollisionEpsilon)
			{
				blockedRight = true;
			}
			if (requestedStep.Y < 0f &&
				-collisionDisplacement.Y < -requestedStep.Y - CollisionEpsilon)
			{
				blockedUp = true;
			}
			if (requestedStep.Y > 0f &&
				collisionDisplacement.Y < requestedStep.Y - CollisionEpsilon)
			{
				blockedDown = true;
			}

			Vector2 constrained = ConstrainCenterToRange(
				resolvedPosition,
				width,
				height,
				livePlayerCenter);
			if (Vector2.DistanceSquared(constrained, resolvedPosition) > CollisionEpsilon * CollisionEpsilon)
			{
				rangeLimited = true;
			}
			position = constrained;
		}

		return new(
			position,
			blockedLeft,
			blockedRight,
			blockedUp,
			blockedDown,
			rangeLimited);
	}

	internal static Vector2 ConstrainCenterToRange(
		Vector2 position,
		int width,
		int height,
		Vector2 livePlayerCenter)
	{
		Vector2 virtualCenter = position + new Vector2(width * 0.5f, height * 0.5f);
		Vector2 offset = virtualCenter - livePlayerCenter;
		float distanceSquared = offset.LengthSquared();
		float maximumDistanceSquared = MaximumRangePixels * MaximumRangePixels;
		if (distanceSquared <= maximumDistanceSquared || distanceSquared <= 0.0001f)
		{
			return position;
		}

		Vector2 constrainedCenter =
			livePlayerCenter + offset * (MaximumRangePixels / MathF.Sqrt(distanceSquared));
		return constrainedCenter - new Vector2(width * 0.5f, height * 0.5f);
	}

	private static Vector2 ClampToWorld(Vector2 position, int width, int height)
	{
		float minimumX = Main.leftWorld + 16f;
		float minimumY = Main.topWorld + 16f;
		float maximumX = MathF.Max(minimumX, Main.rightWorld - width - 16f);
		float maximumY = MathF.Max(minimumY, Main.bottomWorld - height - 16f);
		return new(
			MathHelper.Clamp(position.X, minimumX, maximumX),
			MathHelper.Clamp(position.Y, minimumY, maximumY));
	}
}
