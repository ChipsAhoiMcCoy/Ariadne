#nullable enable

using System;
using Microsoft.Xna.Framework;
using Terraria;

namespace Terrarium.Ingame.Controls;

internal sealed class WorldCursorState
{
	private Point _precisionTile;

	internal bool IsInitialized { get; private set; }

	internal Vector2 AimDirection { get; private set; } = Vector2.UnitX;

	internal Point PrecisionTile => _precisionTile;

	internal Vector2 PrecisionWorld => TileCenter(_precisionTile);

	internal void Initialize(Player player)
	{
		int facing = player.direction == 0 ? 1 : player.direction;
		_precisionTile = ClampTile((player.Center + new Vector2(facing * 16f, 0f)).ToTileCoordinates());
		AimDirection = new Vector2(facing, 0f);
		IsInitialized = true;
	}

	internal void MovePrecision(Point offset)
	{
		_precisionTile = ClampTile(new Point(_precisionTile.X + offset.X, _precisionTile.Y + offset.Y));
		UpdateDirectionFromPrecision(Main.LocalPlayer);
	}

	internal void SetPrecision(Vector2 worldPosition, Player player)
	{
		_precisionTile = ClampTile(worldPosition.ToTileCoordinates());
		UpdateDirectionFromPrecision(player);
		IsInitialized = true;
	}

	internal void SetPrecision(Point tile, Player player)
	{
		_precisionTile = ClampTile(tile);
		UpdateDirectionFromPrecision(player);
		IsInitialized = true;
	}

	internal void Recenter(Player player)
	{
		_precisionTile = ClampTile(player.Center.ToTileCoordinates());
		AimDirection = new Vector2(player.direction == 0 ? 1 : player.direction, 0f);
		IsInitialized = true;
	}

	internal void SetAimDirection(Vector2 direction)
	{
		if (direction.LengthSquared() < 0.001f)
		{
			return;
		}

		AimDirection = Vector2.Normalize(direction);
	}

	internal void UpdateDirectionFromPrecision(Player player)
	{
		Vector2 direction = PrecisionWorld - player.Center;
		if (direction.LengthSquared() >= 0.001f)
		{
			AimDirection = Vector2.Normalize(direction);
		}
	}

	internal void RecoverIntoViewport()
	{
		Vector2 viewportPosition = Main.Camera.ScaledPosition;
		Vector2 viewportSize = Main.Camera.ScaledSize;
		if (viewportSize.X < 16f || viewportSize.Y < 16f)
		{
			return;
		}

		int firstX = Math.Clamp((int)MathF.Ceiling(viewportPosition.X / 16f), 1, Main.maxTilesX - 2);
		int lastX = Math.Clamp((int)MathF.Floor((viewportPosition.X + viewportSize.X) / 16f) - 1, 1, Main.maxTilesX - 2);
		int firstY = Math.Clamp((int)MathF.Ceiling(viewportPosition.Y / 16f), 1, Main.maxTilesY - 2);
		int lastY = Math.Clamp((int)MathF.Floor((viewportPosition.Y + viewportSize.Y) / 16f) - 1, 1, Main.maxTilesY - 2);
		if (lastX < firstX || lastY < firstY)
		{
			return;
		}

		_precisionTile.X = Math.Clamp(_precisionTile.X, firstX, lastX);
		_precisionTile.Y = Math.Clamp(_precisionTile.Y, firstY, lastY);
	}

	internal void Reset()
	{
		_precisionTile = Point.Zero;
		AimDirection = Vector2.UnitX;
		IsInitialized = false;
	}

	private static Point ClampTile(Point tile)
	{
		return new Point(
			Math.Clamp(tile.X, 1, Math.Max(1, Main.maxTilesX - 2)),
			Math.Clamp(tile.Y, 1, Math.Max(1, Main.maxTilesY - 2)));
	}

	private static Vector2 TileCenter(Point tile) => new(tile.X * 16f + 8f, tile.Y * 16f + 8f);
}
