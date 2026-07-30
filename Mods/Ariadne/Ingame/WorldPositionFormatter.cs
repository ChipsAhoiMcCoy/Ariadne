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
		Point tile = ToTile(worldPosition);
		Point playerTile = ToTile(Main.LocalPlayer.Center);
		Vector2 deltaTiles = new(tile.X - playerTile.X, tile.Y - playerTile.Y);
		int distance = (int)MathF.Round(deltaTiles.Length());
		string horizontal = MathF.Abs(deltaTiles.X) < SummaryDeadbandTiles
			? Text("SameEastWest")
			: HorizontalOffset((int)deltaTiles.X);
		string vertical = MathF.Abs(deltaTiles.Y) < SummaryDeadbandTiles
			? Text("SameElevation")
			: VerticalOffset((int)deltaTiles.Y);
		return Text("RelativeSummary", distance, horizontal, vertical);
	}

	/// <summary>
	/// Which way a position lies from the player, without the straight-line total that
	/// <see cref="DescribeRelativePosition"/> leads with.
	///
	/// The two components already say how far in the only sense that helps someone walk
	/// there, and the total mostly repeats them. That repetition is affordable when one
	/// thing is being described, which is the cursor's case, and not when several are in
	/// a row: a radar sweep and a scanner row both carry more than the position alone.
	/// </summary>
	internal static string DescribeDirection(Vector2 worldPosition)
	{
		Point tile = ToTile(worldPosition);
		Point playerTile = ToTile(Main.LocalPlayer.Center);
		int offsetX = tile.X - playerTile.X;
		int offsetY = tile.Y - playerTile.Y;
		bool alongside = Math.Abs(offsetX) < SummaryDeadbandTiles;
		bool level = Math.Abs(offsetY) < SummaryDeadbandTiles;
		if (alongside && level)
		{
			return Text("AtPlayerDirection");
		}

		return Text(
			"DirectionSummary",
			alongside ? Text("SameEastWest") : HorizontalOffset(offsetX),
			level ? Text("SameElevation") : VerticalOffset(offsetY));
	}

	/// <summary>
	/// Raw tile numbers, whatever the relative-readout preference says.
	///
	/// The preference exists because an offset is the more useful answer while moving,
	/// and that stays true. A scanner result is the case it does not cover: the snapshot
	/// is fixed, the player may act on it after walking somewhere else, and the raw
	/// numbers are the part that is still true when they do. Waypoint names already keep
	/// their coordinates for the same reason.
	/// </summary>
	internal static string DescribeRawCoordinates(Vector2 worldPosition)
	{
		Point tile = ToTile(worldPosition);
		return Text("TileCoordinates", tile.X, tile.Y);
	}

	internal static string DescribeCoordinates(Vector2 worldPosition)
	{
		Point tile = ToTile(worldPosition);
		int tileX = tile.X;
		int tileY = tile.Y;
		if (!ModContent.GetInstance<AriadneClientConfig>().RelativeCoordinateReadoutEnabled)
		{
			return Text("RawCoordinates", tileX, tileY, SpawnOffset(tileX));
		}

		Point playerTile = ToTile(Main.LocalPlayer.Center);
		int offsetX = tileX - playerTile.X;
		int offsetY = tileY - playerTile.Y;
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
		Point playerTile = ToTile(Main.LocalPlayer.Center);
		int tileX = playerTile.X;
		int tileY = playerTile.Y;
		if (!ModContent.GetInstance<AriadneClientConfig>().RelativeCoordinateReadoutEnabled)
		{
			return Text("RawCoordinates", tileX, tileY, SpawnOffset(tileX));
		}

		int depth = tileY - Main.spawnTileY;
		string vertical = depth == 0 ? Text("SpawnLevel") : VerticalOffset(depth);
		return Text("SelfLocation", SpawnOffset(tileX), vertical);
	}

	/// <summary>
	/// Converts a world position to the tile that contains it, the same truncation
	/// Terraria's own ToTileCoordinates uses. Rounding instead would report the tile
	/// boundary nearest the position, so a cursor sitting on the player's own tile
	/// could land one tile away from the player and read as one tile above or below
	/// when it is meant to be the neutral origin.
	/// </summary>
	private static Point ToTile(Vector2 worldPosition)
	{
		return new Point(
			(int)MathF.Floor(worldPosition.X / TileSize),
			(int)MathF.Floor(worldPosition.Y / TileSize));
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
