#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Ariadne.Ingame.Status;
using Ariadne.Menus;

namespace Ariadne.Ingame;

internal sealed class AccessibleWorldPlayerStatusMenuState : AccessibleMenuState
{
	private int _informationAccessoryCount;

	internal AccessibleWorldPlayerStatusMenuState(AccessibleMenuController controller)
		: base(controller)
	{
	}

	protected override string Title => "World and Player Status";

	protected override void BuildEntries(List<AccessibleMenuEntry> entries)
	{
		Player player = Main.LocalPlayer;
		_informationAccessoryCount = InformationAccessoryStatusProvider.GetActiveStatuses().Count;

		entries.Add(Status(
			() => $"World: {Main.worldName}, {WorldDifficulty()}, {(Main.hardMode ? "Hardmode" : "Pre-Hardmode")}",
			() => $"Character: {player.name}, {CharacterDifficulty(player)}."));
		entries.Add(Status(
			() => $"World events: {WorldEvents()}",
			() => "Reports conspicuous active world events that Terraria ordinarily announces or displays."));
		entries.Add(Status(
			() => PlayerStatusReadout.MoonPhaseLine(),
			() => "The moon's current phase, which any player can see in the night sky. Unlike the other clock and sky readings this one does not require its Sextant."));
		entries.Add(Status(
			() => $"Biome: {BiomeStatusFormatter.Capture(player).Description}",
			() => "The current environmental biome. Exact horizontal position, depth, time, weather, and similar readings remain restricted to their informational accessories."));
		entries.Add(Status(
			() => $"Health: {Math.Max(0, player.statLife)} of {player.statLifeMax2}",
			() => $"{Percentage(player.statLife, player.statLifeMax2)} percent health."));
		entries.Add(Status(
			() => $"Mana: {Math.Max(0, player.statMana)} of {player.statManaMax2}",
			() => $"{Percentage(player.statMana, player.statManaMax2)} percent mana."));
		entries.Add(Status(
			() => $"Defense: {(int)player.statDefense}",
			() => $"Damage reduction from endurance: {Math.Round(player.endurance * 100f)} percent."));
		entries.Add(Status(
			() => PlayerStatusReadout.SetBonusLine(player),
			() => "The bonus granted by the currently equipped armor set, including modded sets."));

		if (player.breath < player.breathMax)
		{
			entries.Add(Status(
				() => $"Breath: {Math.Max(0, player.breath)} of {player.breathMax}",
				() => $"{Percentage(player.breath, player.breathMax)} percent breath remaining."));
		}

		entries.Add(new(
			() => $"Current buffs: {ActiveBuffCount(player)}",
			() => Controller.Navigate(new AccessibleBuffStatusMenuState(Controller)),
			description: () => "Review every active buff and debuff, including its description and displayed remaining duration.",
			role: "submenu"));
		entries.Add(new(
			() => $"Minions and sentries: {SummonSummary(player)}",
			() => Controller.Navigate(new AccessibleSummonStatusMenuState(Controller)),
			description: () => "Review minion-slot capacity and each currently summoned minion or sentry.",
			role: "submenu"));
		entries.Add(new(
			() => $"Information accessories: {_informationAccessoryCount} active",
			() => Controller.Navigate(new AccessibleInformationAccessoryMenuState(Controller)),
			description: () => "Review only readings currently granted by active informational accessories, nearby clocks, team sharing, and mod-provided information displays.",
			role: "submenu"));
		entries.Add(Status(
			() => $"Active bosses: {BossSummary()}",
			() => BossDetails()));
	}

	private static AccessibleMenuEntry Status(Func<string> label, Func<string>? description = null) =>
		new(label, description: description, role: "status");

	private static int Percentage(int current, int maximum) => maximum <= 0
		? 0
		: Math.Clamp((int)Math.Round(current * 100d / maximum), 0, 100);

	private static string WorldDifficulty()
	{
		if (Main.GameModeInfo.IsJourneyMode) return "Journey world";
		if (Main.GameModeInfo.IsMasterMode) return "Master world";
		if (Main.GameModeInfo.IsExpertMode) return "Expert world";
		return "Classic world";
	}

	private static string CharacterDifficulty(Player player) => player.difficulty switch
	{
		1 => "Mediumcore character",
		2 => "Hardcore character",
		3 => "Journey character",
		_ => "Classic character",
	};

