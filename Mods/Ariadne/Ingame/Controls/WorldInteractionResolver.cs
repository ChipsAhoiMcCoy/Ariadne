#nullable enable

using System;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Audio;
using Terraria.ID;
using Terraria.Localization;

namespace Ariadne.Ingame.Controls;

internal enum WorldInteractionTargetKind
{
	Native,
	ConversationNpc,
	OldShakingChest,
}

internal readonly record struct WorldInteractionTarget(
	WorldInteractionTargetKind Kind,
	NPC? Npc = null)
{
	internal bool IsEntity => Npc is not null &&
		Kind is WorldInteractionTargetKind.ConversationNpc or WorldInteractionTargetKind.OldShakingChest;
}

/// <summary>
/// Resolves the semantic target of the secondary-use key before Terraria receives it.
/// Tile interaction remains native; only entity interactions that Terraria normally
/// performs during mouse-hover drawing are issued here, because the virtual pointer's
/// button is deliberately cleared before that draw pass.
/// </summary>
internal static class WorldInteractionResolver
{
	internal static WorldInteractionTarget ResolvePrecision(Point tilePosition, Player player)
	{
		Vector2 cursorPosition = new(tilePosition.X * 16f + 8f, tilePosition.Y * 16f + 8f);
		return FindEntityAt(cursorPosition, player) is { } entity
			? entity
			: new(WorldInteractionTargetKind.Native);
	}

	internal static WorldInteractionTarget ResolveSmart(Player player)
	{
		// A genuine Smart Interact result wins before proximity. A tile or projectile
		// result is returned to Terraria untouched; an NPC result has to be performed
		// here because the virtual mouse button does not survive into hover drawing.
		if (Main.SmartInteractShowingGenuine)
		{
			if (Main.SmartInteractNPC >= 0 && Main.SmartInteractNPC < Main.maxNPCs)
			{
				NPC npc = Main.npc[Main.SmartInteractNPC];
				if (npc.active && TryClassify(npc, player, out WorldInteractionTarget target))
				{
					return target;
				}
			}

			return new(WorldInteractionTargetKind.Native);
		}

		return NpcConversation.FindNearestSpeakable(player) is NPC nearby
			? new(WorldInteractionTargetKind.ConversationNpc, nearby)
			: new(WorldInteractionTargetKind.Native);
	}

	internal static WorldInteractionTarget? FindEntityAt(Vector2 worldPosition, Player player)
	{
		WorldInteractionTarget? nearest = null;
		float nearestDistanceSquared = float.PositiveInfinity;
		Point cursor = worldPosition.ToPoint();
		for (int index = 0; index < Main.maxNPCs; index++)
		{
			NPC npc = Main.npc[index];
			if (!npc.active ||
				!npc.Hitbox.Contains(cursor) ||
				!TryClassify(npc, player, out WorldInteractionTarget target))
			{
				continue;
			}

			float distanceSquared = Vector2.DistanceSquared(worldPosition, npc.Center);
			if (distanceSquared < nearestDistanceSquared)
			{
				nearestDistanceSquared = distanceSquared;
				nearest = target;
			}
		}

		return nearest;
	}

	/// <summary>
	/// Finds any NPC under the exact cursor point for description only. Interactable
	/// entities win even when another NPC's overlapping hitbox is closer, keeping spoken
	/// focus aligned with what secondary use will activate.
	/// </summary>
	internal static NPC? FindNpcAt(Vector2 worldPosition, Player player)
	{
		if (FindEntityAt(worldPosition, player) is WorldInteractionTarget { Npc: NPC interactive })
		{
			return interactive;
		}

		NPC? nearest = null;
		float nearestDistanceSquared = float.PositiveInfinity;
		Point cursor = worldPosition.ToPoint();
		for (int index = 0; index < Main.maxNPCs; index++)
		{
			NPC npc = Main.npc[index];
			if (!npc.active || !npc.Hitbox.Contains(cursor))
			{
				continue;
			}

			float distanceSquared = Vector2.DistanceSquared(worldPosition, npc.Center);
			if (distanceSquared < nearestDistanceSquared)
			{
				nearestDistanceSquared = distanceSquared;
				nearest = npc;
			}
		}

		return nearest;
	}

	/// <summary>
	/// Performs only the entity cases. False means Terraria should receive the key and
	/// run its ordinary exact-tile interaction path.
	/// </summary>
	internal static bool TryActivate(
		WorldInteractionTarget target,
		Player player,
		out string? failureAnnouncement)
	{
		failureAnnouncement = null;
		if (target.Npc is not NPC npc || !npc.active)
		{
			return false;
		}

		switch (target.Kind)
		{
			case WorldInteractionTargetKind.ConversationNpc:
				NpcConversation.Begin(npc, player, playChatSound: true);
				return true;
			case WorldInteractionTargetKind.OldShakingChest:
				return TryFreeOldShakingChest(npc, player, out failureAnnouncement);
			default:
				return false;
		}
	}

	private static bool TryClassify(
		NPC npc,
		Player player,
		out WorldInteractionTarget target)
	{
		if (npc.type == NPCID.BoundTownSlimeOld)
		{
			target = new(WorldInteractionTargetKind.OldShakingChest, npc);
			return true;
		}

		if (NpcConversation.CanChat(npc) &&
			NpcConversation.IsWithinConversationReach(player, npc))
		{
			target = new(WorldInteractionTargetKind.ConversationNpc, npc);
			return true;
		}

		target = default;
		return false;
	}

	private static bool TryFreeOldShakingChest(
		NPC npc,
		Player player,
		out string? failureAnnouncement)
	{
		failureAnnouncement = null;
		bool inVoidBag;
		int slot = player.FindItemInInventoryOrOpenVoidBag(ItemID.GoldenKey, out inVoidBag);
		if (slot < 0)
		{
			failureAnnouncement = Language.GetTextValue(
				"Mods.Ariadne.Announcements.OldShakingChestNeedsKey");
			return true;
		}

		Item key = inVoidBag ? player.bank4.item[slot] : player.inventory[slot];
		key.stack--;
		if (key.stack <= 0)
		{
			key.TurnToAir();
		}

		Recipe.FindRecipes();
		NPC.TransformElderSlime(npc.whoAmI);
		SoundEngine.PlaySound(SoundID.Unlock, npc.Center);
		return true;
	}
}
