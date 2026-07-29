#nullable enable

using System;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;

namespace Ariadne.Ingame.Controls;

/// <summary>
/// Where the cursor goes for a locked target. This is Terraria's own lock-on aim rebuilt
/// against the same public sets, rather than a call into
/// <see cref="Terraria.GameInput.LockOnHelper"/>: that helper only aims while gamepad mode
/// owns the mouse, and it writes the pointer directly, which is the one thing Ariadne's
/// virtual cursor cannot share.
///
/// Every part of it is vanilla's. A worm is aimed at whichever of its segments is nearest
/// the player, because the chain is one health pool and the near link is the one actually
/// in reach. Any target is then led by its own velocity, so a moving enemy is not shot
/// behind. Weapons in <see cref="ItemID.Sets.LockOnAimAbove"/> aim over the target, and
/// weapons in <see cref="ItemID.Sets.LockOnAimCompensation"/> aim high and short so an
/// arcing shot falls onto it instead of into the ground in front of it.
///
/// Without this the lock pointed at a segment's exact centre, which is a miss for every
/// weapon vanilla compensates and for every enemy that is moving.
/// </summary>
internal static class CombatTargetAim
{
	/// <summary>The distance vanilla measures its velocity lead against.</summary>
	private const float LeadReferenceDistance = 2_000f;

	/// <summary>How many ticks ahead a target at that full distance is led.</summary>
	private const float LeadTicks = 45f;

	/// <summary>The distance vanilla's arc compensation is scaled against.</summary>
	private const float ArcReferenceDistance = 700f;

	/// <summary>The world height below which vanilla stops aiming further up.</summary>
	private const float SkyLimit = 100f;

	internal static Vector2 Resolve(Player player, int npcIndex)
	{
		if ((uint)npcIndex >= Main.maxNPCs)
		{
			return player.Center;
		}

		NPC target = Main.npc[npcIndex];
		Vector2 aim = target.Center;
		if (NPC.GetNPCLocation(npcIndex, seekHead: true, averageDirection: false, out int leadIndex, out Vector2 nearest) &&
			(uint)leadIndex < Main.maxNPCs)
		{
			NPC lead = Main.npc[leadIndex];
			aim = nearest + lead.Distance(player.Center) / LeadReferenceDistance * lead.velocity * LeadTicks;
		}

		Item item = player.HeldItem;
		// Vanilla refuses to aim this one at all: it places blocks, and a lock that pulled
		// them onto the enemy would fight what the item is being held for. The lock still
		// points at the target so it keeps saying where the target is; only the
		// weapon-shaped offsets below are withheld.
		if (item.type == ItemID.IceRod)
		{
			return aim;
		}

		return ApplyArcCompensation(ApplyAimAbove(aim, item), target, player, item);
	}

	/// <summary>
	/// Raises the aim by up to the item's configured number of tiles, stopping early at
	/// solid ground so the point cannot be walked inside a ceiling.
	/// </summary>
	private static Vector2 ApplyAimAbove(Vector2 aim, Item item)
	{
		if ((uint)item.type >= ItemID.Sets.LockOnAimAbove.Length)
		{
			return aim;
		}

		int remaining = ItemID.Sets.LockOnAimAbove[item.type];
		while (remaining > 0 && aim.Y > SkyLimit)
		{
			Point probe = aim.ToTileCoordinates();
			probe.Y -= 4;
			if (!WorldGen.InWorld(probe.X, probe.Y, 10) || WorldGen.SolidTile(probe.X, probe.Y))
			{
				break;
			}

			aim.Y -= 16f;
			remaining--;
		}

		return aim;
	}

	/// <summary>
	/// Offsets the aim up and back toward the player for weapons whose shots fall, by an
	/// amount that grows with the square of the distance the shot has to cover.
	/// </summary>
	private static Vector2 ApplyArcCompensation(Vector2 aim, NPC target, Player player, Item item)
	{
		if ((uint)item.type >= ItemID.Sets.LockOnAimCompensation.Length ||
			ItemID.Sets.LockOnAimCompensation[item.type] is not float compensation)
		{
			return aim;
		}

		aim.Y -= target.height / 2;
		Vector2 delta = aim - player.Center;
		Vector2 direction = delta.SafeNormalize(Vector2.Zero);
		direction.Y -= 1f;
		float reach = MathF.Pow(delta.Length() / ArcReferenceDistance, 2f) * ArcReferenceDistance;
		aim.Y += direction.Y * reach * compensation;
		aim.X -= direction.X * reach * compensation;
		return aim;
	}
}
