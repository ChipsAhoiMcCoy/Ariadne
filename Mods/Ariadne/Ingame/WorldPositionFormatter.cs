#nullable enable

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Localization;
using Terraria.ModLoader;
using Ariadne.Configs;

namespace Ariadne.Ingame;

/// <summary>
/// Turns world positions into spoken text. Coordinates are reported as an offset
/// from the player by default, who counts as zero, because Terraria's raw tile
/// numbers run into the thousands on a large world and carry no usable meaning on
/// their own. Raw numbers remain available through configuration.
/// </summary>
internal static class WorldPositionFormatter
{
	private const float TileSize = 16f;
	// A summary is only meant to give a bearing, so small offsets read as level
	// rather than inviting the listener to act on a difference of one tile.
	private const float SummaryDeadbandTiles = 2f;

	internal static string DescribeRelativePosition(Vector2 worldPosition)
	{
		Vector2 deltaTiles = (worldPosition - Main.LocalPlayer.Center) / TileSize;
		int distance = (int)MathF.Round(deltaTiles.Length());
		string horizontal = MathF.Abs(deltaTiles.X) < SummaryDeadbandTiles
			? Text("SameEastWest")
			: HorizontalOffset((int)MathF.Round(deltaTiles.X));
		string vertical = MathF.Abs(deltaTiles.Y) < SummaryDeadbandTiles
			? Text("SameElevation")
			: VerticalOffset((int)MathF.Round(deltaTiles.Y));
		return Text("RelativeSummary", distance, horizontal, vertical);
	}

	internal static string DescribeCoordinates(Vector2 worldPosition)
	{
		int tileX = (int)MathF.Round(worldPosition.X / TileSize);
		int tileY = (int)MathF.Round(worldPosition.Y / TileSize);
		if (!ModContent.GetInstance<AriadneClientConfig>().RelativeCoordinateReadoutEnabled)
		{
			return Text("RawCoordinates", tileX, tileY, SpawnOffset(tileX));
		}

		Vector2 playerCenter = Main.LocalPlayer.Center;
		int offsetX = tileX - (int)MathF.Round(playerCenter.X / TileSize);
		int offsetY = tileY - (int)MathF.Round(playerCenter.Y / TileSize);
		if (offsetX == 0 && offsetY == 0)
		{
			return Text("AtPlayer");
		}

		List<string> parts = [];
		if (offsetX != 0)
		{
			parts.Add(HorizontalOffset(offsetX));
		}
		if (offsetY != 0)
		{
			parts.Add(VerticalOffset(offsetY));
		}
		return $"{string.Join(", ", parts)}.";
	}

	/// <summary>
	/// Describes where the player themselves stands. The player-relative form would
	/// answer zero here, so this reports the offset from world spawn instead. Callers
	/// pair it with the biome readout, which already names the elevation layer.
	/// </summary>
	internal static string DescribeSelfLocation()
	{
		int tileX = (int)MathF.Round(Main.LocalPlayer.Center.X / TileSize);
		int tileY = (int)MathF.Round(Main.LocalPlayer.Center.Y / TileSize);
		if (!ModContent.GetInstance<AriadneClientConfig>().RelativeCoordinateReadoutEnabled)
		{
			return Text("RawCoordinates", tileX, tileY, SpawnOffset(tileX));
		}

		int depth = tileY - Main.spawnTileY;
		string vertical = depth == 0 ? Text("SpawnLevel") : VerticalOffset(depth);
		return Text("SelfLocation", SpawnOffset(tileX), vertical);
	}

	private static string SpawnOffset(int tileX)
	{
		int offset = tileX - Main.spawnTileX;
		return offset == 0
			? Text("SpawnLongitude")
			: Text(offset < 0 ? "WestOfSpawn" : "EastOfSpawn", Math.Abs(offset));
	}

	private static string HorizontalOffset(int tiles)
	{
		return Text(tiles < 0 ? "TilesWest" : "TilesEast", Math.Abs(tiles));
	}

	private static string VerticalOffset(int tiles)
	{
		// Terraria's Y axis grows downward, so a positive offset is below the origin.
		return Text(tiles < 0 ? "TilesAbove" : "TilesBelow", Math.Abs(tiles));
	}

	private static string Text(string key, params object[] arguments)
	{
		return Language.GetTextValue($"Mods.Ariadne.Position.{key}", arguments);
	}
}
