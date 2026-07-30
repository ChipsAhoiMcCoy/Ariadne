#nullable enable

using Terraria;
using Terraria.ModLoader;

namespace Ariadne.Ingame.Controls;

/// <summary>
/// Talks to whoever is standing next to the player when the interact key is pressed.
///
/// Secondary use is advertised as "secondary use or interact", and for tiles it is: the
/// mirrored mouse button reaches doors, chests and signs through the player update. NPCs are
/// the exception, because vanilla resolves those at draw time from a pointer position and a
/// mouse button that a keyboard cursor cannot supply. See <see cref="NpcConversation"/> for
/// why. Until this existed the scanner was the only way to reach a conversation at all, so
/// walking up to the Guide and pressing the interact key did nothing whatsoever.
///
/// A nearby NPC takes the key ahead of nothing else being consumed, matching vanilla, where
/// a right-click near a town NPC both talks and still reaches the held item. The
/// conversation itself is spoken by the semantic menu that opens on the next update.
/// </summary>
[Autoload(Side = ModSide.Client)]
internal sealed class NpcInteractionSystem : ModSystem
{
	public override void PostUpdateInput()
	{
		// Reading a mod keybind before PlayerInput has taken the mod's triggers into its
		// key status throws, and the exception costs every system that runs after this one
		// in the same update. The world gate below would catch it, but only after the read.
		if (Main.gameMenu)
		{
			return;
		}

		// The modifier turns this key into a chord that belongs to something else, and the
		// world gate already excludes a conversation that is open, so pressing interact
		// while talking cannot start the same conversation over.
		if (AriadneMod.SecondaryUseKeybind?.JustPressed != true ||
			AriadneMod.CombatTargetModifierKeybind?.Current == true ||
			!WorldInputContext.IsUnobstructedGameplay())
		{
			return;
		}

		Player player = Main.LocalPlayer;
		if (NpcConversation.FindNearestSpeakable(player) is NPC npc)
		{
			NpcConversation.Begin(npc, player, playChatSound: true);
		}
	}
}
