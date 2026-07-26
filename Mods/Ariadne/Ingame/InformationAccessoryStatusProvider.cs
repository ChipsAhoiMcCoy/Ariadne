#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.GameContent.Events;
using Terraria.ID;
using Terraria.Localization;
using Terraria.Map;
using Terraria.ModLoader;

namespace Ariadne.Ingame;

internal readonly record struct InformationAccessoryStatus(string Name, string Value);

internal static class InformationAccessoryStatusProvider
{
	private static readonly InfoDisplay[] VanillaDisplays =
	[
		InfoDisplay.Watches,
		InfoDisplay.WeatherRadio,
		InfoDisplay.Sextant,
		InfoDisplay.FishFinder,
		InfoDisplay.MetalDetector,
		InfoDisplay.LifeformAnalyzer,
		InfoDisplay.Radar,
		InfoDisplay.TallyCounter,
		InfoDisplay.DPSMeter,
		InfoDisplay.Stopwatch,
		InfoDisplay.Compass,
		InfoDisplay.DepthMeter,
	];

	private static readonly HashSet<string> ReportedFailures = [];

	internal static List<InformationAccessoryStatus> GetActiveStatuses()
	{
		List<InformationAccessoryStatus> statuses = [];
		foreach (InfoDisplay display in VanillaDisplays.Concat(ModContent.GetContent<InfoDisplay>()))
		{
			try
			{
				if (!InfoDisplayLoader.Active(display))
				{
					continue;
				}

				string value = VanillaValue(display) ?? ModdedValue(display);
				string name = display.DisplayName.Value;
				Color textColor = Color.White;
				Color shadowColor = Color.Black;
				InfoDisplayLoader.ModifyDisplayParameters(
					display,
					ref value,
					ref name,
					ref textColor,
					ref shadowColor);
				if (!string.IsNullOrWhiteSpace(value))
				{
					statuses.Add(new InformationAccessoryStatus(name, value));
				}
			}
			catch (Exception exception)
			{
				if (ReportedFailures.Add(display.FullName))
				{
					ModContent.GetInstance<AriadneMod>().Logger.Warn(
						$"Could not read the {display.FullName} information display for the accessible status menu: {exception.Message}");
				}
			}
		}
		return statuses;
	}

	private static string? VanillaValue(InfoDisplay display)
	{
		Player player = Main.LocalPlayer;
		if (display == InfoDisplay.Watches) return Time(player);
		if (display == InfoDisplay.WeatherRadio) return Weather();
		if (display == InfoDisplay.Sextant) return MoonPhase();
		if (display == InfoDisplay.FishFinder) return FishingPower(player);
		if (display == InfoDisplay.MetalDetector) return Treasure();
		if (display == InfoDisplay.LifeformAnalyzer) return RareCreature(player);
		if (display == InfoDisplay.Radar) return NearbyEnemies(player);
		if (display == InfoDisplay.TallyCounter) return KillCount(player);
		if (display == InfoDisplay.DPSMeter) return DamagePerSecond(player);
		if (display == InfoDisplay.Stopwatch) return Speed(player);
		if (display == InfoDisplay.Compass) return Compass(player);
		if (display == InfoDisplay.DepthMeter) return Depth(player);
		return null;
	}

	private static string ModdedValue(InfoDisplay display)
	{
		Color textColor = Color.White;
		Color shadowColor = Color.Black;
		return display.DisplayValue(ref textColor, ref shadowColor);
	}

	private static string Time(Player player)
	{
		double clockTime = Main.time;
		if (!Main.dayTime)
		{
			clockTime += 54000d;
		}
		clockTime = clockTime / 86400d * 24d - 19.5d;
		if (clockTime < 0d)
		{
			clockTime += 24d;
		}

		bool afternoon = clockTime >= 12d;
		int hour = (int)clockTime;
		int minute = (int)((clockTime - hour) * 60d);
		if (player.accWatch == 1)
		{
			minute = 0;
		}
		else if (player.accWatch == 2)
		{
			minute = minute < 30 ? 0 : 30;
		}
		if (hour > 12) hour -= 12;
		if (hour == 0) hour = 12;

		string meridiem = Language.GetTextValue(afternoon ? "GameUI.TimePastMorning" : "GameUI.TimeAtMorning");
		return $"{hour}:{minute:00} {meridiem}";
	}

