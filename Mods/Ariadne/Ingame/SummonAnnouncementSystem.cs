#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Terraria;
using Terraria.DataStructures;
using Terraria.Localization;
using Terraria.ModLoader;
using Ariadne.Audio;
using Ariadne.Configs;
using Ariadne.Ingame.Status;

namespace Ariadne.Ingame;

[Autoload(Side = ModSide.Client)]
internal sealed class SummonAnnouncementSystem : ModSystem
{
	private const int WorldSynchronizationGraceTicks = 60;
	private readonly Dictionary<(int Type, bool IsSentry), string> _pending = [];
	private bool _armed;
	private int _warmupTicks;

	public override void OnWorldLoad() => Reset();

	public override void OnWorldUnload() => Reset();

	internal void ReportSpawn(Projectile projectile)
	{
		if (!_armed ||
			projectile.owner != Main.myPlayer ||
			!SummonStatusSnapshot.IsCapacityBearing(projectile))
		{
			return;
		}

		_pending[(projectile.type, projectile.sentry)] = projectile.Name;
	}

	public override void PostUpdatePlayers()
	{
		AriadneClientConfig config = ModContent.GetInstance<AriadneClientConfig>();
		Player player = Main.LocalPlayer;
		if (!config.SummonAnnouncementsEnabled ||
			!player.active ||
			player.dead ||
			player.ghost)
		{
			Reset();
			return;
		}
		if (!GameplayAudioGate.CanListen())
		{
			_pending.Clear();
			return;
		}

		if (!_armed)
		{
			_pending.Clear();
			if (--_warmupTicks <= 0)
			{
				_armed = true;
			}
			return;
		}
		if (_pending.Count == 0)
		{
			return;
		}

		SummonStatusSnapshot snapshot = SummonStatusSnapshot.Capture(player);
		List<string> names = _pending.Values
			.Where(name => !string.IsNullOrWhiteSpace(name))
			.Distinct(StringComparer.CurrentCultureIgnoreCase)
			.ToList();
		bool addedMinion = _pending.Keys.Any(key => !key.IsSentry);
		bool addedSentry = _pending.Keys.Any(key => key.IsSentry);
		_pending.Clear();

		List<string> clauses = [];
		if (names.Count > 0)
		{
			clauses.Add(Language.GetTextValue("Mods.Ariadne.Announcements.Summoned", JoinNames(names)));
		}
		if (addedMinion)
		{
			clauses.Add(Language.GetTextValue(
				"Mods.Ariadne.Announcements.MinionCapacity",
				snapshot.MinionCount,
				player.slotsMinions.ToString("0.##"),
				player.maxMinions));
		}
		if (addedSentry)
		{
			clauses.Add(Language.GetTextValue(
				"Mods.Ariadne.Announcements.SentryCapacity",
				snapshot.SentryCount,
				player.maxTurrets));
		}

		AriadneMod.ScreenReader.Output(string.Join(" ", clauses), interrupt: false);
	}

	private void Reset()
	{
		_pending.Clear();
		_armed = false;
		_warmupTicks = WorldSynchronizationGraceTicks;
	}

	private static string JoinNames(IReadOnlyList<string> names) => names.Count switch
	{
		0 => "summon",
		1 => names[0],
		2 => $"{names[0]} and {names[1]}",
		_ => $"{string.Join(", ", names.Take(names.Count - 1))}, and {names[^1]}",
	};
}

[Autoload(Side = ModSide.Client)]
internal sealed class SummonAnnouncementGlobalProjectile : GlobalProjectile
{
	public override void OnSpawn(Projectile projectile, IEntitySource source)
	{
		if (projectile.owner == Main.myPlayer)
		{
			ModContent.GetInstance<SummonAnnouncementSystem>().ReportSpawn(projectile);
		}
	}
}
