#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Terraria;
using Terraria.Localization;
using Ariadne.Accessibility;

namespace Ariadne.Ingame.Status;

internal readonly record struct PlayerStatusEntry(string Key, string Text);

/// <summary>
/// Builds the ordered character report spoken one entry per keypress. Readings
/// Terraria itself restricts to an informational accessory stay restricted here;
/// only the clock degrades to a rough phase of day rather than disappearing.
/// </summary>
internal static class PlayerStatusReadout
{
	internal static List<PlayerStatusEntry> Build(Player player)
	{
		List<PlayerStatusEntry> entries =
		[
			new("health", Health(player)),
			new("mana", Mana(player)),
			new("defense", Defense(player)),
			new("setbonus", SetBonusLine(player)),
			new("summons", Summons(player)),
			new("buffs", Buffs(player)),
			new("time", TimeOfDay()),
			new("moon", MoonPhaseLine()),
		];

		if (player.breath < player.breathMax)
		{
			entries.Add(new("breath", Breath(player)));
		}

		entries.Add(new("location", Location(player)));

		string? bosses = Bosses();
		if (bosses is not null)
		{
			entries.Add(new("bosses", bosses));
		}

		return entries;
	}

	private static string Health(Player player) => Text(
		"Health",
		Math.Max(0, player.statLife),
		player.statLifeMax2,
		Percentage(player.statLife, player.statLifeMax2));

	private static string Mana(Player player) => Text(
		"Mana",
		Math.Max(0, player.statMana),
		player.statManaMax2,
		Percentage(player.statMana, player.statManaMax2));

	private static string Defense(Player player) => Text(
		"Defense",
		(int)player.statDefense,
		(int)MathF.Round(player.endurance * 100f));

	/// <summary>
	/// The active armor set bonus, shared with the in-game status menu so both
	/// surfaces read the same wording.
	/// </summary>
	internal static string SetBonusLine(Player player)
	{
		string bonus = SpeechTextFormatter.Flatten(player.setBonus);
		return bonus.Length == 0 ? Text("NoSetBonus") : Text("SetBonus", bonus);
	}

	/// <summary>
	/// The moon phase, deliberately ungated because a sighted player reads it
	/// straight off the night sky without any accessory.
	/// </summary>
	internal static string MoonPhaseLine() =>
		Text("Moon", InformationAccessoryStatusProvider.MoonPhase());

	private static string Breath(Player player) => Text(
		"Breath",
		Math.Max(0, player.breath),
		player.breathMax,
		Percentage(player.breath, player.breathMax));

	private static string Summons(Player player)
	{
		int minions = 0;
		int sentries = 0;
		foreach (Projectile projectile in Main.projectile)
		{
			if (!projectile.active || projectile.owner != player.whoAmI)
			{
				continue;
			}
			if (projectile.sentry) sentries++;
			else if (projectile.minion) minions++;
		}

		return Text(
			"Summons",
			minions,
			player.slotsMinions.ToString("0.##"),
			player.maxMinions,
			sentries,
			player.maxTurrets);
	}

	private static string Buffs(Player player)
	{
		int buffs = 0;
		List<string> debuffs = [];
		for (int slot = 0; slot < player.buffType.Length; slot++)
		{
			int type = player.buffType[slot];
			if (type <= 0 || player.buffTime[slot] <= 0)
			{
				continue;
			}

			if (Main.debuff[type]) debuffs.Add(Lang.GetBuffName(type));
			else buffs++;
		}

		if (buffs == 0 && debuffs.Count == 0)
		{
			return Text("NoBuffs");
		}
		return debuffs.Count == 0
			? Text("Buffs", buffs)
			: Text("BuffsAndDebuffs", buffs, debuffs.Count, string.Join(", ", debuffs));
	}

	private static string TimeOfDay()
	{
		string phase = Text(PhaseKey(InformationAccessoryStatusProvider.CurrentHourOfDay()));
		string period = Text(Main.dayTime ? "Daytime" : "Nighttime");
		string? watch = InformationAccessoryStatusProvider.TryGetWatchTime();
		return watch is null
			? Text("TimeRough", phase, period)
			: Text("TimeWatch", watch, phase, period);
	}

	private static string PhaseKey(double hourOfDay) => hourOfDay switch
	{
		< 1.5d => "PhaseMidnight",
		< 4.5d => "PhaseLateNight",
		< 7.5d => "PhaseDawn",
		< 12d => "PhaseMorning",
		< 16d => "PhaseAfternoon",
		< 19.5d => "PhaseEvening",
		< 23d => "PhaseNight",
		_ => "PhaseMidnight",
	};

	private static string Location(Player player) => Text(
		"Location",
		BiomeStatusFormatter.Capture(player, includeElevation: true).Description);

	private static string? Bosses()
	{
		List<NPC> bosses = Main.npc.Where(npc => npc.active && npc.boss).ToList();
		if (bosses.Count == 0)
		{
			return null;
		}

		string details = string.Join(", ", bosses
			.GroupBy(boss => boss.FullName)
			.Select(group => Text(
				"BossHealth",
				group.Key,
				Percentage(
					group.Sum(boss => Math.Max(0, boss.life)),
					group.Sum(boss => boss.lifeMax)))));
		return Text("Bosses", details);
	}

	private static int Percentage(int current, int maximum) => maximum <= 0
		? 0
		: Math.Clamp((int)Math.Round(current * 100d / maximum), 0, 100);

	private static string Text(string key, params object[] arguments) =>
		Language.GetTextValue($"Mods.Ariadne.Status.{key}", arguments);
}
