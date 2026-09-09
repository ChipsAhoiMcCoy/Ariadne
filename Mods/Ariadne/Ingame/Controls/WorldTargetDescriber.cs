#nullable enable

using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.Enums;
using Terraria.ID;
using Terraria.Localization;
using Terraria.Map;
using Terraria.ModLoader;
using Terraria.ObjectData;

namespace Ariadne.Ingame.Controls;

internal readonly record struct WorldTargetDescription(
	string SemanticKey,
	string TargetText,
	string DetailedText,
	string CoordinateDetailedText,
	bool IsEmptySpace);

internal static class WorldTargetDescriber
{
	internal static WorldTargetDescription Describe(
		Point tilePosition,
		Player player,
		bool includeInteractableEntities = false)
	{
		int x = Math.Clamp(tilePosition.X, 1, Main.maxTilesX - 2);
		int y = Math.Clamp(tilePosition.Y, 1, Main.maxTilesY - 2);
		Vector2 worldPosition = new(x * 16f + 8f, y * 16f + 8f);
		WorldInteractionTarget? interactiveEntity = includeInteractableEntities
			? WorldInteractionResolver.FindEntityAt(worldPosition, player)
			: null;
		NPC? cursorNpc = includeInteractableEntities
			? interactiveEntity?.Npc ?? WorldInteractionResolver.FindNpcAt(worldPosition, player)
			: null;

		Tile tile = Main.tile[x, y];
		string semanticKey;
		string target;
		bool isEmptySpace = false;

		TileObjectData? objectData = tile.HasTile ? TileObjectData.GetTileData(tile) : null;
		Point16 root = objectData is null ? Point16.NegativeOne : TileObjectData.TopLeft(x, y);
		if (tile.HasTile && objectData is not null && root != Point16.NegativeOne)
		{
			target = GetObjectName(x, y, tile, root);
			semanticKey = $"object:{tile.TileType}:{root.X}:{root.Y}:{target}";
		}
		else if (tile.HasTile)
		{
			target = GetTileName(x, y, tile.TileType);
			semanticKey = $"tile:{tile.TileType}:{target}";
		}
		else if (tile.LiquidAmount > 0)
		{
			target = LiquidName(tile.LiquidType);
			semanticKey = $"liquid:{tile.LiquidType}";
		}
		else if (tile.WallType > 0 && (player.HeldItem.hammer > 0 || player.HeldItem.createWall > 0))
		{
			target = $"{GetWallName(tile.WallType)} background wall";
			semanticKey = $"wall:{tile.WallType}";
		}
		else
		{
			target = "Empty space";
			semanticKey = "empty";
			isEmptySpace = true;
		}

		if (tile.LiquidAmount > 0 && tile.HasTile)
		{
			target = $"{LiquidName(tile.LiquidType)} over {target}";
			semanticKey = $"liquid:{tile.LiquidType}:{semanticKey}";
		}

		List<string> wiring = [];
		if (tile.RedWire) wiring.Add("red wire");
		if (tile.BlueWire) wiring.Add("blue wire");
		if (tile.GreenWire) wiring.Add("green wire");
		if (tile.YellowWire) wiring.Add("yellow wire");
		if (tile.HasActuator) wiring.Add(tile.IsActuated ? "active actuator" : "actuator");
		if (wiring.Count > 0)
		{
			string wiringText = string.Join(", ", wiring);
			target = target == "Empty space" ? wiringText : $"{target} with {wiringText}";
			semanticKey = $"{semanticKey}:wiring:{wiringText}";
			isEmptySpace = false;
		}

		bool isInReach = IsWithinHeldItemReach(player, x, y);
		string reach = isInReach ? "in reach" : "out of reach";
		string coordinateReach = Language.GetTextValue(
			isInReach
				? "Mods.Ariadne.Announcements.CursorInReach"
				: "Mods.Ariadne.Announcements.CursorOutOfReach");
		WorldTargetDescription tileDescription = new(
			semanticKey,
			target,
			$"{target}, {reach}, {WorldPositionFormatter.DescribeRelativePosition(worldPosition)}.",
			Language.GetTextValue(
				"Mods.Ariadne.Announcements.CursorCoordinates",
				target,
				WorldPositionFormatter.DescribeCoordinates(worldPosition),
				coordinateReach),
			isEmptySpace);
		if (cursorNpc is null)
		{
			return tileDescription;
		}

		return DescribeEntity(cursorNpc, interactiveEntity, player, tileDescription.TargetText, isInReach);
	}