	private static string Weather()
	{
		string condition = Main.IsItStorming
			? Language.GetTextValue("GameUI.Storm")
			: Main.maxRaining > 0.6f
				? Language.GetTextValue("GameUI.HeavyRain")
				: Main.maxRaining >= 0.2f
					? Language.GetTextValue("GameUI.Rain")
					: Main.maxRaining > 0f
						? Language.GetTextValue("GameUI.LightRain")
						: Main.cloudBGActive > 0f
							? Language.GetTextValue("GameUI.Overcast")
							: Main.numClouds > 90
								? Language.GetTextValue("GameUI.MostlyCloudy")
								: Main.numClouds > 55
									? Language.GetTextValue("GameUI.Cloudy")
									: Main.numClouds <= 15
										? Language.GetTextValue("GameUI.Clear")
										: Language.GetTextValue("GameUI.PartlyCloudy");

		int wind = (int)(Main.windSpeedCurrent * 50f);
		if (wind < 0)
		{
			condition += Language.GetTextValue("GameUI.EastWind", Math.Abs(wind));
		}
		else if (wind > 0)
		{
			condition += Language.GetTextValue("GameUI.WestWind", wind);
		}

		return Sandstorm.Happening
			? $"{Language.GetTextValue("GameUI.Sandstorm")}; {condition}"
			: condition;
	}

	private static string MoonPhase() => Main.moonPhase switch
	{
		0 => Language.GetTextValue("GameUI.FullMoon"),
		1 => Language.GetTextValue("GameUI.WaningGibbous"),
		2 => Language.GetTextValue("GameUI.ThirdQuarter"),
		3 => Language.GetTextValue("GameUI.WaningCrescent"),
		4 => Language.GetTextValue("GameUI.NewMoon"),
		5 => Language.GetTextValue("GameUI.WaxingCrescent"),
		6 => Language.GetTextValue("GameUI.FirstQuarter"),
		_ => Language.GetTextValue("GameUI.WaxingGibbous"),
	};

	private static string FishingPower(Player player)
	{
		bool hasBobber = Main.projectile.Any(projectile =>
			projectile.active && projectile.owner == player.whoAmI && projectile.bobber);
		if (hasBobber)
		{
			return player.displayedFishingInfo;
		}

		PlayerFishingConditions conditions = player.GetFishingConditions();
		return conditions.BaitItemType == ItemID.TruffleWorm
			? Language.GetTextValue("GameUI.FishingWarning")
			: Language.GetTextValue("GameUI.FishingPower", conditions.FinalFishingLevel);
	}

	private static string Treasure()
	{
		if (Main.SceneMetrics.bestOre <= 0)
		{
			return Language.GetTextValue("GameUI.NoTreasureNearby");
		}

		int tileType = Main.SceneMetrics.bestOre;
		int baseOption = 0;
		if (Main.SceneMetrics.ClosestOrePosition is Point position)
		{
			Tile tile = Framing.GetTileSafely(position);
			if (tile.HasTile)
			{
				tileType = tile.TileType;
				MapHelper.GetTileBaseOption(position.X, position.Y, tile.TileType, tile, ref baseOption);
				if (TileID.Sets.BasicChest[tileType] || TileID.Sets.BasicChestFake[tileType])
				{
					baseOption = 0;
				}
			}
		}

		string treasure = Lang.GetMapObjectName(MapHelper.TileToLookup(tileType, baseOption));
		return Language.GetTextValue("GameUI.OreDetected", treasure);
	}

