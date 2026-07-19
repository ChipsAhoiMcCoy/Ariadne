#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.Audio;
using Terraria.DataStructures;
using Terraria.GameInput;
using Terraria.ID;
using Terraria.Localization;
using Terraria.Map;
using Terraria.ModLoader;
using Terraria.ModLoader.Default;
using Terraria.Social.Steam;
using Terraria.UI;
using Terrarium.Menus;

namespace Terrarium.Ingame;

internal sealed class AccessibleInventoryController
{
	private const int MenuPageSize = 10;
	private static readonly TimeSpan NavigationRepeatDelay = TimeSpan.FromMilliseconds(450);
	private static readonly TimeSpan NavigationRepeatInterval = TimeSpan.FromMilliseconds(85);
	private static readonly FieldInfo? ModAccessoryItemsField = typeof(ModAccessorySlotPlayer).GetField("exAccessorySlot", BindingFlags.Instance | BindingFlags.NonPublic);
	private static readonly FieldInfo? ModAccessoryDyesField = typeof(ModAccessorySlotPlayer).GetField("exDyesAccessory", BindingFlags.Instance | BindingFlags.NonPublic);
	private static readonly Dictionary<int, int> VanillaShopIndices = new()
	{
		[17] = 1,
		[19] = 2,
		[20] = 3,
		[38] = 4,
		[54] = 5,
		[107] = 6,
		[108] = 7,
		[124] = 8,
		[142] = 9,
		[160] = 10,
		[178] = 11,
		[207] = 12,
		[208] = 13,
		[209] = 14,
		[227] = 15,
		[228] = 16,
		[229] = 17,
		[353] = 18,
		[368] = 19,
		[453] = 20,
		[550] = 21,
		[588] = 22,
		[633] = 23,
		[663] = 24,
	};

	private readonly AccessibleMenuController _menuController;
	private readonly List<AccessibleInventoryNode> _rootNodes = [];
	private readonly List<int> _selectionPath = [0];
	private readonly List<AccessibleInventoryItemAction> _itemActions = [];
	private KeyboardState _previousKeyboard;
	private Keys? _repeatingKey;
	private TimeSpan _nextRepeat;
	private bool _active;
	private bool _actionsPaneActive;
	private int _actionSelection;
	private string _lastSemanticState = string.Empty;
	private PendingInventoryItemUse? _pendingItemUse;
	private List<string>? _resumeFocusPath;
	private bool _restoreFocusOnNextActivation;
	private bool _suppressInventoryCloseUntilRelease;

	internal AccessibleInventoryController(AccessibleMenuController menuController)
	{
		_menuController = menuController;
	}

	internal void Update()
	{
		RestoreUsedItemWhenReady();
		if (!CanNavigateInventory())
		{
			Deactivate();
			return;
		}

		KeyboardState keyboard = Keyboard.GetState();
		ConsumeResumeInventoryTrigger(keyboard);
		if (!_active)
		{
			Activate(keyboard);
			return;
		}

		RebuildCategories();
		RefreshActionsPane();
		ConsumeNavigationTriggers();
		if (_rootNodes.Count == 0)
		{
			_previousKeyboard = keyboard;
			return;
		}

		if (_repeatingKey is Keys repeatingKey && keyboard.IsKeyUp(repeatingKey))
		{
			_repeatingKey = null;
		}

		bool handled;
		if (Pressed(keyboard, Keys.Tab))
		{
			ToggleActionsPane();
			handled = true;
		}
		else
		{
			handled = _actionsPaneActive
				? HandleActionsPaneInput(keyboard)
				: HandleInventoryTreeInput(keyboard);
		}

		if (!handled)
		{
			AnnounceExternalStateChange();
		}

		_previousKeyboard = keyboard;
	}

	internal void Deactivate()
	{
		_active = false;
		_actionsPaneActive = false;
		_actionSelection = 0;
		_itemActions.Clear();
		_repeatingKey = null;
		_rootNodes.Clear();
		_selectionPath.Clear();
		_selectionPath.Add(0);
		_lastSemanticState = string.Empty;
		if (Main.gameMenu)
		{
			ClearResumeFocus();
			_suppressInventoryCloseUntilRelease = false;
		}
	}

	private static bool CanNavigateInventory()
	{
		return !Main.gameMenu &&
			Main.playerInventory &&
			!Main.inFancyUI &&
			!Main.ingameOptionsWindow &&
			!Main.drawingPlayerChat &&
			!Main.editChest &&
			!Main.editSign &&
			!PlayerInput.WritingText;
	}

	private void Activate(KeyboardState keyboard)
	{
		_active = true;
		_previousKeyboard = keyboard;
		_repeatingKey = null;
		_actionsPaneActive = false;
		_actionSelection = 0;
		_itemActions.Clear();
		_selectionPath.Clear();
		_selectionPath.Add(0);
		RebuildCategories();
		RestoreResumeFocus();
		ConsumeResumeInventoryTrigger(keyboard);
		_lastSemanticState = GetSemanticState();
		if (CurrentLevel > 0)
		{
			TerrariumMod.ScreenReader.Output(
				$"Inventory tree resumed. {DescribeCurrentLevel()} {DescribeSelection()} " +
				"Use Up and Down Arrow keys to move, Left Arrow to return to the parent, Right Arrow or Enter to open the focused group or screen, Tab for item actions, Enter for the primary action, Shift Enter to take one from a stack, and F1 for help.");
		}
		else
		{
			TerrariumMod.ScreenReader.Output(
				$"Inventory tree, level 0. {DescribeSelection()} " +
				"Use Up and Down Arrow keys to move between categories, Right Arrow or Enter to open a category, Home and End to move to the first and last category, Tab for actions on a focused item, and F1 for help.");
		}
	}

	private void RebuildCategories()
	{
		List<string> oldFocusPath = GetFocusPathIds();
		List<int> oldSelectionPath = [.. _selectionPath];

		_rootNodes.Clear();
		Player player = Main.LocalPlayer;
		AddPlayerInventoryCategories(player);
		AddCraftingCategories();
		AddContextCategories(player);
		AddEquipmentCategories(player);
		AddSettingsEntry();
		AddSaveAndExitEntry();

		if (_rootNodes.Count == 0)
		{
			_selectionPath.Clear();
			_selectionPath.Add(0);
			return;
		}

		RestoreFocusPath(oldFocusPath, oldSelectionPath);
	}

	private void AddPlayerInventoryCategories(Player player)
	{
		List<AccessibleInventoryNode> sections = [];
		AddItemCategory(sections, "hotbar", "Hotbar", player.inventory, ItemSlot.Context.InventoryItem, 0, 10, index => $"slot {index + 1}", canFavorite: true);
		List<AccessibleInventoryEntry> mainInventoryEntries = CreateItemEntries(
			"main-inventory",
			player.inventory,
			ItemSlot.Context.InventoryItem,
			10,
			40,
			index => $"slot {index - 9}",
			canFavorite: true);
		mainInventoryEntries.Add(new AccessibleInventoryEntry(
			"trash-slot",
			() => DescribeItem("trash slot 41", player.trashItem),
			() => DescribeItemDetails(player.trashItem),
			() => LeftClickTrash(player),
			() => RightClickTrash(player),
			selectionDetails: () => DescribeItemDetails(player.trashItem, includeSummary: false)));
		AddCategory(sections, "main-inventory", "Main Inventory", mainInventoryEntries);

		List<AccessibleInventoryEntry> currencyEntries = [];
		for (int index = 50; index < 54; index++)
		{
			int captured = index;
			currencyEntries.Add(ItemEntry($"coin-{captured}", () => $"Coin slot {captured - 49}", player.inventory, ItemSlot.Context.InventoryCoin, captured, canFavorite: true));
		}
		for (int index = 54; index < 58; index++)
		{
			int captured = index;
			currencyEntries.Add(ItemEntry($"ammo-{captured}", () => $"Ammo slot {captured - 53}", player.inventory, ItemSlot.Context.InventoryAmmo, captured, canFavorite: true));
		}
		AddCategory(sections, "coins-ammo", "Coins and Ammo", currencyEntries);

		if (player.chest == -1 && Main.npcShop == 0)
		{
			sections.Add(AccessibleInventoryNode.FromEntry(new AccessibleInventoryEntry(
				"quick-stack-nearby",
				() => Language.GetTextValue("GameUI.QuickStackToNearby"),
				() => "Quick stack matching inventory items into nearby chests.",
				() => QuickStackNearby(player))));
		}
		sections.Add(AccessibleInventoryNode.FromEntry(new AccessibleInventoryEntry(
			"sort-inventory",
			() => Language.GetTextValue("GameUI.SortInventory"),
			() => "Sort the main inventory.",
			ItemSorting.SortInventory)));
		sections.Add(AccessibleInventoryNode.FromEntry(new AccessibleInventoryEntry(
			"sort-ammo",
			() => "Sort ammo",
			() => "Consolidate and sort the ammo slots.",
			ItemSorting.SortAmmo)));

		AddBranch(_rootNodes, "inventory", "Inventory", sections);
	}