	private static string WorldEvents()
	{
		List<string> events = [];
		if (Main.bloodMoon && !Main.dayTime) events.Add("Blood Moon");
		if (Main.eclipse && Main.dayTime) events.Add("Solar Eclipse");
		if (Main.pumpkinMoon) events.Add("Pumpkin Moon");
		if (Main.snowMoon) events.Add("Frost Moon");
		if (Main.slimeRain) events.Add("Slime Rain");
		if (Main.invasionType > 0) events.Add(InvasionName(Main.invasionType));
		return events.Count == 0 ? "none" : string.Join(", ", events);
	}

	private static string InvasionName(int type) => type switch
	{
		1 => "Goblin Army",
		2 => "Frost Legion",
		3 => "Pirate Invasion",
		4 => "Martian Madness",
		_ => "modded invasion",
	};

	private static int ActiveBuffCount(Player player)
	{
		int count = 0;
		for (int slot = 0; slot < player.buffType.Length; slot++)
		{
			if (player.buffType[slot] > 0 && player.buffTime[slot] > 0)
			{
				count++;
			}
		}
		return count;
	}

	private static string SummonSummary(Player player)
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
		return $"{minions} {Plural(minions, "minion")}, {sentries} {Plural(sentries, "sentry")}";
	}

	private static List<NPC> ActiveBosses() => Main.npc
		.Where(npc => npc.active && npc.boss)
		.ToList();

	private static string BossSummary()
	{
		List<NPC> bosses = ActiveBosses();
		return bosses.Count == 0
			? "none"
			: string.Join(", ", bosses.Select(boss => boss.FullName).Distinct());
	}

	private static string BossDetails()
	{
		List<NPC> bosses = ActiveBosses();
		return bosses.Count == 0
			? "No boss health bars are currently active."
			: string.Join(". ", bosses.Select(boss =>
				$"{boss.FullName}: {Math.Max(0, boss.life)} of {boss.lifeMax} health")) + ".";
	}

	internal static string Plural(int count, string singular) => count == 1 ? singular : singular + "s";
}

internal sealed class AccessibleBuffStatusMenuState : AccessibleMenuState
{
	private readonly record struct BuffStatus(string Name, string Description, string Duration, bool IsDebuff);

	internal AccessibleBuffStatusMenuState(AccessibleMenuController controller)
		: base(controller)
	{
	}

	protected override string Title => "Current Player Buffs";

	protected override void BuildEntries(List<AccessibleMenuEntry> entries)
	{
		List<BuffStatus> buffs = CaptureBuffs(Main.LocalPlayer);
		if (buffs.Count == 0)
		{
			entries.Add(new(
				() => "No active buffs or debuffs",
				description: () => "Return to the status menu to review another category.",
				role: "status"));
			return;
		}

		foreach (BuffStatus buff in buffs)
		{
			BuffStatus captured = buff;
			entries.Add(new(
				() => captured.Duration.Length == 0
					? captured.Name
					: $"{captured.Name}, {captured.Duration} remaining",
				description: () => captured.Description,
				role: captured.IsDebuff ? "debuff" : "buff"));
		}
	}

	private static List<BuffStatus> CaptureBuffs(Player player)
	{
		List<BuffStatus> result = [];
		for (int slot = 0; slot < player.buffType.Length; slot++)
		{
			int type = player.buffType[slot];
			if (type <= 0 || player.buffTime[slot] <= 0)
			{
				continue;
			}

			string name = Lang.GetBuffName(type);
			string description = Main.GetBuffTooltip(player, type);
			int rarity = 0;
			BuffLoader.ModifyBuffText(type, ref name, ref description, ref rarity);
			string duration = Main.TryGetBuffTime(slot, out int ticks) && ticks > 2
				? FormatDuration(ticks)
				: string.Empty;
			result.Add(new BuffStatus(name, description, duration, Main.debuff[type]));
		}
		return result;
	}

	private static string FormatDuration(int ticks)
	{
		TimeSpan duration = TimeSpan.FromSeconds(Math.Max(1d, Math.Ceiling(ticks / 60d)));
		List<string> parts = [];
		if (duration.Days > 0) parts.Add($"{duration.Days} {AccessibleWorldPlayerStatusMenuState.Plural(duration.Days, "day")}");
		if (duration.Hours > 0) parts.Add($"{duration.Hours} {AccessibleWorldPlayerStatusMenuState.Plural(duration.Hours, "hour")}");
		if (duration.Minutes > 0) parts.Add($"{duration.Minutes} {AccessibleWorldPlayerStatusMenuState.Plural(duration.Minutes, "minute")}");
		if (parts.Count == 0 || duration.Seconds > 0 && parts.Count < 2)
		{
			parts.Add($"{duration.Seconds} {AccessibleWorldPlayerStatusMenuState.Plural(duration.Seconds, "second")}");
		}
		return string.Join(", ", parts.Take(2));
	}
}

