#nullable enable

using Terraria;

namespace Ariadne.Ingame.Controls;

/// <summary>
/// Which enemy the player currently holds, published for anything outside the targeting
/// controls that has to agree with them. The hostile-enemy tone is the reason it exists:
/// a held enemy keeps the tone however many other enemies close in, and the tone is
/// updated from a different hook than the lock is, so the lock has to be readable rather
/// than passed along.
///
/// The NPC's network id is kept beside its index because an index is a slot the game
/// reuses: something else spawning into a dead enemy's slot would otherwise inherit the
/// lock for as long as it took the tracker to notice.
/// </summary>
internal static class CombatTargetContext
{
	private static int _segmentIndex = -1;
	private static int _netId;

	internal static void Publish(int segmentIndex, int netId)
	{
		_segmentIndex = segmentIndex;
		_netId = netId;
	}

	internal static void Clear()
	{
		_segmentIndex = -1;
		_netId = 0;
	}

	/// <summary>
	/// The held enemy, if the slot still holds the same living NPC it was published for.
	/// </summary>
	internal static bool TryGetSegment(out int segmentIndex)
	{
		segmentIndex = _segmentIndex;
		if ((uint)_segmentIndex >= Main.maxNPCs)
		{
			return false;
		}

		NPC npc = Main.npc[_segmentIndex];
		return npc.active && npc.life > 0 && npc.netID == _netId;
	}
}