	private void AddContextCategories(Player player)
	{
		List<AccessibleInventoryNode> interactions = [];
		AddNpcConversationCategory(player, interactions);

		if (player.chest != -1)
		{
			ChestUI.GetContainerUsageInfo(out _, out Item[] container);
			int context = player.chest >= 0 ? ItemSlot.Context.ChestItem : ItemSlot.Context.BankItem;
			string containerName = GetContainerName(player);
			List<AccessibleInventoryNode> containerSections = [];
			AddItemCategory(containerSections, "container-items", "Items", container, context, 0, container.Length, index => $"row {index / 10 + 1}, column {index % 10 + 1}");
			AddContainerActions(player, containerName, containerSections);
			AddBranch(interactions, "container", containerName, containerSections);
		}

		if (Main.npcShop > 0)
		{
			Item[] shopItems = Main.instance.shop[Main.npcShop].item;
			List<AccessibleInventoryEntry> entries = [];
			for (int index = 0; index < shopItems.Length; index++)
			{
				int captured = index;
				entries.Add(ItemEntry(
					$"shop-{captured}",
					() => $"row {captured / 10 + 1}, column {captured % 10 + 1}",
					shopItems,
					ItemSlot.Context.ShopItem,
					captured,
					extraDetails: () => DescribeShopPrice(shopItems[captured])));
			}
			AddCategory(interactions, "shop", "Shop", entries);
		}

		if (Main.InGuideCraftMenu)
		{
			AddCategory(interactions, "guide", "Guide crafting",
			[
				new AccessibleInventoryEntry(
					"guide-slot",
					() => DescribeItem("material slot", Main.guideItem),
					() => DescribeItemDetails(Main.guideItem),
					LeftClickGuide,
					RightClickGuide,
					selectionDetails: () => DescribeItemDetails(Main.guideItem, includeSummary: false))
			]);
		}

		if (Main.InReforgeMenu)
		{
			AddCategory(interactions, "reforge", "Reforge",
			[
				new AccessibleInventoryEntry(
					"reforge-slot",
					() => DescribeItem("item slot", Main.reforgeItem),
					() => DescribeItemDetails(Main.reforgeItem),
					LeftClickReforge,
					RightClickReforge,
					selectionDetails: () => DescribeItemDetails(Main.reforgeItem, includeSummary: false)),
				new AccessibleInventoryEntry(
					"reforge-action",
					DescribeReforgeAction,
					DescribeReforgeAction,
					ReforgeItem,
					enabled: () => !Main.reforgeItem.IsAir && ItemLoader.CanReforge(Main.reforgeItem))
			]);
		}

		AddBranch(_rootNodes, "interactions", "Interactions", interactions);
	}

	private void AddNpcConversationCategory(Player player, List<AccessibleInventoryNode> destination)
	{
		NPC? npc = player.TalkNPC;
		if (npc is null || !npc.active)
		{
			return;
		}

		List<AccessibleInventoryEntry> entries =
		[
			new AccessibleInventoryEntry(
				"npc-dialog",
				() => $"{npc.FullName}: {(string.IsNullOrWhiteSpace(Main.npcChatText) ? "No current dialog" : Main.npcChatText)}",
				() => string.IsNullOrWhiteSpace(Main.npcChatText) ? $"Talking to {npc.FullName}." : Main.npcChatText)
		];

		if (npc.ModNPC is not null)
		{
			string firstButton = string.Empty;
			string secondButton = string.Empty;
			NPCLoader.SetChatButtons(ref firstButton, ref secondButton);
			AddModNpcChatButton(entries, firstButton, firstButton: true);
			AddModNpcChatButton(entries, secondButton, firstButton: false);
		}
		else
		{
			if (VanillaShopIndices.TryGetValue(npc.type, out int shopIndex))
			{
				entries.Add(new AccessibleInventoryEntry(
					"npc-shop",
					() => $"Open {npc.FullName}'s shop",
					() => "Open this character's shop inventory.",
					() => ActivateVanillaChatButton(firstButton: true, () => OpenVanillaShop(shopIndex))));
			}

			if (npc.type == NPCID.Guide)
			{
				entries.Add(new AccessibleInventoryEntry(
					"npc-guide-crafting",
					() => "Crafting help",
					() => "Open the Guide material slot and recipe list.",
					() => ActivateVanillaChatButton(firstButton: false, OpenGuideCrafting)));
			}
			else if (npc.type == NPCID.GoblinTinkerer)
			{
				entries.Add(new AccessibleInventoryEntry(
					"npc-reforge",
					() => "Reforge",
					() => "Open the Goblin Tinkerer's reforge item slot.",
					() => ActivateVanillaChatButton(firstButton: false, OpenReforge)));
			}
			else if (npc.type == NPCID.Painter)
			{
				entries.Add(new AccessibleInventoryEntry(
					"npc-decor-shop",
					() => "Open décor shop",
					() => "Open the Painter's alternate décor shop.",
					() => ActivateVanillaChatButton(firstButton: false, () => OpenVanillaShop(25))));
			}
		}

		if (!string.IsNullOrWhiteSpace(player.currentShoppingSettings.HappinessReport))
		{
			entries.Add(new AccessibleInventoryEntry(
				"npc-happiness",
				() => "Ask about happiness",
				() => "Hear how this character's surroundings affect shop prices.",
				() =>
				{
					Main.npcChatText = player.currentShoppingSettings.HappinessReport;
					SoundEngine.PlaySound(SoundID.MenuTick);
				}));
		}

		entries.Add(new AccessibleInventoryEntry(
			"npc-close",
			() => "Close conversation",
			() => $"Stop talking to {npc.FullName}.",
			CloseNpcConversation));

		AddCategory(destination, "npc-conversation", "NPC Conversation", entries);
	}

	private void AddEquipmentCategories(Player player)
	{
		Func<int, bool> slotEnabled = index => player.IsItemSlotUnlockedAndUsable(index) || Main.mouseItem.IsAir;
		List<AccessibleInventoryNode> armorSections = [];
		AddItemCategory(armorSections, "equipped-armor", "Equipped Armor", player.armor, ItemSlot.Context.EquipArmor, 0, 3, ArmorSlotName, enabled: slotEnabled);
		AddItemCategory(armorSections, "vanity-armor", "Vanity Armor", player.armor, ItemSlot.Context.EquipArmorVanity, 10, 3, VanityArmorSlotName, enabled: slotEnabled);
		AddItemCategory(armorSections, "armor-dyes", "Armor Dyes", player.dye, ItemSlot.Context.EquipDye, 0, 3, DyeSlotName, enabled: slotEnabled);
		AddBranch(_rootNodes, "armor", "Armor", armorSections);

		List<AccessibleInventoryNode> accessorySections = [];
		AddItemCategory(accessorySections, "equipped-accessories", "Equipped Accessories", player.armor, ItemSlot.Context.EquipAccessory, 3, 7, index => $"Accessory slot {index - 2}", enabled: slotEnabled);
		AddItemCategory(accessorySections, "vanity-accessories", "Vanity Accessories", player.armor, ItemSlot.Context.EquipAccessoryVanity, 13, 7, index => $"Vanity accessory slot {index - 12}", enabled: slotEnabled);
		AddItemCategory(accessorySections, "accessory-dyes", "Accessory Dyes", player.dye, ItemSlot.Context.EquipDye, 3, Math.Max(0, player.dye.Length - 3), DyeSlotName, enabled: slotEnabled);
		AddModAccessoryCategories(player, accessorySections);
		AddBranch(_rootNodes, "accessories", "Accessories", accessorySections);

		string[] miscNames = ["Pet", "Light pet", "Minecart", "Mount", "Grappling hook"];
		int[] miscContexts = [ItemSlot.Context.EquipPet, ItemSlot.Context.EquipLight, ItemSlot.Context.EquipMinecart, ItemSlot.Context.EquipMount, ItemSlot.Context.EquipGrapple];
		List<AccessibleInventoryNode> equipmentSections = [];
		List<AccessibleInventoryEntry> miscEntries = [];
		for (int index = 0; index < player.miscEquips.Length; index++)
		{
			int captured = index;
			miscEntries.Add(ItemEntry($"misc-{captured}", () => $"{miscNames[captured]} slot", player.miscEquips, miscContexts[captured], captured));
		}
		AddCategory(equipmentSections, "misc-equipment", "Pets, Mounts, and Hooks", miscEntries);
		AddItemCategory(equipmentSections, "misc-dyes", "Equipment Dyes", player.miscDyes, ItemSlot.Context.EquipMiscDye, 0, player.miscDyes.Length, index => $"Dye for {miscNames[index]}");
		AddLoadoutAndVisibilityCategories(player, equipmentSections);
		AddBranch(_rootNodes, "equipment", "Equipment", equipmentSections);
	}

	private void AddModAccessoryCategories(Player player, List<AccessibleInventoryNode> destination)
	{
		ModAccessorySlotPlayer modPlayer = player.GetModPlayer<ModAccessorySlotPlayer>();
		if (modPlayer.SlotCount <= 0 ||
			ModAccessoryItemsField?.GetValue(modPlayer) is not Item[] accessories ||
			ModAccessoryDyesField?.GetValue(modPlayer) is not Item[] dyes)
		{
			return;
		}

		AccessorySlotLoader loader = LoaderManager.Get<AccessorySlotLoader>();
		List<AccessibleInventoryEntry> functional = [];
		List<AccessibleInventoryEntry> vanity = [];
		List<AccessibleInventoryEntry> dyeEntries = [];
		for (int index = 0; index < modPlayer.SlotCount; index++)
		{
			int captured = index;
			ModAccessorySlot slot = loader.Get(captured, player);
			string name = SplitWords(slot.Name);
			if (slot.DrawFunctionalSlot)
			{
				functional.Add(ItemEntry($"mod-functional-{captured}", () => $"{name} functional slot", accessories, ItemSlot.Context.ModdedAccessorySlot, captured));
			}
			if (slot.DrawVanitySlot)
			{
				int vanityIndex = captured + modPlayer.SlotCount;
				vanity.Add(ItemEntry($"mod-vanity-{captured}", () => $"{name} vanity slot", accessories, ItemSlot.Context.ModdedVanityAccessorySlot, vanityIndex));
			}
			if (slot.DrawDyeSlot)
			{
				dyeEntries.Add(ItemEntry($"mod-dye-{captured}", () => $"{name} dye slot", dyes, ItemSlot.Context.ModdedDyeSlot, captured));
			}
		}

		List<AccessibleInventoryNode> moddedSections = [];
		AddCategory(moddedSections, "mod-functional-accessories", "Functional", functional);
		AddCategory(moddedSections, "mod-vanity-accessories", "Vanity", vanity);
		AddCategory(moddedSections, "mod-accessory-dyes", "Dyes", dyeEntries);
		AddBranch(destination, "modded-accessories", "Modded Accessories", moddedSections);
	}

