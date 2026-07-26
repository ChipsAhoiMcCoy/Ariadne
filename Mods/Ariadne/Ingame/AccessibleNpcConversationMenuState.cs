#nullable enable

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Audio;
using Terraria.ID;
using Terraria.ModLoader;
using Ariadne.Menus;

namespace Ariadne.Ingame;

internal sealed class AccessibleNpcConversationMenuState : AccessibleMenuState
{
	private readonly AccessibleInventoryController _inventoryController;
	private readonly Player _player;
	private readonly NPC _npc;
	private string _dialog;

	internal AccessibleNpcConversationMenuState(
		AccessibleMenuController controller,
		AccessibleInventoryController inventoryController,
		Player player,
		NPC npc,
		string dialog)
		: base(controller)
	{
		_inventoryController = inventoryController;
		_player = player;
		_npc = npc;
		_dialog = dialog;
	}

	protected override string Title => $"Conversation with {_npc.FullName}";

	protected override bool RightArrowOpensSubmenu => false;

	public override void OnActivate()
	{
		Main.playerInventory = false;
		Main.npcChatText = _dialog;
		base.OnActivate();
	}

	public override void Update(GameTime gameTime)
	{
		if (!ConversationIsActive())
		{
			CloseConversation(announce: true);
			return;
		}

		base.Update(gameTime);
	}

	protected override void BuildEntries(List<AccessibleMenuEntry> entries)
	{
		entries.Add(new AccessibleMenuEntry(
			() => string.IsNullOrWhiteSpace(_dialog) ? $"{_npc.FullName}: No current dialog" : $"{_npc.FullName}: {_dialog}",
			description: () => string.IsNullOrWhiteSpace(_dialog) ? $"Talking to {_npc.FullName}." : _dialog,
			role: "information"));

		_inventoryController.BuildNpcChatButtons(
			_player,
			_npc,
			out string firstButton,
			out Action? firstAction,
			out string secondButton,
			out Action? secondAction);
		NPCLoader.SetChatButtons(ref firstButton, ref secondButton);
		AddChatButton(entries, firstButton, firstButton: true, firstAction, invokeLoaderHooks: !NPCID.Sets.IsTownPet[_npc.type]);
		AddChatButton(entries, secondButton, firstButton: false, secondAction);

		if (!string.IsNullOrWhiteSpace(_player.currentShoppingSettings.HappinessReport))
		{
			entries.Add(new AccessibleMenuEntry(
				() => "Ask about happiness",
				ShowHappiness,
				description: () => "Hear how this character's surroundings affect shop prices."));
		}

		entries.Add(new AccessibleMenuEntry(
			() => "Close conversation",
			() => CloseConversation(announce: false),
			description: () => $"Stop talking to {_npc.FullName}."));
	}

	protected override void GoBack()
	{
		CloseConversation(announce: false);
	}

	private void AddChatButton(
		List<AccessibleMenuEntry> entries,
		string label,
		bool firstButton,
		Action? nativeAction,
		bool invokeLoaderHooks = true)
	{
		if (string.IsNullOrWhiteSpace(label))
		{
			return;
		}

		entries.Add(new AccessibleMenuEntry(
			() => label,
			() => ActivateChatButton(firstButton, nativeAction, invokeLoaderHooks),
			description: () => $"Activate {label}."));
	}

	private void ActivateChatButton(bool firstButton, Action? nativeAction, bool invokeLoaderHooks)
	{
		string returnDialog = _dialog;
		if (invokeLoaderHooks)
		{
			AccessibleInventoryController.ActivateVanillaChatButton(firstButton, nativeAction);
		}
		else
		{
			nativeAction?.Invoke();
		}

		if (!Controller.IsShowing(this) || _inventoryController.TryAdoptInventoryBackedNpcService(returnDialog))
		{
			return;
		}
		if (!ConversationIsActive())
		{
			CloseConversation(announce: true);
			return;
		}

		_dialog = Main.npcChatText;
		SetSelectionWithoutAnnouncement(0);
	}

	private void ShowHappiness()
	{
		Main.npcChatText = _player.currentShoppingSettings.HappinessReport;
		_dialog = Main.npcChatText;
		SetSelectionWithoutAnnouncement(0);
	}

	private bool ConversationIsActive()
	{
		return _npc.active && _player.active && !_player.dead && _player.TalkNPC?.whoAmI == _npc.whoAmI;
	}

	private void CloseConversation(bool announce)
	{
		_inventoryController.SuppressInventoryToggleUntilRelease();
		_player.SetTalkNPC(-1);
		Main.npcChatText = string.Empty;
		Main.npcChatCornerItem = 0;
		Main.SetNPCShopIndex(0);
		Main.InGuideCraftMenu = false;
		Main.InReforgeMenu = false;
		Controller.Close();
		Main.playerInventory = false;
		if (announce)
		{
			SoundEngine.PlaySound(SoundID.MenuClose);
			AriadneMod.ScreenReader.Output("Conversation closed because the NPC is no longer available.");
		}
	}
}
