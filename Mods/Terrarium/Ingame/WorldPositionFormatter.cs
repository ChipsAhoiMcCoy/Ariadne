#nullable enable

using System;
using Microsoft.Xna.Framework;
using Terraria;

namespace Terrarium.Ingame;

internal static class WorldPositionFormatter
{
	internal static string DescribeRelativePosition(Vector2 worldPosition)
	{
		Vector2 deltaTiles = (worldPosition - Main.LocalPlayer.Center) / 16f;
		int distance = (int)MathF.Round(deltaTiles.Length());
		string horizontal = MathF.Abs(deltaTiles.X) < 2f ? "same east-west position" :
			$"{Math.Abs((int)MathF.Round(deltaTiles.X))} tiles {(deltaTiles.X < 0f ? "west" : "east")}";
		string vertical = MathF.Abs(deltaTiles.Y) < 2f ? "same elevation" :
			$"{Math.Abs((int)MathF.Round(deltaTiles.Y))} tiles {(deltaTiles.Y < 0f ? "above" : "below")}";
		return $"about {distance} tiles away, {horizontal}, {vertical}";
	}

	internal static string DescribeCoordinates(Vector2 worldPosition)
	{
		int tileX = (int)MathF.Round(worldPosition.X / 16f);
		int tileY = (int)MathF.Round(worldPosition.Y / 16f);
		int horizontalFromSpawn = tileX - Main.spawnTileX;
		string horizontal = Math.Abs(horizontalFromSpawn) < 2
			? "at the world's spawn longitude"
			: $"{Math.Abs(horizontalFromSpawn)} tiles {(horizontalFromSpawn < 0 ? "west" : "east")} of world spawn";
		return $"Tile {tileX}, {tileY}; {horizontal}.";
	}
}