	private void AddLoadoutAndVisibilityCategories(Player player, List<AccessibleInventoryNode> destination)
	{
		List<AccessibleInventoryEntry> loadoutEntries = [];
		for (int index = 0; index < player.Loadouts.Length; index++)
		{
			int captured = index;
			loadoutEntries.Add(new AccessibleInventoryEntry(
				$"loadout-{captured}",
				() => $"Loadout {captured + 1}, {(player.CurrentLoadoutIndex == captured ? "selected" : "not selected")}",
				() => $"Switch to equipment loadout {captured + 1}.",
				() => player.TrySwitchingLoadout(captured)));
		}
		AddCategory(destination, "loadouts", "Loadouts", loadoutEntries);

		List<AccessibleInventoryEntry> visibilityEntries = [];
		for (int index = 3; index < player.hideVisibleAccessory.Length; index++)
		{
			int captured = index;
			visibilityEntries.Add(new AccessibleInventoryEntry(
				$"visibility-accessory-{captured}",
				() => $"Accessory slot {captured - 2} visuals, {(player.hideVisibleAccessory[captured] ? "hidden" : "visible")}",
				() => "Toggle whether this functional accessory is shown on the character.",
				() => ToggleAccessoryVisibility(player, captured)));
		}

		visibilityEntries.Add(new AccessibleInventoryEntry(
			"visibility-pet",
			() => $"Pet visuals, {(player.hideMisc[0] ? "hidden" : "visible")}",
			() => "Toggle pet visibility.",
			player.TogglePet));
		visibilityEntries.Add(new AccessibleInventoryEntry(
			"visibility-light-pet",
			() => $"Light pet visuals, {(player.hideMisc[1] ? "hidden" : "visible")}",
			() => "Toggle light pet visibility.",
			player.ToggleLight));
		if (player.unlockedSuperCart)
		{
			visibilityEntries.Add(new AccessibleInventoryEntry(
				"super-cart",
				() => $"Super cart, {(player.enabledSuperCart ? "enabled" : "disabled")}",
				() => "Toggle the upgraded minecart behavior.",
				() => player.enabledSuperCart = !player.enabledSuperCart));
		}

		ModAccessorySlotPlayer modPlayer = player.GetModPlayer<ModAccessorySlotPlayer>();
		AccessorySlotLoader loader = LoaderManager.Get<AccessorySlotLoader>();
		for (int index = 0; index < modPlayer.SlotCount; index++)
		{
			int captured = index;
			ModAccessorySlot slot = loader.Get(captured, player);
			if (!slot.DrawFunctionalSlot)
			{
				continue;
			}
			visibilityEntries.Add(new AccessibleInventoryEntry(
				$"visibility-mod-{captured}",
				() => $"{SplitWords(slot.Name)} visuals, {(slot.HideVisuals ? "hidden" : "visible")}",
				() => "Toggle whether this modded accessory is shown on the character.",
				() => slot.HideVisuals = !slot.HideVisuals));
		}

		AddCategory(destination, "equipment-visibility", "Visibility", visibilityEntries);
	}

	private void AddCraftingCategories()
	{
		if (Main.numAvailableRecipes <= 0)
		{
			return;
		}

		List<AccessibleInventoryEntry> entries = [];
		for (int index = 0; index < Main.numAvailableRecipes; index++)
		{
			int captured = index;
			entries.Add(new AccessibleInventoryEntry(
				$"recipe-{Main.availableRecipe[captured]}",
				() => DescribeRecipe(captured),
				() => DescribeRecipeDetails(captured),
				() => SelectOrCraftRecipe(captured)));
		}
		AddCategory(_rootNodes, "crafting", Main.InGuideCraftMenu ? "Guide Recipes" : "Crafting", entries);
	}

	private void AddSettingsEntry()
	{
		_rootNodes.Add(AccessibleInventoryNode.FromEntry(new AccessibleInventoryEntry(
			"settings",
			() => Lang.menu[14].Value,
			() => "Open the accessible in-game settings menu.",
			OpenSettings,
			opensSubmenu: true)));
	}

	private void AddSaveAndExitEntry()
	{
		_rootNodes.Add(AccessibleInventoryNode.FromEntry(new AccessibleInventoryEntry(
			"save-and-exit",
			() => Lang.inter[35].Value,
			() => "Save the current world and return to the main menu.",
			SaveAndExit)));
	}

	private void AddContainerActions(Player player, string containerName, List<AccessibleInventoryNode> destination)
	{
		ContainerTransferContext context = ContainerTransferContext.FromUnknown(player);
		List<AccessibleInventoryEntry> actions =
		[
			new("loot-all", () => "Loot all", () => $"Move all possible items from {containerName} to the inventory.", ChestUI.LootAll),
			new("deposit-all", () => "Deposit all", () => $"Move all possible inventory items into {containerName}.", () => ChestUI.DepositAll(context)),
			new("quick-stack", () => "Quick stack", () => $"Stack matching inventory items into {containerName}.", () => ChestUI.QuickStack(context)),
			new("restock", () => "Restock", () => $"Refill partial inventory stacks from {containerName}.", ChestUI.Restock),
			new("sort-container", () => $"Sort {containerName}", () => "Sort this container.", ItemSorting.SortChest),
		];
		if (player.chest >= 0)
		{
			actions.Add(new("rename-container", () => $"Rename {containerName}", () => "Edit this chest's name.", () => OpenChestRename(player)));
		}
		else if (player.chest == -5)
		{
			actions.Add(new AccessibleInventoryEntry(
				"void-vacuum",
				() => Language.GetTextValue(player.IsVoidVaultEnabled ? "UI.ToggleBank4VacuumIsOn" : "UI.ToggleBank4VacuumIsOff"),
				() => "Toggle whether overflow item pickups are sent to the Void Vault.",
				() => player.IsVoidVaultEnabled = !player.IsVoidVaultEnabled));
		}
		AddCategory(destination, "container-actions", "Actions", actions);
	}

	private void AddItemCategory(
		List<AccessibleInventoryNode> destination,
		string id,
		string name,
		Item[] items,
		int context,
		int start,
		int count,
		Func<int, string> slotName,
		bool canFavorite = false,
		Func<int, bool>? enabled = null)
	{
		AddCategory(destination, id, name, CreateItemEntries(id, items, context, start, count, slotName, canFavorite, enabled));
	}

	private static List<AccessibleInventoryEntry> CreateItemEntries(
		string id,
		Item[] items,
		int context,
		int start,
		int count,
		Func<int, string> slotName,
		bool canFavorite = false,
		Func<int, bool>? enabled = null)
	{
		List<AccessibleInventoryEntry> entries = [];
		int end = Math.Min(items.Length, start + count);
		for (int index = start; index < end; index++)
		{
			int captured = index;
			entries.Add(ItemEntry(
				$"{id}-{captured}",
				() => slotName(captured),
				items,
				context,
				captured,
				canFavorite,
				enabled is null ? null : () => enabled(captured)));
		}
		return entries;
	}

	private static AccessibleInventoryEntry ItemEntry(
		string id,
		Func<string> slotName,
		Item[] items,
		int context,
		int index,
		bool canFavorite = false,
		Func<bool>? enabled = null,
		Func<string>? extraDetails = null)
	{
		return new AccessibleInventoryEntry(
			id,
			() => DescribeItem(slotName(), items[index]),
			() => CombineDetails(DescribeItemDetails(items[index]), extraDetails?.Invoke()),
			() => LeftClick(items, context, index),
			() => RightClick(items, context, index),
			canFavorite ? () => ToggleFavorite(items[index]) : null,
			enabled,
			() => CombineDetails(DescribeItemDetails(items[index], includeSummary: false), extraDetails?.Invoke()),
			itemSlot: new AccessibleInventoryItemSlot(items, context, index, canFavorite));
	}

	private static void AddCategory(List<AccessibleInventoryNode> destination, string id, string name, List<AccessibleInventoryEntry> entries)
	{
		AddBranch(destination, id, name, entries.Select(AccessibleInventoryNode.FromEntry).ToList());
	}

	private static void AddBranch(List<AccessibleInventoryNode> destination, string id, string name, List<AccessibleInventoryNode> children)
	{
		if (children.Count > 0)
		{
			destination.Add(new AccessibleInventoryNode(id, () => name, children));
		}
	}

	private IReadOnlyList<AccessibleInventoryNode> CurrentLevelNodes
	{
		get
		{
			IReadOnlyList<AccessibleInventoryNode> nodes = _rootNodes;
			for (int depth = 0; depth < CurrentLevel; depth++)
			{
				nodes = nodes[_selectionPath[depth]].Children;
			}
			return nodes;
		}
	}

	private AccessibleInventoryNode CurrentNode => CurrentLevelNodes[CurrentLevelIndex];

	private AccessibleInventoryEntry CurrentEntry => CurrentNode.Entry ?? throw new InvalidOperationException("The current inventory node is not an action.");

