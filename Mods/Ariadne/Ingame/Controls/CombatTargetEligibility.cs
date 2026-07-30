#nullable enable

using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;

namespace Ariadne.Ingame.Controls;

/// <summary>
/// Whether an enemy can be locked onto. Kept apart from the tracker that uses it because
/// it is a policy rather than bookkeeping: most of it is a deliberate departure from
/// vanilla's own lock-on rule, and the reasoning below is what a later reader needs.
/// </summary>
internal static class CombatTargetEligibility
{
	/// <summary>
	/// Whether a living NPC already known to be on the field can be locked.
	/// </summary>
	internal static bool CanLockOn(Player player, NPC npc)
	{
		if (npc.friendly ||
			npc.isLikeATownNPC ||
			npc.immortal ||
			npc.CountsAsACritter)
		{
			return false;
		}

		// Deliberately no test for whether the thing can be hurt right now. Vanilla's
		// lock-on refuses anything holding dontTakeDamage, and that single flag is how
		// vanilla builds most boss structure: a Lunar pillar holds it until its shield is
		// spent, Moon Lord's core holds it until all three eyes are dead, and Moon Lord's
		// hands and head raise and drop it every time their eye shuts. Copying that rule
		// made those parts unreachable, which for a sighted player is a small annoyance
		// and here is the whole fight missing. They are offered instead, and the state is
		// spoken and sounded.
		//
		// What that rule did usefully was keep scenery out, so scenery is excluded
		// directly: something nothing can hurt and nothing will chase is not a target.
		if (npc.dontTakeDamage && !npc.chaseable)
		{
			return false;
		}

		// Vanilla's own lock-on rule, and worth keeping: a mimic still shaped like a chest
		// has not revealed itself yet.
		if (npc.aiStyle == NPCAIStyleID.Mimic && npc.ai[0] == 0f)
		{
			return false;
		}

		// Vanilla also requires the target to be lit. That is a sighted affordance, so it
		// is left out here: an enemy in the dark is one you still have to fight. Line of
		// sight below is the test that actually answers whether it can be hit.
		Item item = player.HeldItem;
		if ((uint)item.type < ItemID.Sets.LockOnIgnoresCollision.Length &&
			ItemID.Sets.LockOnIgnoresCollision[item.type])
		{
			return true;
		}

		Vector2 predicted = CombatTargetAim.Resolve(player, npc.whoAmI);
		return Collision.CanHit(player.Center, 0, 0, predicted, 0, 0) ||
			Collision.CanHitLine(player.Center, 0, 0, predicted, 0, 0) ||
			Collision.CanHit(player.Center, 0, 0, npc.Center, 0, 0) ||
			Collision.CanHitLine(player.Center, 0, 0, npc.Center, 0, 0);
	}
}