	private static WorldTargetDescription DescribeEntity(
		NPC npc,
		WorldInteractionTarget? interaction,
		Player player,
		string underlyingTarget,
		bool tileIsInReach)
	{
		string name = npc.FullName;
		if (CombatTargetStatus.DescribePart(npc) is string part)
		{
			name = $"{name}, {part}";
		}

		bool isInteractive = interaction is WorldInteractionTarget { IsEntity: true };
		bool isInReach = interaction?.Kind == WorldInteractionTargetKind.OldShakingChest ||
			interaction?.Kind == WorldInteractionTargetKind.ConversationNpc &&
			NpcConversation.IsWithinConversationReach(player, npc);
		string reach = isInReach ? "in reach" : "out of reach";
		string condition = npc.dontTakeDamage || npc.immortal
			? "invulnerable"
			: $"{Math.Max(0, npc.life)} of {Math.Max(1, npc.lifeMax)} health";
		string over = string.Equals(underlyingTarget, "Empty space", StringComparison.OrdinalIgnoreCase)
			? string.Empty
			: $", over {underlyingTarget}";
		string summary = isInteractive
			? $"{name}, {reach}{over}"
			: $"{name}, {condition}{over}";
		bool coordinateIsInReach = isInteractive ? isInReach : tileIsInReach;
		string coordinateReach = Language.GetTextValue(
			coordinateIsInReach
				? "Mods.Ariadne.Announcements.CursorInReach"
				: "Mods.Ariadne.Announcements.CursorOutOfReach");
		return new(
			$"npc:{npc.whoAmI}:{npc.type}:{interaction?.Kind.ToString() ?? "description"}",
			name,
			$"{summary}, {WorldPositionFormatter.DescribeRelativePosition(npc.Center)}.",
			Language.GetTextValue(
				"Mods.Ariadne.Announcements.CursorCoordinates",
				summary,
				WorldPositionFormatter.DescribeCoordinates(npc.Center),
				coordinateReach),
			IsEmptySpace: false);
	}

	private static string GetObjectName(int x, int y, Tile tile, Point16 root)
	{
		string name = GetTileName(x, y, tile.TileType);
		return ContainerNameResolver.IsContainer(tile.TileType)
			? ContainerNameResolver.Describe(root, tile, name)
			: name;
	}

	internal static string GetTileName(int x, int y, ushort type)
	{
		Tile tile = Main.tile[x, y];
		if (type is TileID.Banners or TileID.Statues)
		{
			int style = TileObjectData.GetTileStyle(tile);
			// Vanilla statues use another bank of 165 styles when facing right.
			if (type == TileID.Statues && style >= 0) style %= 165;
			foreach (Item sample in ContentSamples.ItemsByType.Values)
			{
				if (sample.createTile == type && sample.placeStyle == style) return sample.Name;
			}
		}
		string? treeName = GetTreeName(x, y, type);
		if (treeName is not null)
		{
			return treeName;
		}

		if (type < MapHelper.tileLookup.Length && MapHelper.tileLookup[type] != 0)
		{
			MapTile mapTile = MapHelper.CreateMapTile(x, y, byte.MaxValue);
			if (mapTile.Type != 0)
			{
				string mapName = Lang.GetMapObjectName(mapTile.Type);
				if (!string.IsNullOrWhiteSpace(mapName))
				{
					return HerbNameResolver.Describe(tile, mapName);
				}
			}
		}

		ModTile? modTile = TileLoader.GetTile(type);
		if (modTile is not null)
		{
			return HerbNameResolver.Describe(tile, Humanize(modTile.Name));
		}

		string? vanillaName = TileID.Search.GetName(type);
		string fallback = string.IsNullOrWhiteSpace(vanillaName) ? $"Tile {type}" : Humanize(vanillaName);
		return HerbNameResolver.Describe(tile, fallback);
	}