	private int CurrentLevel => _selectionPath.Count - 1;

	private int CurrentLevelIndex => _selectionPath[^1];

	private int CurrentLevelCount => CurrentLevelNodes.Count;

	private bool HandleInventoryTreeInput(KeyboardState keyboard)
	{
		if (Pressed(keyboard, Keys.Left) && CurrentLevel > 0)
		{
			CloseSubmenu();
		}
		else if (Pressed(keyboard, Keys.Right) && CurrentNode.OpensSubmenu)
		{
			OpenCurrentNode();
		}
		else if (NavigationTriggered(keyboard, Keys.Up))
		{
			MoveVertical(-1);
		}
		else if (NavigationTriggered(keyboard, Keys.Down))
		{
			MoveVertical(1);
		}
		else if (Pressed(keyboard, Keys.Home))
		{
			SetCurrentLevelSelection(0);
		}
		else if (Pressed(keyboard, Keys.End))
		{
			SetCurrentLevelSelection(CurrentLevelCount - 1);
		}
		else if (Pressed(keyboard, Keys.PageUp))
		{
			SetCurrentLevelSelection(Math.Max(0, CurrentLevelIndex - MenuPageSize));
		}
		else if (Pressed(keyboard, Keys.PageDown))
		{
			SetCurrentLevelSelection(Math.Min(CurrentLevelCount - 1, CurrentLevelIndex + MenuPageSize));
		}
		else if (Pressed(keyboard, Keys.Enter))
		{
			if (CurrentNode.HasChildren)
			{
				OpenSubmenu();
			}
			else
			{
				ActivateEntry(secondary: IsShiftDown(keyboard));
			}
			Main.chatRelease = false;
		}
		else if (CurrentNode.IsAction && Pressed(keyboard, Keys.F))
		{
			ToggleFavorite();
		}
		else if (CurrentNode.IsAction && Pressed(keyboard, Keys.R))
		{
			ReadDetails();
		}
		else if (Pressed(keyboard, Keys.F1))
		{
			ReadHelp();
		}
		else
		{
			return false;
		}

		return true;
	}

	private bool HandleActionsPaneInput(KeyboardState keyboard)
	{
		if (NavigationTriggered(keyboard, Keys.Up))
		{
			MoveActionSelection(-1);
		}
		else if (NavigationTriggered(keyboard, Keys.Down))
		{
			MoveActionSelection(1);
		}
		else if (Pressed(keyboard, Keys.Home))
		{
			SetActionSelection(0);
		}
		else if (Pressed(keyboard, Keys.End))
		{
			SetActionSelection(_itemActions.Count - 1);
		}
		else if (Pressed(keyboard, Keys.PageUp))
		{
			SetActionSelection(Math.Max(0, _actionSelection - MenuPageSize));
		}
		else if (Pressed(keyboard, Keys.PageDown))
		{
			SetActionSelection(Math.Min(_itemActions.Count - 1, _actionSelection + MenuPageSize));
		}
		else if (Pressed(keyboard, Keys.Enter))
		{
			ActivateItemAction();
			Main.chatRelease = false;
		}
		else if (Pressed(keyboard, Keys.R))
		{
			ReadActionDetails();
		}
		else if (Pressed(keyboard, Keys.F1))
		{
			ReadHelp();
		}
		else
		{
			return false;
		}

		return true;
	}

	private void OpenCurrentNode()
	{
		if (CurrentNode.HasChildren)
		{
			OpenSubmenu();
			return;
		}

		ActivateEntry(secondary: false);
	}

	private void OpenSubmenu()
	{
		if (!CurrentNode.HasChildren)
		{
			return;
		}

		_selectionPath.Add(0);
		SoundEngine.PlaySound(SoundID.MenuOpen);
		_lastSemanticState = GetSemanticState();
		TerrariumMod.ScreenReader.Output($"{DescribeCurrentLevel()} {DescribeSelection()}");
	}

	private void CloseSubmenu()
	{
		if (CurrentLevel == 0)
		{
			return;
		}

		_selectionPath.RemoveAt(_selectionPath.Count - 1);
		SoundEngine.PlaySound(SoundID.MenuClose);
		_lastSemanticState = GetSemanticState();
		TerrariumMod.ScreenReader.Output($"{DescribeCurrentLevel()} {DescribeSelection()}");
	}

	private void MoveVertical(int direction)
	{
		int count = CurrentLevelCount;
		if (count <= 1)
		{
			return;
		}
		SetCurrentLevelSelection((CurrentLevelIndex + direction + count) % count);
	}

	private void SetCurrentLevelSelection(int index)
	{
		index = Math.Clamp(index, 0, CurrentLevelCount - 1);
		if (index == CurrentLevelIndex)
		{
			return;
		}
		_selectionPath[^1] = index;
		SoundEngine.PlaySound(SoundID.MenuTick);
		AnnounceSelection();
	}

	private void ToggleActionsPane()
	{
		if (_actionsPaneActive)
		{
			CloseActionsPane(announce: true);
			return;
		}

		AccessibleInventoryItemSlot? slot = CurrentNode.Entry?.ItemSlot;
		if (slot is null || slot.Item.IsAir)
		{
			TerrariumMod.ScreenReader.Output("The focused entry has no item actions.");
			return;
		}

		_itemActions.Clear();
		_itemActions.AddRange(BuildItemActions(slot));
		if (_itemActions.Count == 0)
		{
			TerrariumMod.ScreenReader.Output($"{slot.Item.AffixName()} has no available item actions.");
			return;
		}

		_actionsPaneActive = true;
		_actionSelection = 0;
		SoundEngine.PlaySound(SoundID.MenuOpen);
		_lastSemanticState = GetSemanticState();
		TerrariumMod.ScreenReader.Output($"Actions pane for {DescribeItemBrief(slot.Item)}. {DescribeActionSelection()} Tab returns to the inventory pane.");
	}

	private void CloseActionsPane(bool announce)
	{
		_actionsPaneActive = false;
		_actionSelection = 0;
		_itemActions.Clear();
		SoundEngine.PlaySound(SoundID.MenuClose);
		_lastSemanticState = GetSemanticState();
		if (announce)
		{
			TerrariumMod.ScreenReader.Output($"Inventory pane. {DescribeSelection()}");
		}
	}

	private void RefreshActionsPane()
	{
		if (!_actionsPaneActive)
		{
			return;
		}

		string? previousActionId = _itemActions.Count > 0
			? _itemActions[Math.Clamp(_actionSelection, 0, _itemActions.Count - 1)].Id
			: null;
		AccessibleInventoryItemSlot? slot = CurrentNode.Entry?.ItemSlot;
		List<AccessibleInventoryItemAction> refreshed = slot is null || slot.Item.IsAir
			? []
			: BuildItemActions(slot);
		_itemActions.Clear();
		_itemActions.AddRange(refreshed);
		if (_itemActions.Count == 0)
		{
			_actionsPaneActive = false;
			_actionSelection = 0;
			return;
		}

		int preservedIndex = previousActionId is null
			? -1
			: _itemActions.FindIndex(action => action.Id == previousActionId);
		_actionSelection = preservedIndex >= 0
			? preservedIndex
			: Math.Clamp(_actionSelection, 0, _itemActions.Count - 1);
	}

	private List<AccessibleInventoryItemAction> BuildItemActions(AccessibleInventoryItemSlot slot)
	{
		List<AccessibleInventoryItemAction> actions = [];
		Item item = slot.Item;
		if (item.IsAir)
		{
			return actions;
		}

		if (slot.Context == ItemSlot.Context.InventoryItem && ItemLoader.CanRightClick(item))
		{
			actions.Add(new(
				"open",
				"Open",
				"Open this item or perform its mod-defined right-click action.",
				() =>
				{
					RightClick(slot.Items, slot.Context, slot.Index);
					return null;
				},
				returnsToInventory: true));
		}

		if (CanUse(slot))
		{
			string verb = item.consumable ? "Consume" : "Use";
			actions.Add(new(
				verb.ToLowerInvariant(),
				verb,
				$"{verb} one {item.AffixName()} using its normal item behavior.",
				() => BeginUsingItem(slot),
				returnsToInventory: true));
		}

		if (CanEquip(slot))
		{
			actions.Add(new(
				"equip",
				"Equip",
				$"Equip {item.AffixName()} in its appropriate equipment slot. If that slot is occupied, its previous item replaces this item in the source slot.",
				() => EquipItem(slot),
				returnsToInventory: true));
		}
		else if (IsEquipmentContext(slot.Context))
		{
			actions.Add(new(
				"unequip",
				"Unequip",
				$"Remove {item.AffixName()} from this equipment slot and send it to the player inventory.",
				() => UnequipItem(slot),
				returnsToInventory: true));
		}

		if (slot.CanFavorite)
		{
			string verb = item.favorited ? "Unfavorite" : "Favorite";
			actions.Add(new(
				"favorite",
				verb,
				$"{verb} {item.AffixName()}.",
				() =>
				{
					ToggleFavorite(slot.Item);
					return null;
				}));
		}

		if (CanTakeOne(slot))
		{
			actions.Add(new(
				"take-one",
				"Take one",
				$"Take one {item.AffixName()} from this stack and hold it.",
				() =>
				{
					TakeOneFromStack(slot);
					return null;
				}));
		}

		if (IsPlayerInventorySlot(slot) && !item.favorited)
		{
			actions.Add(new(
				"drop",
				"Drop",
				$"Drop the full stack of {item.AffixName()} into the world.",
				() =>
				{
					DropItem(slot);
					return null;
				},
				returnsToInventory: true));

			string verb = Main.npcShop > 0 ? "Sell" : "Trash";
			actions.Add(new(
				"trash-or-sell",
				verb,
				$"{verb} the full stack of {item.AffixName()}.",
				() =>
				{
					ItemSlot.SellOrTrash(slot.Items, slot.Context, slot.Index);
					return null;
				},
				returnsToInventory: true));
		}

		return actions;
	}

