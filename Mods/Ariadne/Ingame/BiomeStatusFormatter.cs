#nullable enable

using System.Collections.Generic;
using System.Linq;
using Terraria;
using Terraria.ModLoader;

namespace Ariadne.Ingame;

internal readonly record struct BiomeStatusSnapshot(string Identity, string Description);

internal static class BiomeStatusFormatter
{
	internal static BiomeStatusSnapshot Capture(Player player, bool includeElevation = false)
	{
		List<(string Identity, string Name)> parts = [];

		Add(parts, player.ZoneDungeon, "Terraria/Dungeon", "Dungeon");
		Add(parts, player.ZoneLihzhardTemple, "Terraria/LihzahrdTemple", "Lihzahrd Temple");
		Add(parts, player.ZoneShimmer, "Terraria/Shimmer", "Shimmer");
		Add(parts, player.ZoneBeach, "Terraria/Ocean", "Ocean");
		Add(parts, player.ZoneJungle, "Terraria/Jungle", "Jungle");
		Add(parts, player.ZoneSnow, "Terraria/Snow", "Snow");
		if (player.ZoneUndergroundDesert)
		{
			parts.Add(("Terraria/UndergroundDesert", "Underground Desert"));
		}
		else
		{
			Add(parts, player.ZoneDesert, "Terraria/Desert", "Desert");
		}
		Add(parts, player.ZoneGlowshroom, "Terraria/GlowingMushroom", "Glowing Mushroom");
		Add(parts, player.ZoneHallow, "Terraria/Hallow", "Hallow");
		Add(parts, player.ZoneCorrupt, "Terraria/Corruption", "Corruption");
		Add(parts, player.ZoneCrimson, "Terraria/Crimson", "Crimson");
		Add(parts, player.ZoneGraveyard, "Terraria/Graveyard", "Graveyard");
		Add(parts, player.ZoneMeteor, "Terraria/Meteor", "Meteor");
		Add(parts, player.ZoneGranite, "Terraria/GraniteCave", "Granite Cave");
		Add(parts, player.ZoneMarble, "Terraria/MarbleCave", "Marble Cave");
		Add(parts, player.ZoneHive, "Terraria/BeeHive", "Bee Hive");
		Add(parts, player.ZoneGemCave, "Terraria/GemstoneCave", "Gemstone Cave");
		Add(parts, player.ZoneOldOneArmy, "Terraria/OldOnesArmy", "Old One's Army");
		Add(parts, player.ZoneTowerSolar, "Terraria/SolarPillar", "Solar Pillar");
		Add(parts, player.ZoneTowerVortex, "Terraria/VortexPillar", "Vortex Pillar");
		Add(parts, player.ZoneTowerNebula, "Terraria/NebulaPillar", "Nebula Pillar");
		Add(parts, player.ZoneTowerStardust, "Terraria/StardustPillar", "Stardust Pillar");

		foreach (ModBiome biome in ModContent.GetContent<ModBiome>().OrderBy(biome => biome.FullName))
		{
			if (player.InModBiome(biome))
			{
				parts.Add((biome.FullName, biome.DisplayName.Value));
			}
		}

		if (parts.Count == 0)
		{
			parts.Add(("Terraria/Purity", player.ZoneForest ? "Forest" : "Purity"));
		}

		if (includeElevation)
		{
			parts.Add(Elevation(player));
		}

		return new BiomeStatusSnapshot(
			string.Join('|', parts.Select(part => part.Identity)),
			string.Join(", ", parts.Select(part => part.Name)));
	}

	private static void Add(List<(string Identity, string Name)> parts, bool active, string identity, string name)
	{
		if (active)
		{
			parts.Add((identity, name));
		}
	}

	private static (string Identity, string Name) Elevation(Player player)
	{
		if (player.ZoneSkyHeight) return ("Terraria/Space", "Space");
		if (player.ZoneOverworldHeight) return ("Terraria/Surface", "Surface");
		if (player.ZoneDirtLayerHeight) return ("Terraria/Underground", "Underground");
		if (player.ZoneRockLayerHeight) return ("Terraria/Caverns", "Caverns");
		return ("Terraria/Underworld", "Underworld");
	}
}
