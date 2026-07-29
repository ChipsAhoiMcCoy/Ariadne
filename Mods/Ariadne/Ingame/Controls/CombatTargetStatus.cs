#nullable enable

using Terraria;
using Terraria.ID;
using Terraria.Localization;

namespace Ariadne.Ingame.Controls;

/// <summary>
/// What a combat target is, beyond its name and its health: which part of a larger boss
/// it is, and whether it can be hurt right now.
///
/// Both questions only matter because sight normally answers them. A player watching the
/// fight sees Moon Lord's hands from its head, and sees a pillar's shield flare when a
/// shot bounces off it. Neither reaches a name and a health total, so both are stated.
/// </summary>
internal static class CombatTargetStatus
{
	/// <summary>
	/// Whether attacks land on this segment at this moment. Vanilla drives a great deal
	/// of boss structure through this one flag: a pillar raises it until its shield is
	/// spent, Moon Lord's core holds it until all three eyes are dead, and Moon Lord's
	/// hands and head raise and drop it with the animation that shuts their eye.
	/// </summary>
	internal static bool CanBeHit(NPC npc) => !npc.dontTakeDamage;

	/// <summary>
	/// The remaining shield on a Lunar pillar. The four counts are world state rather
	/// than NPC state, so they are read per tower type rather than off the segment.
	/// </summary>
	internal static bool TryGetLunarShield(NPC npc, out int strength, out int maximum)
	{
		maximum = NPC.ShieldStrengthTowerMax;
		strength = LunarShieldSlot(npc.type) switch
		{
			0 => NPC.ShieldStrengthTowerSolar,
			1 => NPC.ShieldStrengthTowerVortex,
			2 => NPC.ShieldStrengthTowerNebula,
			3 => NPC.ShieldStrengthTowerStardust,
			_ => -1,
		};
		return strength >= 0;
	}

	/// <summary>
	/// A stable index per pillar, so a watcher can keep one previous value each without
	/// holding a dictionary keyed on NPC type.
	/// </summary>
	internal static int LunarShieldSlot(int npcType) => npcType switch
	{
		NPCID.LunarTowerSolar => 0,
		NPCID.LunarTowerVortex => 1,
		NPCID.LunarTowerNebula => 2,
		NPCID.LunarTowerStardust => 3,
		_ => -1,
	};

	internal const int LunarShieldSlotCount = 4;

	/// <summary>
	/// Which part of a multi-part boss this segment is, or null when the segment's own
	/// name already says. Only Moon Lord needs it among vanilla bosses: its head, both
	/// hands, its core, and the eye that breaks loose all display the single name
	/// "Moon Lord", so cycling through the fight otherwise reads the same two words four
	/// times over and never says which one the lock just took.
	///
	/// The hands are told apart by the side they spawned on, which is fixed for the whole
	/// fight, and named for that side of the world rather than for the boss's own left and
	/// right, to stay in the compass the rest of the mod speaks in.
	/// </summary>
	internal static string? DescribePart(NPC npc) => npc.type switch
	{
		NPCID.MoonLordHead => Text("PartHead"),
		NPCID.MoonLordHand => Text(npc.ai[2] == 0f ? "PartWestHand" : "PartEastHand"),
		NPCID.MoonLordCore => Text("PartCore"),
		NPCID.MoonLordFreeEye => Text("PartTrueEye"),
		_ => null,
	};

	private static string Text(string key) => Language.GetTextValue($"Mods.Ariadne.CombatTarget.{key}");
}