	private static string? GetTreeName(int x, int y, ushort type)
	{
		string? fixedName = type switch
		{
			TileID.TreeTopaz => "Topaz gem tree",
			TileID.TreeAmethyst => "Amethyst gem tree",
			TileID.TreeSapphire => "Sapphire gem tree",
			TileID.TreeEmerald => "Emerald gem tree",
			TileID.TreeRuby => "Ruby gem tree",
			TileID.TreeDiamond => "Diamond gem tree",
			TileID.TreeAmber => "Amber gem tree",
			TileID.VanityTreeSakura => "Sakura tree",
			TileID.VanityTreeYellowWillow => "Yellow willow tree",
			_ => null,
		};
		if (fixedName is not null)
		{
			return fixedName;
		}

		if (type is not (TileID.Trees or TileID.MushroomTrees or TileID.PalmTree or TileID.TreeAsh))
		{
			return null;
		}

		WorldGen.GetTreeBottom(x, y, out int bottomX, out int bottomY);
		if (!WorldGen.InWorld(bottomX, bottomY, 1))
		{
			return null;
		}

		ushort soilType = Main.tile[bottomX, bottomY].TileType;
		object? modTree = PlantLoader.GetTree(soilType);
		if (modTree is not null)
		{
			string modTreeName = Humanize(modTree.GetType().Name);
			return modTreeName.EndsWith("tree", StringComparison.OrdinalIgnoreCase)
				? modTreeName
				: $"{modTreeName} tree";
		}

		return WorldGen.GetTreeType(soilType) switch
		{
			TreeTypes.Forest => "Forest tree",
			TreeTypes.Corrupt => "Corrupt tree",
			TreeTypes.Mushroom => "Mushroom tree",
			TreeTypes.Crimson => "Crimson tree",
			TreeTypes.Jungle => "Jungle tree",
			TreeTypes.Snow => "Snow tree",
			TreeTypes.Hallowed => "Hallowed tree",
			TreeTypes.Palm => "Palm tree",
			TreeTypes.PalmCrimson => "Crimson palm tree",
			TreeTypes.PalmCorrupt => "Corrupt palm tree",
			TreeTypes.PalmHallowed => "Hallowed palm tree",
			TreeTypes.Ash => "Ash tree",
			TreeTypes.Custom => type == TileID.PalmTree ? "Custom palm tree" : "Custom tree",
			_ => null,
		};
	}

	private static bool IsWithinHeldItemReach(Player player, int x, int y)
	{
		Item item = player.HeldItem;
		int boost = item.tileBoost + player.blockRange;
		return player.position.X / 16f - Player.tileRangeX - boost <= x &&
			(player.position.X + player.width) / 16f + Player.tileRangeX + boost - 1f >= x &&
			player.position.Y / 16f - Player.tileRangeY - boost <= y &&
			(player.position.Y + player.height) / 16f + Player.tileRangeY + boost - 2f >= y;
	}

	private static string GetWallName(ushort type)
	{
		ModWall? modWall = WallLoader.GetWall(type);
		if (modWall is not null)
		{
			return Humanize(modWall.Name);
		}

		string? vanillaName = WallID.Search.GetName(type);
		return string.IsNullOrWhiteSpace(vanillaName) ? $"Wall {type}" : Humanize(vanillaName);
	}

	private static string LiquidName(int liquidType) => liquidType switch
	{
		LiquidID.Lava => "Lava",
		LiquidID.Honey => "Honey",
		LiquidID.Shimmer => "Shimmer",
		_ => "Water",
	};

	private static string Humanize(string value)
	{
		StringBuilder result = new();
		for (int index = 0; index < value.Length; index++)
		{
			char current = value[index];
			if (index > 0 && char.IsUpper(current) && char.IsLower(value[index - 1]))
			{
				result.Append(' ');
			}
			result.Append(current);
		}
		return result.ToString();
	}
}
