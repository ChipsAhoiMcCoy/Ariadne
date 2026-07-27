#nullable enable

using System;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;

namespace Ariadne.Ingame.Teleport;

/// <summary>
/// Decides whether a player-sized box is somewhere a teleport may legitimately end. Shared by
/// every Ariadne feature that moves the player, so they all reject the same hazards.
/// </summary>
internal static class SafeLandingProbe
{
	internal static bool IsSafeLanding(Player player, Vector2 position)
	{
		float worldRight = Main.maxTilesX * 16f;
		float worldBottom = Main.maxTilesY * 16f;
		if (position.X < 16f || position.Y < 16f ||
			position.X + player.width > worldRight - 16f || position.Y + player.height > worldBottom - 16f)
		{
			return false;
		}

		if (Collision.SolidCollision(position, player.width, player.height) ||
			Collision.LavaCollision(position, player.width, player.height) ||
			ContainsShimmer(position, player.width, player.height))
		{
			return false;
		}

		int gravityDirection = player.gravDir < 0f ? -1 : 1;
		Vector2 supportVelocity = new(0f, gravityDirection * 3f);
		Vector2 collisionVelocity = Collision.TileCollision(
			position,
			supportVelocity,
			player.width,
			player.height,
			fallThrough: false,
			fall2: false,
			gravDir: gravityDirection);
		if (MathF.Abs(collisionVelocity.Y) >= MathF.Abs(supportVelocity.Y))
		{
			return false;
		}

		Vector2 hazardProbe = gravityDirection > 0 ? position : position - new Vector2(0f, 2f);
		if (Collision.AnyHurtingTiles(hazardProbe, player.width, player.height + 2))
		{
			return false;
		}

		return true;
	}

	internal static bool ContainsShimmer(Vector2 position, int width, int height)
	{
		int firstX = Math.Clamp((int)MathF.Floor(position.X / 16f), 0, Main.maxTilesX - 1);
		int lastX = Math.Clamp((int)MathF.Floor((position.X + width - 1f) / 16f), 0, Main.maxTilesX - 1);
		int firstY = Math.Clamp((int)MathF.Floor(position.Y / 16f), 0, Main.maxTilesY - 1);
		int lastY = Math.Clamp((int)MathF.Floor((position.Y + height - 1f) / 16f), 0, Main.maxTilesY - 1);
		for (int x = firstX; x <= lastX; x++)
		{
			for (int y = firstY; y <= lastY; y++)
			{
				Tile tile = Main.tile[x, y];
				if (tile.LiquidAmount > 0 && tile.LiquidType == LiquidID.Shimmer)
				{
					return true;
				}
			}
		}
		return false;
	}

	/// <summary>
	/// Converts a tile coordinate into the position a player standing on that tile would occupy,
	/// honouring inverted gravity.
	/// </summary>
	internal static Vector2 StandingPosition(Player player, int tileX, int tileY)
	{
		float standingY = player.gravDir < 0f ? (tileY + 1) * 16f : tileY * 16f - player.height;
		return new Vector2(tileX * 16f, standingY);
	}
}
