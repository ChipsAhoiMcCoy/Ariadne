#nullable enable

using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Audio;
using Terraria.ID;
using Terraria.ModLoader;

namespace Ariadne.Ingame.Controls;

/// <summary>
/// Opens a conversation with a town NPC the way vanilla does, without going through the
/// mouse.
///
/// Vanilla decides this in <c>Main.HoverOverNPCs</c>, which runs during drawing and asks
/// two questions: is the pointer inside the NPC's hover box, and is <c>Main.mouseRight</c>
/// still set. Neither survives a keyboard cursor. <see cref="WorldCursorSystem"/> withholds
/// the mouse buttons after the player update so a virtual cursor resting over the hotbar
/// cannot click it, and that withholding outlives the update into the draw pass where the
/// question is asked. Smart Cursor fails the first question as well, because it aims at the
/// edge of the viewport rather than at whatever the player means to talk to.
///
/// So the interaction is issued directly instead. What replaces the hover test is
/// proximity, which is the same rectangle vanilla already uses to decide whether an NPC is
/// close enough to be talked to at all, and it is the right question for a listener:
/// standing next to someone is how you address them, and requiring a cursor to be parked on
/// their sprite first would charge aim as a toll on a conversation.
/// </summary>
internal static class NpcConversation
{
	/// <summary>
	/// The nearest NPC that would answer if spoken to, or null. Range is vanilla's own
	/// tile-interaction box around the player, so a conversation begins exactly where
	/// vanilla would have allowed a right-click to begin one.
	/// </summary>
	internal static NPC? FindNearestSpeakable(Player player)
	{
		if (!player.active || player.dead)
		{
			return null;
		}

		// Vanilla withholds NPC interaction entirely while the wiring tool is deployed.
		if (player.ownedProjectileCounts[ProjectileID.WireKite] > 0)
		{
			return null;
		}

		Rectangle reach = new(
			(int)(player.position.X + player.width / 2 - Player.tileRangeX * 16),
			(int)(player.position.Y + player.height / 2 - Player.tileRangeY * 16),
			Player.tileRangeX * 16 * 2,
			Player.tileRangeY * 16 * 2);

		NPC? nearest = null;
		float nearestDistanceSquared = float.PositiveInfinity;
		for (int index = 0; index < Main.maxNPCs; index++)
		{
			NPC npc = Main.npc[index];
			if (!npc.active || index == player.talkNPC || !CanChat(npc))
			{
				continue;
			}

			Rectangle bounds = new((int)npc.position.X, (int)npc.position.Y, npc.width, npc.height);
			if (!reach.Intersects(bounds))
			{
				continue;
			}

			float distanceSquared = Vector2.DistanceSquared(npc.Center, player.Center);
			if (distanceSquared < nearestDistanceSquared)
			{
				nearestDistanceSquared = distanceSquared;
				nearest = npc;
			}
		}
		return nearest;
	}

	/// <summary>
	/// Vanilla's own chat eligibility, including the captive NPCs that are freed by talking
	/// to them rather than by any other interaction, and any mod override of either.
	/// </summary>
	private static bool CanChat(NPC npc)
	{
		bool vanillaDefault = npc.townNPC ||
			npc.type is NPCID.BoundGoblin or NPCID.BoundWizard or NPCID.BoundMechanic
				or NPCID.WebbedStylist or NPCID.SleepingAngler or NPCID.BartenderUnconscious
				or NPCID.SkeletonMerchant or NPCID.GolferRescue;
		return NPCLoader.CanChat(npc).GetValueOrDefault(vanillaDefault);
	}

	/// <summary>
	/// Begins the conversation, clearing the same competing screens and menu state vanilla
	/// clears before it sets the talking NPC. <c>AccessibleIngameMenuSystem</c> promotes the
	/// result to its focused semantic menu on the following input update.
	/// </summary>
	internal static void Begin(NPC npc, Player player, bool playChatSound)
	{
		Main.CancelHairWindow();
		Main.SetNPCShopIndex(0);
		Main.InGuideCraftMenu = false;
		Main.InReforgeMenu = false;
		player.dropItemCheck();
		Main.npcChatCornerItem = 0;
		player.sign = -1;
		Main.editSign = false;
		player.SetTalkNPC(npc.whoAmI);
		player.chest = -1;
		Recipe.FindRecipes();
		Main.npcChatText = npc.GetChat();
		Main.playerInventory = false;
		if (playChatSound)
		{
			SoundEngine.PlaySound(SoundID.Chat);
		}
	}
}