	private void MoveActionSelection(int direction)
	{
		if (_itemActions.Count <= 1)
		{
			return;
		}
		SetActionSelection((_actionSelection + direction + _itemActions.Count) % _itemActions.Count);
	}

	private void SetActionSelection(int index)
	{
		index = Math.Clamp(index, 0, _itemActions.Count - 1);
		if (index == _actionSelection)
		{
			return;
		}
		_actionSelection = index;
		SoundEngine.PlaySound(SoundID.MenuTick);
		AnnounceActionSelection();
	}

	private void ActivateItemAction()
	{
		AccessibleInventoryItemAction action = _itemActions[_actionSelection];
		string? announcement = action.Activate();
		if (!CanNavigateInventory())
		{
			return;
		}

		RebuildCategories();
		if (action.ReturnsToInventory)
		{
			CloseActionsPane(announce: false);
			TerrariumMod.ScreenReader.Output(announcement is null
				? $"Inventory pane. {DescribeSelection()}"
				: $"{announcement} Inventory pane.");
			return;
		}

		RefreshActionsPane();
		SoundEngine.PlaySound(SoundID.MenuTick);
		if (_actionsPaneActive)
		{
			if (string.IsNullOrWhiteSpace(announcement))
			{
				AnnounceActionSelection();
			}
			else
			{
				_lastSemanticState = GetSemanticState();
				TerrariumMod.ScreenReader.Output($"{announcement} {DescribeActionSelection()}");
			}
		}
		else
		{
			_lastSemanticState = GetSemanticState();
			TerrariumMod.ScreenReader.Output($"Inventory pane. {DescribeSelection()}");
		}
	}

	private void ReadActionDetails()
	{
		TerrariumMod.ScreenReader.Output(_itemActions[_actionSelection].Details);
	}

	private void AnnounceActionSelection()
	{
		_lastSemanticState = GetSemanticState();
		TerrariumMod.ScreenReader.Output(DescribeActionSelection());
	}

	private string DescribeActionSelection()
	{
		AccessibleInventoryItemAction action = _itemActions[_actionSelection];
		string held = Main.mouseItem.IsAir ? string.Empty : $" Holding {DescribeItemBrief(Main.mouseItem)}.";
		return $"{action.Label}, {_actionSelection + 1} of {_itemActions.Count}.{held}";
	}

	private void ActivateEntry(bool secondary)
	{
		AccessibleInventoryEntry entry = CurrentEntry;
		if (!entry.IsEnabled)
		{
			SoundEngine.PlaySound(SoundID.MenuClose);
			TerrariumMod.ScreenReader.Output($"{entry.Label()}, unavailable.");
			return;
		}
		if (secondary &&
			entry.ItemSlot is AccessibleInventoryItemSlot slot &&
			slot.Item.stack > 1 &&
			SupportsTakeOne(slot.Context))
		{
			if (!CanTakeOne(slot))
			{
				TerrariumMod.ScreenReader.Output($"Cannot take one {slot.Item.AffixName()} while holding {DescribeItemBrief(Main.mouseItem)}.");
				return;
			}

			TakeOneFromStack(slot);
			RebuildCategories();
			SoundEngine.PlaySound(SoundID.MenuTick);
			AnnounceSelection();
			return;
		}

		Action? action = secondary ? entry.Secondary : entry.Primary;
		action ??= secondary ? entry.Primary : entry.Secondary;
		if (action is null)
		{
			TerrariumMod.ScreenReader.Output($"{entry.Label()}, no {(secondary ? "secondary" : "primary")} action.");
			return;
		}

		int previousLevel = CurrentLevel;
		action();
		if (CanNavigateInventory())
		{
			RebuildCategories();
			SoundEngine.PlaySound(SoundID.MenuTick);
			AnnounceSelection(includeLevel: CurrentLevel != previousLevel);
		}
	}

	private void ToggleFavorite()
	{
		if (CurrentEntry.Favorite is null)
		{
			TerrariumMod.ScreenReader.Output("This entry cannot be favorited.");
			return;
		}
		CurrentEntry.Favorite();
		SoundEngine.PlaySound(SoundID.MenuTick);
		AnnounceSelection();
	}

	private void ReadDetails()
	{
		string details = CurrentEntry.Details();
		TerrariumMod.ScreenReader.Output(string.IsNullOrWhiteSpace(details) ? "No additional details." : details);
	}

	private void ReadHelp()
	{
		if (_actionsPaneActive)
		{
			TerrariumMod.ScreenReader.Output(
				$"Item actions help. {DescribeActionSelection()} " +
				"Up and Down move through the available actions, Home and End move to the first and last action, Enter performs the focused action, R reads its description, and Tab returns to the inventory pane.");
			return;
		}

		TerrariumMod.ScreenReader.Output(
			$"Inventory tree help. {DescribeCurrentLevel()} {DescribeSelection()} " +
			"At every level, Up and Down move through the current list and wrap. Right Arrow or Enter opens the focused group or screen, and Left Arrow returns to its parent. Home and End move to the first and last option, and Page Up and Page Down move by ten options. On an item slot, Tab opens its available actions and Shift Enter takes one item from a stack. Enter performs the primary or normal left click action. F toggles favorite for inventory items. R reads the full item tooltip or action details. Escape uses Terraria's normal inventory close control.");
	}

	private void AnnounceSelection(bool includeLevel = false)
	{
		_lastSemanticState = GetSemanticState();
		string level = includeLevel ? $"{DescribeCurrentLevel()} " : string.Empty;
		TerrariumMod.ScreenReader.Output($"{level}{DescribeSelection()}");
	}

	private void AnnounceExternalStateChange()
	{
		string state = GetSemanticState();
		if (state == _lastSemanticState)
		{
			return;
		}
		if (_actionsPaneActive)
		{
			_lastSemanticState = state;
			TerrariumMod.ScreenReader.Output(DescribeActionSelection());
			return;
		}

		bool levelChanged = !_lastSemanticState.StartsWith($"tree|{CurrentLevel}|", StringComparison.Ordinal);
		_lastSemanticState = state;
		string level = levelChanged ? $"{DescribeCurrentLevel()} " : string.Empty;
		TerrariumMod.ScreenReader.Output($"{level}{DescribeSelection()}");
	}

	private string DescribeSelection()
	{
		AccessibleInventoryNode node = CurrentNode;
		AccessibleInventoryEntry? entry = node.Entry;
		string unavailable = entry?.IsEnabled == false ? ", unavailable" : string.Empty;
		string position = entry?.SelectionDetails is null ? $", {CurrentLevelIndex + 1} of {CurrentLevelCount}" : string.Empty;
		string details = entry?.SelectionDetails?.Invoke() ?? string.Empty;
		if (!string.IsNullOrWhiteSpace(details))
		{
			details = $" {details}";
		}
		string held = entry is null || Main.mouseItem.IsAir ? string.Empty : $" Holding {DescribeItemBrief(Main.mouseItem)}.";
		return $"{node.Label()}{unavailable}{position}.{details}{held}";
	}

	private string GetSemanticState()
	{
		if (_rootNodes.Count == 0)
		{
			return string.Empty;
		}

		if (_actionsPaneActive)
		{
			AccessibleInventoryItemAction action = _itemActions[_actionSelection];
			string item = CurrentNode.Entry?.ItemSlot is AccessibleInventoryItemSlot slot
				? DescribeItemBrief(slot.Item)
				: string.Empty;
			return $"actions|{string.Join('/', GetFocusPathIds())}|{item}|{action.Id}|{action.Label}|{_actionSelection}|{_itemActions.Count}|{DescribeItemBrief(Main.mouseItem)}";
		}

		AccessibleInventoryNode node = CurrentNode;
		string enabled = node.Entry?.IsEnabled.ToString() ?? string.Empty;
		string held = node.IsAction ? DescribeItemBrief(Main.mouseItem) : string.Empty;
		return $"tree|{CurrentLevel}|{string.Join('/', GetFocusPathIds())}|{node.Label()}|{enabled}|{CurrentLevelIndex}|{CurrentLevelCount}|{held}";
	}

	private string DescribeCurrentLevel()
	{
		return $"Level {CurrentLevel}.";
	}

	private List<string> GetFocusPathIds()
	{
		return GetFocusPathNodes().Select(node => node.Id).ToList();
	}

	private List<AccessibleInventoryNode> GetFocusPathNodes()
	{
		List<AccessibleInventoryNode> focusPath = [];
		IReadOnlyList<AccessibleInventoryNode> nodes = _rootNodes;
		foreach (int selection in _selectionPath)
		{
			if (nodes.Count == 0)
			{
				break;
			}

			AccessibleInventoryNode node = nodes[Math.Clamp(selection, 0, nodes.Count - 1)];
			focusPath.Add(node);
			nodes = node.Children;
		}
		return focusPath;
	}

