#nullable enable

using System.Collections.Generic;
using System.Linq;
using Terraria;

namespace Ariadne.Ingame.Status;

internal readonly record struct SummonInstanceStatus(
	int Type,
	string Name,
	bool IsSentry,
	int Count,
	float Slots,
	int MinimumDamage,
	int MaximumDamage);

/// <summary>
/// One definition of an active summon for both requested status and immediate
/// feedback. Capacity-bearing projectiles are counted; zero-slot decorative pieces
/// of a multipart minion are not separate minions.
/// </summary>
internal sealed class SummonStatusSnapshot
{
	private SummonStatusSnapshot(List<SummonInstanceStatus> groups)
	{
		Groups = groups;
		MinionCount = groups.Where(group => !group.IsSentry).Sum(group => group.Count);
		SentryCount = groups.Where(group => group.IsSentry).Sum(group => group.Count);
	}

	internal List<SummonInstanceStatus> Groups { get; }
	internal int MinionCount { get; }
	internal int SentryCount { get; }

	internal static SummonStatusSnapshot Capture(Player player)
	{
		List<SummonInstanceStatus> groups = Main.projectile
			.Where(projectile =>
				projectile.active &&
				projectile.owner == player.whoAmI &&
				IsCapacityBearing(projectile))
			.GroupBy(projectile => (projectile.type, projectile.sentry))
			.Select(group => new SummonInstanceStatus(
				group.Key.type,
				group.First().Name,
				group.Key.sentry,
				group.Count(),
				group.Sum(projectile => projectile.minionSlots),
				group.Min(projectile => projectile.damage),
				group.Max(projectile => projectile.damage)))
			.OrderBy(group => group.IsSentry)
			.ThenBy(group => group.Name)
			.ToList();
		return new(groups);
	}

	internal static bool IsCapacityBearing(Projectile projectile) =>
		projectile.sentry || projectile.minion && projectile.minionSlots > 0f;
}
