#nullable enable

using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;

namespace Ariadne.Ingame;

/// <summary>
/// Names a container tile the way the game labels it for a sighted player. The map
/// legend only carries one entry for the whole chest family, so a map-derived name
/// collapses every style down to "Chest" or worse to the raw tile name. Vanilla
/// instead reads the style out of the tile frame and appends any player-assigned
/// label, which is the distinction a stored-loot chest is usually named for.
/// </summary>
internal static class ContainerNameResolver
{
	internal static bool IsContainer(ushort type) =>
		(type < TileID.Sets.IsAContainer.Length && TileID.Sets.IsAContainer[type]) ||
		(type < TileID.Sets.BasicChestFake.Length && TileID.Sets.BasicChestFake[type]);

	/// <param name="root">Top-left tile of the container, where its chest data is stored.</param>
	/// <param name="tile">Any tile belonging to the container; only its type and frame are read.</param>
	/// <param name="fallbackName">Name to use when no style table covers the tile.</param>
	internal static string Describe(Point16 root, Tile tile, string fallbackName)
	{
		string variety = VarietyName(tile, fallbackName);
		int chestIndex = root == Point16.NegativeOne ? -1 : Chest.FindChest(root.X, root.Y);
		string? label = chestIndex >= 0 ? Main.chest[chestIndex]?.name : null;
		return string.IsNullOrWhiteSpace(label) || label == variety
			? variety
			: $"{variety}: {label}";
	}

	private static string VarietyName(Tile tile, string fallbackName)
	{
		switch (tile.TileType)
		{
			case TileID.Containers:
			case TileID.FakeContainers:
				return StyleName(Lang.chestType, tile.TileFrameX / 36, fallbackName);
			// A Dead Man's Chest keeps its disguised style name here, the same way
			// vanilla's hover icon and map label leave the trap hidden until it opens.
			case TileID.Containers2:
			case TileID.FakeContainers2:
				return StyleName(Lang.chestType2, tile.TileFrameX / 36, fallbackName);
			case TileID.Dressers:
				return StyleName(Lang.dresserType, tile.TileFrameX / 54, fallbackName);
			default:
				string moddedName = TileLoader.DefaultContainerName(tile.TileType, tile.TileFrameX, tile.TileFrameY);
				return string.IsNullOrWhiteSpace(moddedName) ? fallbackName : moddedName;
		}
	}

	private static string StyleName(LocalizedText[] names, int style, string fallbackName)
	{
		if (style < 0 || style >= names.Length)
		{
			return fallbackName;
		}

		string value = names[style].Value;
		return string.IsNullOrWhiteSpace(value) ? fallbackName : value;
	}
}