	private void RestoreFocusPath(IReadOnlyList<string> focusPath, IReadOnlyList<int> fallbackSelections)
	{
		_selectionPath.Clear();
		IReadOnlyList<AccessibleInventoryNode> nodes = _rootNodes;
		int depthCount = Math.Max(1, fallbackSelections.Count);
		for (int depth = 0; depth < depthCount && nodes.Count > 0; depth++)
		{
			int preservedIndex = depth < focusPath.Count
				? FindNodeIndex(nodes, focusPath[depth])
				: -1;
			int fallbackIndex = depth < fallbackSelections.Count ? fallbackSelections[depth] : 0;
			int selection = preservedIndex >= 0 ? preservedIndex : Math.Clamp(fallbackIndex, 0, nodes.Count - 1);
			_selectionPath.Add(selection);

			AccessibleInventoryNode selectedNode = nodes[selection];
			if (depth + 1 >= depthCount || preservedIndex < 0 || !selectedNode.HasChildren)
			{
				break;
			}
			nodes = selectedNode.Children;
		}
	}

	private static int FindNodeIndex(IReadOnlyList<AccessibleInventoryNode> nodes, string id)
	{
		for (int index = 0; index < nodes.Count; index++)
		{
			if (nodes[index].Id == id)
			{
				return index;
			}
		}
		return -1;
	}

	private bool Pressed(KeyboardState keyboard, Keys key)
	{
		return keyboard.IsKeyDown(key) && _previousKeyboard.IsKeyUp(key);
	}

	private bool NavigationTriggered(KeyboardState keyboard, Keys key)
	{
		if (keyboard.IsKeyUp(key))
		{
			return false;
		}

		TimeSpan now = Main.gameTimeCache.TotalGameTime;
		if (_previousKeyboard.IsKeyUp(key))
		{
			_repeatingKey = key;
			_nextRepeat = now + NavigationRepeatDelay;
			return true;
		}
		if (_repeatingKey != key || now < _nextRepeat)
		{
			return false;
		}
		_nextRepeat = now + NavigationRepeatInterval;
		return true;
	}

	private static bool IsShiftDown(KeyboardState keyboard)
	{
		return keyboard.IsKeyDown(Keys.LeftShift) || keyboard.IsKeyDown(Keys.RightShift);
	}

	private static void ConsumeNavigationTriggers()
	{
		TriggersSet current = PlayerInput.Triggers.Current;
		TriggersSet justPressed = PlayerInput.Triggers.JustPressed;
		current.MapStyle = false;
		justPressed.MapStyle = false;
		current.MenuUp = current.MenuDown = current.MenuLeft = current.MenuRight = false;
		justPressed.MenuUp = justPressed.MenuDown = justPressed.MenuLeft = justPressed.MenuRight = false;
	}

	private static bool CanUse(AccessibleInventoryItemSlot slot)
	{
		return ReferenceEquals(slot.Items, Main.LocalPlayer.inventory) &&
			slot.Context == ItemSlot.Context.InventoryItem &&
			slot.Index is >= 0 and < 50 &&
			slot.Item.CanBeQuickUsed &&
			Main.mouseItem.IsAir &&
			Main.LocalPlayer.itemAnimation == 0 &&
			Main.LocalPlayer.ItemTimeIsZero;
	}

	private static bool CanEquip(AccessibleInventoryItemSlot slot)
	{
		return slot.Context is ItemSlot.Context.InventoryItem or ItemSlot.Context.ChestItem or ItemSlot.Context.BankItem or ItemSlot.Context.VoidItem &&
			slot.Item.maxStack == 1 &&
			ItemSlot.Equippable(slot.Items, slot.Context, slot.Index);
	}

	private static bool IsEquipmentContext(int context)
	{
		return context is
			ItemSlot.Context.ModdedAccessorySlot or
			ItemSlot.Context.ModdedVanityAccessorySlot or
			ItemSlot.Context.ModdedDyeSlot or
			ItemSlot.Context.EquipArmor or
			ItemSlot.Context.EquipArmorVanity or
			ItemSlot.Context.EquipAccessory or
			ItemSlot.Context.EquipAccessoryVanity or
			ItemSlot.Context.EquipDye or
			ItemSlot.Context.EquipGrapple or
			ItemSlot.Context.EquipMount or
			ItemSlot.Context.EquipMinecart or
			ItemSlot.Context.EquipPet or
			ItemSlot.Context.EquipLight or
			ItemSlot.Context.EquipMiscDye;
	}

	private static bool CanTakeOne(AccessibleInventoryItemSlot slot)
	{
		if (!SupportsTakeOne(slot.Context) || slot.Item.stack <= 1)
		{
			return false;
		}

		return Main.mouseItem.IsAir ||
			(Main.mouseItem.type == slot.Item.type &&
				Main.mouseItem.netID == slot.Item.netID &&
				ItemLoader.CanStack(Main.mouseItem, slot.Item) &&
				Main.mouseItem.stack < Main.mouseItem.maxStack);
	}

	private static bool SupportsTakeOne(int context)
	{
		return context is
			ItemSlot.Context.InventoryItem or
			ItemSlot.Context.InventoryCoin or
			ItemSlot.Context.InventoryAmmo or
			ItemSlot.Context.ChestItem or
			ItemSlot.Context.BankItem or
			ItemSlot.Context.VoidItem;
	}

	private static bool IsPlayerInventorySlot(AccessibleInventoryItemSlot slot)
	{
		return ReferenceEquals(slot.Items, Main.LocalPlayer.inventory) && slot.Index is >= 0 and < 58;
	}

	private static void TakeOneFromStack(AccessibleInventoryItemSlot slot)
	{
		ItemSlot.PickupItemIntoMouse(slot.Items, slot.Context, slot.Index, Main.LocalPlayer);
		ItemSlot.RefreshStackSplitCooldown();
		SoundEngine.PlaySound(SoundID.Grab);
	}

	private string BeginUsingItem(AccessibleInventoryItemSlot slot)
	{
		if (!CanUse(slot))
		{
			return "This item cannot be used right now.";
		}
		if (Main.netMode == NetmodeID.SinglePlayer && Main.autoPause)
		{
			return "This item cannot be used while Auto Pause is enabled.";
		}

		string itemName = slot.Item.AffixName();
		_pendingItemUse = new PendingInventoryItemUse(slot.Items, slot.Index, slot.Item.type, itemName);
		ItemSlot.PickupItemIntoMouse(slot.Items, slot.Context, slot.Index, Main.LocalPlayer);
		if (Main.mouseItem.type != _pendingItemUse.ItemType)
		{
			_pendingItemUse = null;
			return $"{itemName} could not be prepared for use.";
		}

		PlayerInput.TryEnteringFastUseModeForMouseItem();
		return $"Using {itemName}.";
	}

	private void RestoreUsedItemWhenReady()
	{
		if (_pendingItemUse is not PendingInventoryItemUse pending)
		{
			return;
		}
		if (Main.LocalPlayer.itemAnimation > 0 || !Main.LocalPlayer.ItemTimeIsZero)
		{
			pending.UseStarted = true;
			return;
		}
		if (PlayerInput.ShouldFastUseItem)
		{
			return;
		}

		_pendingItemUse = null;
		if (Main.mouseItem.IsAir || Main.mouseItem.type != pending.ItemType)
		{
			return;
		}

		bool returned = TryReturnUsedItemToInventory(pending);
		if (!pending.UseStarted)
		{
			TerrariumMod.ScreenReader.Output(returned
				? $"{pending.ItemName} could not be used and was returned to the inventory."
				: $"{pending.ItemName} could not be used and could not be returned because the inventory is full.");
		}
		else if (!returned)
		{
			TerrariumMod.ScreenReader.Output($"{pending.ItemName} was used but could not be returned because the inventory is full.");
		}
	}

	private static bool TryReturnUsedItemToInventory(PendingInventoryItemUse pending)
	{
		if (pending.Index >= 0 && pending.Index < pending.Items.Length)
		{
			Item destination = pending.Items[pending.Index];
			if (destination.IsAir)
			{
				Utils.Swap(ref pending.Items[pending.Index], ref Main.mouseItem);
			}
			else if (destination.type == Main.mouseItem.type &&
				destination.netID == Main.mouseItem.netID &&
				ItemLoader.CanStack(destination, Main.mouseItem) &&
				destination.stack < destination.maxStack)
			{
				ItemLoader.StackItems(destination, Main.mouseItem, out _);
				if (Main.mouseItem.stack <= 0)
				{
					Main.mouseItem = new Item();
				}
			}
		}

		if (!Main.mouseItem.IsAir)
		{
			Main.mouseItem = Main.LocalPlayer.GetItem(
				Main.LocalPlayer.whoAmI,
				Main.mouseItem,
				GetItemSettings.InventoryUIToInventorySettings);
		}
		Recipe.FindRecipes();
		return Main.mouseItem.IsAir;
	}

	private static string EquipItem(AccessibleInventoryItemSlot slot)
	{
		if (!CanEquip(slot))
		{
			return "This item cannot be equipped right now.";
		}

		Item itemBeforeEquip = slot.Item;
		string itemName = itemBeforeEquip.AffixName();
		ItemSlot.SwapEquip(slot.Items, slot.Context, slot.Index);
		if (ReferenceEquals(slot.Item, itemBeforeEquip))
		{
			return $"{itemName} could not be equipped.";
		}

		return slot.Item.IsAir
			? $"Equipped {itemName}."
			: $"Equipped {itemName}. {slot.Item.AffixName()} moved to the source slot.";
	}

	private static string UnequipItem(AccessibleInventoryItemSlot slot)
	{
		if (!IsEquipmentContext(slot.Context) || slot.Item.IsAir)
		{
			return "This equipment slot is already empty.";
		}

		Item item = slot.Item;
		string itemName = item.AffixName();
		if (!Main.LocalPlayer.ItemSpace(item).CanTakeItemToPersonalInventory)
		{
			return $"Cannot unequip {itemName} because the player inventory is full.";
		}

		int oldCursorOverride = Main.cursorOverride;
		try
		{
			Main.cursorOverride = 7;
			LeftClick(slot.Items, slot.Context, slot.Index);
		}
		finally
		{
			Main.cursorOverride = oldCursorOverride;
		}

		return ReferenceEquals(slot.Item, item)
			? $"{itemName} could not be unequipped."
			: $"Unequipped {itemName} to the player inventory.";
	}