internal sealed class AccessibleSummonStatusMenuState : AccessibleMenuState
{
	private readonly record struct SummonStatus(
		string Name,
		bool IsSentry,
		int Count,
		float Slots,
		int MinimumDamage,
		int MaximumDamage);

	internal AccessibleSummonStatusMenuState(AccessibleMenuController controller)
		: base(controller)
	{
	}

	protected override string Title => "Minions and Sentries";

	protected override void BuildEntries(List<AccessibleMenuEntry> entries)
	{
		Player player = Main.LocalPlayer;
		entries.Add(new(
			() => $"Minion slots: {FormatSlots(player.slotsMinions)} used of {player.maxMinions}",
			description: () => "Minions consume the slot amount defined by the summoned projectile.",
			role: "status"));

		List<SummonStatus> summons = CaptureSummons(player);
		int sentryCount = summons.Where(summon => summon.IsSentry).Sum(summon => summon.Count);
		entries.Add(new(
			() => $"Sentry capacity: {sentryCount} active of {player.maxTurrets}",
			description: () => "Sentries use one sentry capacity each.",
			role: "status"));

		if (summons.Count == 0)
		{
			entries.Add(new(
				() => "No active minions or sentries",
				description: () => "Pets and light pets that do not consume minion or sentry capacity are not included.",
				role: "status"));
			return;
		}

		foreach (SummonStatus summon in summons)
		{
			SummonStatus captured = summon;
			entries.Add(new(
				() => SummonLabel(captured),
				description: () => SummonDescription(captured),
				role: captured.IsSentry ? "sentry" : "minion"));
		}
	}

	private static List<SummonStatus> CaptureSummons(Player player)
	{
		return Main.projectile
			.Where(projectile =>
				projectile.active &&
				projectile.owner == player.whoAmI &&
				(projectile.minion || projectile.sentry))
			.GroupBy(projectile => (projectile.type, projectile.sentry))
			.Select(group => new SummonStatus(
				group.First().Name,
				group.Key.sentry,
				group.Count(),
				group.Sum(projectile => projectile.minionSlots),
				group.Min(projectile => projectile.damage),
				group.Max(projectile => projectile.damage)))
			.OrderBy(summon => summon.IsSentry)
			.ThenBy(summon => summon.Name)
			.ToList();
	}

	private static string SummonLabel(SummonStatus summon)
	{
		string count = $"{summon.Count} active";
		string slots = summon.IsSentry ? string.Empty : $", {FormatSlots(summon.Slots)} slots";
		return $"{summon.Name}: {count}{slots}";
	}

	private static string SummonDescription(SummonStatus summon)
	{
		string kind = summon.IsSentry ? "Sentry" : "Minion";
		string damage = summon.MinimumDamage == summon.MaximumDamage
			? $"{summon.MinimumDamage} current damage each"
			: $"{summon.MinimumDamage} to {summon.MaximumDamage} current damage";
		return $"{kind}. {damage}.";
	}

	private static string FormatSlots(float slots) => slots.ToString("0.##");
}

internal sealed class AccessibleInformationAccessoryMenuState : AccessibleMenuState
{
	internal AccessibleInformationAccessoryMenuState(AccessibleMenuController controller)
		: base(controller)
	{
	}

	protected override string Title => "Information Accessories";

	protected override void BuildEntries(List<AccessibleMenuEntry> entries)
	{
		List<InformationAccessoryStatus> statuses = InformationAccessoryStatusProvider.GetActiveStatuses();
		if (statuses.Count == 0)
		{
			entries.Add(new(
				() => "No information readings are currently available",
				description: () => "Equip an informational accessory, stand near a clock, or receive shared information from a teammate to make its reading available.",
				role: "status"));
			return;
		}

		foreach (InformationAccessoryStatus status in statuses)
		{
			InformationAccessoryStatus captured = status;
			entries.Add(new(
				() => $"{captured.Name}: {captured.Value}",
				description: () => "This reading is available because its Terraria or mod-provided information display is currently active.",
				role: "status"));
		}
	}
}
