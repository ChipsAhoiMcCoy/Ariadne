#nullable enable

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.GameContent;
using Terraria.GameContent.Tile_Entities;
using Terraria.ID;
using Terrarium.Menus;

namespace Terrarium.Ingame;

internal sealed class AccessibleMapMenuState : AccessibleMenuState
{
	private readonly record struct MapPoint(string Name, Vector2 WorldPosition, string Kind);

	internal AccessibleMapMenuState(AccessibleMenuController controller)
		: base(controller)
	{
	}

	protected override string Title => $"World map: {Main.worldName}";

	protected override void BuildEntries(List<AccessibleMenuEntry> entries)
	{
		Player player = Main.LocalPlayer;
		entries.Add(new(
			() => $"Current location: {DescribeBiome(player)}",
			description: () => WorldPositionFormatter.DescribeCoordinates(player.Center),
			role: "status"));

		Vector2 spawn = new(Main.spawnTileX * 16f, Main.spawnTileY * 16f);
		entries.Add(CreatePointEntry(new MapPoint("World spawn", spawn, "landmark")));
		if (player.showLastDeath)
		{
			entries.Add(CreatePointEntry(new MapPoint("Last death", player.lastDeathPostion, "death marker")));
		}

		AddPylons(entries);
		AddPlayers(entries);
		AddBosses(entries);
		AddTownNpcs(entries);

		entries.Add(new(
			() => "Close map",
			activate: CloseMap,
			description: () => "Return to gameplay."));
	}

	protected override void GoBack()
	{
		CloseMap();
	}

	protected override void OpenContextHelp()
	{
		Announce(
			"Accessible world map. Up and Down move through the current location, spawn, last death marker, discovered pylons, active players, bosses, and town NPCs. " +
			"Each entry reports distance and direction from you. Enter on a pylon requests normal pylon travel; Terraria still applies proximity, town, biome, and danger requirements. " +
			"This semantic map does not expose individual background tiles or arbitrary mouse coordinates. Escape closes the map.");
	}

	private static AccessibleMenuEntry CreatePointEntry(MapPoint point)
	{
		return new AccessibleMenuEntry(
			() => $"{point.Name}, {WorldPositionFormatter.DescribeRelativePosition(point.WorldPosition)}",
			description: () => $"{point.Kind}. {WorldPositionFormatter.DescribeCoordinates(point.WorldPosition)}",
			role: point.Kind);
	}

	private void AddPylons(List<AccessibleMenuEntry> entries)
	{
		foreach (TeleportPylonInfo pylon in Main.PylonSystem.Pylons)
		{
			TeleportPylonInfo captured = pylon;
			Vector2 position = captured.PositionInTiles.ToWorldCoordinates();
			string name = DescribePylon(captured);
			entries.Add(new(
				() => $"{name}, {WorldPositionFormatter.DescribeRelativePosition(position)}",
				activate: () => TeleportToPylon(captured, name),
				description: () => $"Pylon destination. {WorldPositionFormatter.DescribeCoordinates(position)} Enter requests teleportation.",
				role: "travel button"));
		}
	}

	private static void AddPlayers(List<AccessibleMenuEntry> entries)
	{
		for (int index = 0; index < Main.maxPlayers; index++)
		{
			Player player = Main.player[index];
			if (!player.active || player.whoAmI == Main.myPlayer)
			{
				continue;
			}

			MapPoint point = new(player.name, player.Center, "player");
			entries.Add(CreatePointEntry(point));
		}
	}

	private static void AddTownNpcs(List<AccessibleMenuEntry> entries)
	{
		for (int index = 0; index < Main.maxNPCs; index++)
		{
			NPC npc = Main.npc[index];
			if (!npc.active || !npc.isLikeATownNPC)
			{
				continue;
			}

			MapPoint point = new(npc.FullName, npc.Center, "town NPC");
			entries.Add(CreatePointEntry(point));
		}
	}

	private static void AddBosses(List<AccessibleMenuEntry> entries)
	{
		for (int index = 0; index < Main.maxNPCs; index++)
		{
			NPC npc = Main.npc[index];
			if (!npc.active || !npc.boss)
			{
				continue;
			}

			MapPoint point = new(npc.FullName, npc.Center, "boss");
			entries.Add(CreatePointEntry(point));
		}
	}

	private void TeleportToPylon(TeleportPylonInfo pylon, string name)
	{
		Main.PylonSystem.RequestTeleportation(pylon, Main.LocalPlayer);
		TerrariumMod.ScreenReader.Output($"Requested travel to {name}.");
		CloseMap();
	}

	private void CloseMap()
	{
		Controller.Close();
		Main.playerInventory = false;
	}

	private static string DescribePylon(TeleportPylonInfo pylon)
	{
		if (pylon.ModPylon is not null)
		{
			string name = Humanize(pylon.ModPylon.Name);
			return name.EndsWith("pylon", StringComparison.OrdinalIgnoreCase) ? name : name + " pylon";
		}

		int itemType = TETeleportationPylon.GetPylonItemTypeFromTileStyle((int)pylon.TypeOfPylon);
		return itemType > ItemID.None ? Lang.GetItemNameValue(itemType) : Humanize(pylon.TypeOfPylon.ToString()) + " pylon";
	}

	private static string DescribeBiome(Player player)
	{
		List<string> parts = [];
		if (player.ZoneDungeon) parts.Add("Dungeon");
		if (player.ZoneLihzhardTemple) parts.Add("Lihzahrd Temple");
		if (player.ZoneShimmer) parts.Add("Shimmer");
		if (player.ZoneBeach) parts.Add("Ocean");
		if (player.ZoneJungle) parts.Add("Jungle");
		if (player.ZoneSnow) parts.Add("Snow");
		if (player.ZoneDesert) parts.Add(player.ZoneUndergroundDesert ? "Underground Desert" : "Desert");
		if (player.ZoneGlowshroom) parts.Add("Glowing Mushroom");
		if (player.ZoneHallow) parts.Add("Hallow");
		if (player.ZoneCorrupt) parts.Add("Corruption");
		if (player.ZoneCrimson) parts.Add("Crimson");
		if (player.ZoneGraveyard) parts.Add("Graveyard");
		if (parts.Count == 0) parts.Add(player.ZoneForest ? "Forest" : "Purity");

		parts.Add(player.ZoneSkyHeight ? "Space" :
			player.ZoneOverworldHeight ? "Surface" :
			player.ZoneDirtLayerHeight ? "Underground" :
			player.ZoneRockLayerHeight ? "Caverns" : "Underworld");
		return string.Join(", ", parts);
	}

	private static string Humanize(string value)
	{
		System.Text.StringBuilder result = new();
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