	private static void DropItem(AccessibleInventoryItemSlot slot)
	{
		Player player = Main.LocalPlayer;
		player.DropSelectedItem(slot.Index, ref slot.Items[slot.Index]);
		Recipe.FindRecipes();
	}

	private static void LeftClick(Item[] items, int context, int index)
	{
		WithMouseButton(left: true, () => ItemSlot.LeftClick(items, context, index));
		Recipe.FindRecipes();
	}

	private static void RightClick(Item[] items, int context, int index)
	{
		WithMouseButton(left: false, () => ItemSlot.RightClick(items, context, index));
		Recipe.FindRecipes();
	}

	private static void LeftClickTrash(Player player)
	{
		WithMouseButton(left: true, () => ItemSlot.LeftClick(ref player.trashItem, ItemSlot.Context.TrashItem));
		Recipe.FindRecipes();
	}

	private static void RightClickTrash(Player player)
	{
		WithMouseButton(left: false, () => ItemSlot.RightClick(ref player.trashItem, ItemSlot.Context.TrashItem));
		Recipe.FindRecipes();
	}

	private static void LeftClickGuide()
	{
		WithMouseButton(left: true, () => ItemSlot.LeftClick(ref Main.guideItem, ItemSlot.Context.GuideItem));
		Recipe.FindRecipes();
	}

	private static void RightClickGuide()
	{
		WithMouseButton(left: false, () => ItemSlot.RightClick(ref Main.guideItem, ItemSlot.Context.GuideItem));
		Recipe.FindRecipes();
	}

	private static void LeftClickReforge()
	{
		WithMouseButton(left: true, () => ItemSlot.LeftClick(ref Main.reforgeItem, ItemSlot.Context.PrefixItem));
		Recipe.FindRecipes();
	}

	private static void RightClickReforge()
	{
		WithMouseButton(left: false, () => ItemSlot.RightClick(ref Main.reforgeItem, ItemSlot.Context.PrefixItem));
		Recipe.FindRecipes();
	}

	private static void WithMouseButton(bool left, Action action)
	{
		bool oldMouse = left ? Main.mouseLeft : Main.mouseRight;
		bool oldRelease = left ? Main.mouseLeftRelease : Main.mouseRightRelease;
		try
		{
			if (left)
			{
				Main.mouseLeft = true;
				Main.mouseLeftRelease = true;
			}
			else
			{
				Main.mouseRight = true;
				Main.mouseRightRelease = true;
			}
			action();
		}
		finally
		{
			if (left)
			{
				Main.mouseLeft = oldMouse;
				Main.mouseLeftRelease = oldRelease;
			}
			else
			{
				Main.mouseRight = oldMouse;
				Main.mouseRightRelease = oldRelease;
			}
		}
	}

	private static void ToggleFavorite(Item item)
	{
		if (!item.IsAir)
		{
			item.favorited = !item.favorited;
			item.newAndShiny = false;
		}
	}

	private static string DescribeItem(string slotName, Item item)
	{
		return item.IsAir ? $"Empty, {slotName}" : $"{DescribeItemBrief(item)}, {slotName}";
	}

	private static string DescribeItemBrief(Item item)
	{
		if (item.IsAir)
		{
			return "nothing";
		}
		string stack = item.stack > 1 ? $", {item.stack}" : string.Empty;
		string favorite = item.favorited ? ", favorited" : string.Empty;
		return $"{item.AffixName()}{stack}{favorite}";
	}

	private static string DescribeItemDetails(Item item, bool includeSummary = true)
	{
		if (item.IsAir)
		{
			return includeSummary ? "Empty slot." : string.Empty;
		}

		int capacity = 64 + (item.ToolTip?.Lines ?? 0);
		string[] text = new string[capacity];
		string[] names = new string[capacity];
		bool[] modifiers = new bool[capacity];
		bool[] badModifiers = new bool[capacity];
		int yoyoLogo = -1;
		int researchLine = -1;
		int lineCount = 1;
		Main.MouseText_DrawItemTooltip_GetLinesInfo(
			item,
			ref yoyoLogo,
			ref researchLine,
			item.knockBack,
			ref lineCount,
			text,
			modifiers,
			badModifiers,
			names,
			out int prefixLineIndex);
		List<TooltipLine> lines = ItemLoader.ModifyTooltips(
			item,
			ref lineCount,
			names,
			ref text,
			ref modifiers,
			ref badModifiers,
			ref yoyoLogo,
			out _,
			prefixLineIndex);
		return string.Join(
			". ",
			lines
				.Where(line => line.Visible && !string.IsNullOrWhiteSpace(line.Text) && (includeSummary || line.Name != "ItemName"))
				.Select(line => line.Text.Trim()));
	}

	private static string DescribeShopPrice(Item item)
	{
		if (item.IsAir)
		{
			return string.Empty;
		}
		Main.LocalPlayer.GetItemExpectedPrice(item, out _, out long buyPrice);
		long price = Math.Max(1, buyPrice * Math.Max(1, item.stack));
		return item.shopSpecialCurrency == -1
			? $"Buy price: {Main.ValueToCoins(price)}."
			: $"Buy price: {price} units of custom currency {item.shopSpecialCurrency}.";
	}

	private static string CombineDetails(string first, string? second)
	{
		if (string.IsNullOrWhiteSpace(first))
		{
			return second?.Trim() ?? string.Empty;
		}
		return string.IsNullOrWhiteSpace(second) ? first : $"{first} {second.Trim()}";
	}

	private static string DescribeRecipe(int availableIndex)
	{
		Recipe recipe = Main.recipe[Main.availableRecipe[availableIndex]];
		string stack = recipe.createItem.stack > 1 ? $", creates {recipe.createItem.stack}" : string.Empty;
		string selected = Main.focusRecipe == availableIndex ? ", selected" : string.Empty;
		return $"{recipe.createItem.AffixName()}{stack}{selected}";
	}

	private static string DescribeRecipeDetails(int availableIndex)
	{
		Recipe recipe = Main.recipe[Main.availableRecipe[availableIndex]];
		List<string> requirements = [];
		foreach (Item required in recipe.requiredItem)
		{
			if (required.IsAir)
			{
				continue;
			}
			string name = required.Name;
			if (recipe.ProcessGroupsForText(required.type, out string groupName))
			{
				name = groupName;
			}
			requirements.Add($"{required.stack} {name}");
		}
		List<string> stations = [];
		foreach (int tile in recipe.requiredTile)
		{
			if (tile >= 0)
			{
				stations.Add(Lang.GetMapObjectName(MapHelper.TileToLookup(tile, Recipe.GetRequiredTileStyle(tile))));
			}
		}
		stations.AddRange(recipe.Conditions.Select(condition => condition.Description.Value));
		string ingredients = requirements.Count == 0 ? "No item ingredients" : $"Requires {string.Join(", ", requirements)}";
		string stationText = stations.Count == 0 ? "No crafting station required" : $"At {string.Join(", ", stations.Distinct())}";
		return $"{DescribeItemDetails(recipe.createItem)} {ingredients}. {stationText}.";
	}

	private static void SelectOrCraftRecipe(int availableIndex)
	{
		Main.focusRecipe = availableIndex;
		if (!Main.InGuideCraftMenu)
		{
			Main.CraftItem(Main.recipe[Main.availableRecipe[availableIndex]]);
		}
	}

	private static void ToggleAccessoryVisibility(Player player, int index)
	{
		player.hideVisibleAccessory[index] = !player.hideVisibleAccessory[index];
		if (Main.netMode == NetmodeID.MultiplayerClient)
		{
			NetMessage.SendData(MessageID.PlayerInfo, number: player.whoAmI);
		}
	}

	private static string DescribeReforgeAction()
	{
		if (Main.reforgeItem.IsAir)
		{
			return "Reforge, unavailable. Place an item in the reforge slot.";
		}
		int price = GetReforgePrice(Main.reforgeItem);
		return $"Reforge {Main.reforgeItem.HoverName}, costs {Main.ValueToCoins(Math.Max(1, price))}";
	}

	private static int GetReforgePrice(Item item)
	{
		int price = item.value * item.stack;
		bool canApplyDiscount = true;
		if (ItemLoader.ReforgePrice(item, ref price, ref canApplyDiscount))
		{
			if (canApplyDiscount && Main.LocalPlayer.discountAvailable)
			{
				price = (int)(price * 0.8);
			}
			price = (int)(price * Main.LocalPlayer.currentShoppingSettings.PriceAdjustment);
			price /= 3;
		}
		return Math.Max(1, price);
	}

	private static void ReforgeItem()
	{
		Item item = Main.reforgeItem;
		if (item.IsAir || !ItemLoader.CanReforge(item))
		{
			return;
		}
		int price = GetReforgePrice(item);
		Player player = Main.LocalPlayer;
		if (!player.CanAfford(price))
		{
			TerrariumMod.ScreenReader.Output($"Cannot afford the reforge cost of {Main.ValueToCoins(price)}.");
			return;
		}
		player.BuyItem(price);
		ItemLoader.PreReforge(item);
		item.ResetPrefix();
		item.Prefix(-2);
		item.position.X = player.position.X + player.width / 2f - item.width / 2f;
		item.position.Y = player.position.Y + player.height / 2f - item.height / 2f;
		ItemLoader.PostReforge(item);
		PopupText.NewText(PopupTextContext.ItemReforge, item, item.stack, noStack: true);
		SoundEngine.PlaySound(SoundID.Item37);
	}