	private static string RareCreature(Player player)
	{
		NPC? rarest = null;
		const float maximumDistanceSquared = 1300f * 1300f;
		foreach (NPC npc in Main.npc)
		{
			if (npc.active &&
				npc.rarity > (rarest?.rarity ?? 0) &&
				Vector2.DistanceSquared(npc.Center, player.Center) < maximumDistanceSquared)
			{
				rarest = npc;
			}
		}
		return rarest?.GivenOrTypeName ?? Language.GetTextValue("GameUI.NoRareCreatures");
	}

	private static string NearbyEnemies(Player player)
	{
		const float maximumDistanceSquared = 2000f * 2000f;
		int count = Main.npc.Count(npc =>
			npc.active &&
			!npc.friendly &&
			npc.damage > 0 &&
			npc.lifeMax > 5 &&
			!npc.dontCountMe &&
			Vector2.DistanceSquared(npc.Center, player.Center) < maximumDistanceSquared);
		return count switch
		{
			0 => Language.GetTextValue("GameUI.NoEnemiesNearby"),
			1 => Language.GetTextValue("GameUI.OneEnemyNearby"),
			_ => Language.GetTextValue("GameUI.EnemiesNearby", count),
		};
	}

	private static string KillCount(Player player)
	{
		int bannerType = player.lastCreatureHit;
		if (bannerType <= 0 || bannerType >= NPC.killCount.Length)
		{
			return Language.GetTextValue("GameUI.NoKillCount");
		}
		return $"{Lang.GetNPCNameValue(Item.BannerToNPC(bannerType))}: {NPC.killCount[bannerType]}";
	}

	private static string DamagePerSecond(Player player)
	{
		player.checkDPSTime();
		int damage = player.getDPS();
		return damage == 0
			? Language.GetTextValue("GameUI.NoDPS")
			: Language.GetTextValue("GameUI.DPS", damage);
	}

	private static string Speed(Player player)
	{
		Vector2 velocity = player.velocity + player.instantMovementAccumulatedThisFrame;
		if (player.mount.Active &&
			player.mount.IsConsideredASlimeMount &&
			player.velocity.Y != 0f &&
			!player.SlimeDontHyperJump)
		{
			velocity.Y += player.velocity.Y;
		}

		float milesPerHour = velocity.Length() * 216000f / 42240f;
		if (!player.merman && !player.ignoreWater)
		{
			if (player.honeyWet) milesPerHour /= 4f;
			else if (player.wet) milesPerHour /= 2f;
		}
		return Language.GetTextValue("GameUI.Speed", Math.Round(milesPerHour));
	}

	private static string Compass(Player player)
	{
		int horizontal = (int)((player.position.X + player.width / 2f) * 2f / 16f - Main.maxTilesX);
		return horizontal > 0
			? Language.GetTextValue("GameUI.CompassEast", horizontal)
			: horizontal < 0
				? Language.GetTextValue("GameUI.CompassWest", -horizontal)
				: Language.GetTextValue("GameUI.CompassCenter");
	}

	private static string Depth(Player player)
	{
		int depth = (int)((player.position.Y + player.height) * 2f / 16f - Main.worldSurface * 2d);
		float worldScale = Main.maxTilesX / 4200f;
		worldScale *= worldScale;
		float surfaceLayer = (float)((player.Center.Y / 16f - (65f + 10f * worldScale)) / (Main.worldSurface / 5d));
		string layer = player.position.Y > (Main.maxTilesY - 204) * 16f
			? Language.GetTextValue("GameUI.LayerUnderworld")
			: player.position.Y > Main.rockLayer * 16d + 616d
				? Language.GetTextValue("GameUI.LayerCaverns")
				: depth > 0
					? Language.GetTextValue("GameUI.LayerUnderground")
					: surfaceLayer >= 1f
						? Language.GetTextValue("GameUI.LayerSurface")
						: Language.GetTextValue("GameUI.LayerSpace");
		depth = Math.Abs(depth);
		string measurement = depth == 0
			? Language.GetTextValue("GameUI.DepthLevel")
			: Language.GetTextValue("GameUI.Depth", depth);
		return $"{measurement} {layer}";
	}
}