	private void OpenSettings()
	{
		RememberFocusForResume();
		_menuController.ShowRoot(new AccessibleSettingsMenuState(_menuController));
	}

	private void SaveAndExit()
	{
		SteamedWraps.StopPlaytimeTracking();
		SystemLoader.PreSaveAndQuit();
		Main.menuMode = 10;
		Main.gameMenu = true;
		WorldGen.SaveAndQuit();
	}

	private void RememberFocusForResume()
	{
		_resumeFocusPath = GetFocusPathIds();
		_restoreFocusOnNextActivation = true;
		_suppressInventoryCloseUntilRelease = true;
	}

	private void RestoreResumeFocus()
	{
		if (!_restoreFocusOnNextActivation)
		{
			return;
		}

		if (_resumeFocusPath is { Count: > 0 } focusPath)
		{
			RestoreFocusPath(focusPath, Enumerable.Repeat(0, focusPath.Count).ToList());
		}

		ClearResumeFocus();
	}

	private void ClearResumeFocus()
	{
		_resumeFocusPath = null;
		_restoreFocusOnNextActivation = false;
	}

	private void ConsumeResumeInventoryTrigger(KeyboardState keyboard)
	{
		if (!_suppressInventoryCloseUntilRelease)
		{
			return;
		}

		bool escapeHeld = keyboard.IsKeyDown(Keys.Escape);
		bool inventoryBindingHeld = PlayerInput.Triggers.Current.Inventory;
		// The accessible menu reads Escape directly, while Terraria also maps it to
		// Inventory. Keep the vanilla release latch closed until both views release it.
		PlayerInput.Triggers.Current.Inventory = false;
		PlayerInput.Triggers.JustPressed.Inventory = false;
		Main.LocalPlayer.controlInv = false;
		if (escapeHeld || inventoryBindingHeld)
		{
			Main.LocalPlayer.releaseInventory = false;
			return;
		}

		_suppressInventoryCloseUntilRelease = false;
		Main.LocalPlayer.releaseInventory = true;
	}

	private static void QuickStackNearby(Player player)
	{
		player.QuickStackAllChests();
		Recipe.FindRecipes();
		SoundEngine.PlaySound(SoundID.MenuTick);
	}

	private static void AddModNpcChatButton(List<AccessibleInventoryEntry> entries, string label, bool firstButton)
	{
		if (string.IsNullOrWhiteSpace(label))
		{
			return;
		}

		string id = firstButton ? "npc-mod-primary" : "npc-mod-secondary";
		entries.Add(new AccessibleInventoryEntry(
			id,
			() => label,
			() => $"Activate {label}.",
			() =>
			{
				if (NPCLoader.PreChatButtonClicked(firstButton))
				{
					NPCLoader.OnChatButtonClicked(firstButton);
				}
			}));
	}

	private static void ActivateVanillaChatButton(bool firstButton, Action action)
	{
		if (!NPCLoader.PreChatButtonClicked(firstButton))
		{
			return;
		}

		NPCLoader.OnChatButtonClicked(firstButton);
		action();
	}

	private static void OpenVanillaShop(int shopIndex)
	{
		Main.playerInventory = true;
		Main.stackSplit = 9999;
		Main.npcChatText = string.Empty;
		Main.SetNPCShopIndex(1);
		Main.instance.shop[Main.npcShop].SetupShop(shopIndex);
		SoundEngine.PlaySound(SoundID.MenuTick);
	}

	private static void OpenGuideCrafting()
	{
		Main.playerInventory = true;
		Main.npcChatText = string.Empty;
		Main.InReforgeMenu = false;
		Main.InGuideCraftMenu = true;
		SoundEngine.PlaySound(SoundID.MenuTick);
	}

	private static void OpenReforge()
	{
		Main.playerInventory = true;
		Main.npcChatText = string.Empty;
		Main.InGuideCraftMenu = false;
		Main.InReforgeMenu = true;
		SoundEngine.PlaySound(SoundID.MenuTick);
	}

	private static void CloseNpcConversation()
	{
		Main.LocalPlayer.SetTalkNPC(-1);
		Main.npcChatText = string.Empty;
		Main.npcChatCornerItem = 0;
		Main.SetNPCShopIndex(0);
		SoundEngine.PlaySound(SoundID.MenuClose);
	}

	private void OpenChestRename(Player player)
	{
		if (player.chest < 0 || Main.chest[player.chest] is not Chest chest)
		{
			return;
		}
		string initialName = chest.name;
		_menuController.ShowRoot(new AccessibleTextInputState(
			_menuController,
			"Chest name",
			initialName,
			20,
			value =>
			{
				Main.npcChatText = value;
				ChestUI.RenameChestSubmit(player);
				_menuController.Close();
			},
			cancel: _menuController.Close));
	}

	private static string GetContainerName(Player player)
	{
		return player.chest switch
		{
			>= 0 when Main.chest[player.chest] is Chest chest && !string.IsNullOrWhiteSpace(chest.name) => chest.name,
			>= 0 => "Chest",
			-2 => "Piggy Bank",
			-3 => "Safe",
			-4 => "Defender's Forge",
			-5 => "Void Vault",
			_ => "Container",
		};
	}

	private static string ArmorSlotName(int index) => index switch
	{
		0 => "Head armor slot",
		1 => "Body armor slot",
		2 => "Leg armor slot",
		_ => $"Armor slot {index + 1}",
	};

	private static string VanityArmorSlotName(int index) => index switch
	{
		10 => "Vanity head slot",
		11 => "Vanity body slot",
		12 => "Vanity leg slot",
		_ => $"Vanity armor slot {index - 9}",
	};

	private static string DyeSlotName(int index) => index switch
	{
		0 => "Head armor dye slot",
		1 => "Body armor dye slot",
		2 => "Leg armor dye slot",
		_ => $"Accessory dye slot {index - 2}",
	};

	private static string SplitWords(string text)
	{
		return string.Concat(text.Select((character, index) => index > 0 && char.IsUpper(character) ? $" {character}" : character.ToString()));
	}
}

internal sealed class AccessibleInventoryNode
{
	internal AccessibleInventoryNode(string id, Func<string> label, List<AccessibleInventoryNode> children)
	{
		Id = id;
		Label = label;
		Children = children;
	}

	private AccessibleInventoryNode(AccessibleInventoryEntry entry)
	{
		Id = entry.Id;
		Label = entry.Label;
		Entry = entry;
		Children = [];
	}

	internal static AccessibleInventoryNode FromEntry(AccessibleInventoryEntry entry) => new(entry);

	internal string Id { get; }
	internal Func<string> Label { get; }
	internal AccessibleInventoryEntry? Entry { get; }
	internal IReadOnlyList<AccessibleInventoryNode> Children { get; }
	internal bool HasChildren => Children.Count > 0;
	internal bool IsAction => Entry is not null;
	internal bool OpensSubmenu => HasChildren || Entry?.OpensSubmenu == true;
}

internal sealed class AccessibleInventoryEntry
{
	internal AccessibleInventoryEntry(
		string id,
		Func<string> label,
		Func<string> details,
		Action? primary = null,
		Action? secondary = null,
		Action? favorite = null,
		Func<bool>? enabled = null,
		Func<string>? selectionDetails = null,
		bool opensSubmenu = false,
		AccessibleInventoryItemSlot? itemSlot = null)
	{
		Id = id;
		Label = label;
		Details = details;
		Primary = primary;
		Secondary = secondary;
		Favorite = favorite;
		Enabled = enabled;
		SelectionDetails = selectionDetails;
		OpensSubmenu = opensSubmenu;
		ItemSlot = itemSlot;
	}

	internal string Id { get; }
	internal Func<string> Label { get; }
	internal Func<string> Details { get; }
	internal Action? Primary { get; }
	internal Action? Secondary { get; }
	internal Action? Favorite { get; }
	internal Func<bool>? Enabled { get; }
	internal Func<string>? SelectionDetails { get; }
	internal bool OpensSubmenu { get; }
	internal AccessibleInventoryItemSlot? ItemSlot { get; }
	internal bool IsEnabled => Enabled?.Invoke() ?? true;
}

internal sealed class AccessibleInventoryItemSlot
{
	internal AccessibleInventoryItemSlot(Item[] items, int context, int index, bool canFavorite)
	{
		Items = items;
		Context = context;
		Index = index;
		CanFavorite = canFavorite;
	}

	internal Item[] Items { get; }
	internal int Context { get; }
	internal int Index { get; }
	internal bool CanFavorite { get; }
	internal Item Item => Items[Index];
}

internal sealed class AccessibleInventoryItemAction
{
	internal AccessibleInventoryItemAction(string id, string label, string details, Func<string?> activate, bool returnsToInventory = false)
	{
		Id = id;
		Label = label;
		Details = details;
		Activate = activate;
		ReturnsToInventory = returnsToInventory;
	}

	internal string Id { get; }
	internal string Label { get; }
	internal string Details { get; }
	internal Func<string?> Activate { get; }
	internal bool ReturnsToInventory { get; }
}

internal sealed class PendingInventoryItemUse
{
	internal PendingInventoryItemUse(Item[] items, int index, int itemType, string itemName)
	{
		Items = items;
		Index = index;
		ItemType = itemType;
		ItemName = itemName;
	}

	internal Item[] Items { get; }
	internal int Index { get; }
	internal int ItemType { get; }
	internal string ItemName { get; }
	internal bool UseStarted { get; set; }
}
