#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Xna.Framework;
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
using Terraria.UI;
using Terrarium.Menus;

namespace Terrarium.Ingame;

internal sealed class AccessibleInventoryController
{
	private const int MenuPageSize = 10;
	private static readonly TimeSpan NavigationRepeatDelay = TimeSpan.FromMilliseconds(450);
	private static readonly TimeSpan NavigationRepeatInterval = TimeSpan.FromMilliseconds(85);
	private static readonly FieldInfo? InfoDisplaysField = typeof(InfoDisplayLoader).GetField("InfoDisplays", BindingFlags.Static | BindingFlags.NonPublic);
	private static readonly FieldInfo? BuilderTogglesField = typeof(BuilderToggleLoader).GetField("_drawOrder", BindingFlags.Static | BindingFlags.NonPublic);
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
	private readonly List<AccessibleInventoryCategory> _categories = [];
	private KeyboardState _previousKeyboard;
	private Keys? _repeatingKey;
	private TimeSpan _nextRepeat;
	private bool _active;
	private bool _categoryOpen;
	private int _categoryIndex;
	private int _entryIndex;
	private string _lastSemanticState = string.Empty;
	private string? _resumeCategoryId;
	private string? _resumeEntryId;
	private bool _restoreFocusOnNextActivation;
	private bool _suppressInventoryCloseUntilRelease;

	internal AccessibleInventoryController(AccessibleMenuController menuController)
	{
		_menuController = menuController;
	}

	internal void Update()
	{
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
		ConsumeNavigationTriggers();
		if (_categories.Count == 0)
		{
			_previousKeyboard = keyboard;
			return;
		}

		if (_repeatingKey is Keys repeatingKey && keyboard.IsKeyUp(repeatingKey))
		{
			_repeatingKey = null;
		}

		bool handled = false;
		if (Pressed(keyboard, Keys.Left) && _categoryOpen)
		{
			CloseCategory();
			handled = true;
		}
		else if (Pressed(keyboard, Keys.Right) && (!_categoryOpen || CurrentEntry.OpensSubmenu))
		{
			if (_categoryOpen)
			{
				ActivateEntry(secondary: false);
			}
			else
			{
				OpenCategory();
			}
			handled = true;
		}
		else if (NavigationTriggered(keyboard, Keys.Up))
		{
			MoveVertical(-1);
			handled = true;
		}
		else if (NavigationTriggered(keyboard, Keys.Down))
		{
			MoveVertical(1);
			handled = true;
		}
		else if (Pressed(keyboard, Keys.Home))
		{
			SetCurrentLevelSelection(0);
			handled = true;
		}
		else if (Pressed(keyboard, Keys.End))
		{
			SetCurrentLevelSelection(CurrentLevelCount - 1);
			handled = true;
		}
		else if (Pressed(keyboard, Keys.PageUp))
		{
			SetCurrentLevelSelection(Math.Max(0, CurrentLevelIndex - MenuPageSize));
			handled = true;
		}
		else if (Pressed(keyboard, Keys.PageDown))
		{
			SetCurrentLevelSelection(Math.Min(CurrentLevelCount - 1, CurrentLevelIndex + MenuPageSize));
			handled = true;
		}
		else if (Pressed(keyboard, Keys.Enter))
		{
			if (_categoryOpen)
			{
				ActivateEntry(secondary: IsShiftDown(keyboard));
			}
			else
			{
				OpenCategory();
			}
			Main.chatRelease = false;
			handled = true;
		}
		else if (_categoryOpen && Pressed(keyboard, Keys.F))
		{
			ToggleFavorite();
			handled = true;
		}
		else if (_categoryOpen && Pressed(keyboard, Keys.R))
		{
			ReadDetails();
			handled = true;
		}
		else if (Pressed(keyboard, Keys.F1))
		{
			ReadHelp();
			handled = true;
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
		_categoryOpen = false;
		_repeatingKey = null;
		_categories.Clear();
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
		_categoryOpen = false;
		_categoryIndex = 0;
		_entryIndex = 0;
		RebuildCategories();
		RestoreResumeFocus();
		ConsumeResumeInventoryTrigger(keyboard);
		_lastSemanticState = GetSemanticState();
		if (_categoryOpen)
		{
			TerrariumMod.ScreenReader.Output(
				$"Inventory tree resumed. Level 1, {CurrentCategory.Name}. {DescribeSelection()} " +
				"Use Up and Down Arrow keys to move through entries, Left Arrow to return to level 0, Enter for the primary action, Shift Enter for the secondary action, and F1 for help.");
		}
		else
		{
			TerrariumMod.ScreenReader.Output(
				$"Inventory tree, level 0. {DescribeCategorySelection()} " +
				"Use Up and Down Arrow keys to move between categories, Right Arrow or Enter to open a category, Home and End to move to the first and last category, and F1 for help.");
		}
	}

	private void RebuildCategories()
	{
		string? oldCategoryId = _categories.Count == 0 ? null : CurrentCategory.Id;
		string? oldEntryId = _categories.Count == 0 || CurrentCategory.Entries.Count == 0 ? null : CurrentEntry.Id;

		_categories.Clear();
		Player player = Main.LocalPlayer;
		AddPlayerInventoryCategories(player);
		AddContextCategories(player);
		AddEquipmentCategories(player);
		AddStatusCategories(player);
		AddCraftingCategories(player);
		AddActionCategories(player);

		if (_categories.Count == 0)
		{
			_categoryIndex = 0;
			_entryIndex = 0;
			return;
		}

		int preservedCategory = oldCategoryId is null ? -1 : _categories.FindIndex(category => category.Id == oldCategoryId);
		if (_categoryOpen && oldCategoryId is not null && preservedCategory < 0)
		{
			_categoryOpen = false;
		}
		_categoryIndex = preservedCategory >= 0 ? preservedCategory : Math.Clamp(_categoryIndex, 0, _categories.Count - 1);
		AccessibleInventoryCategory category = CurrentCategory;
		int preservedEntry = oldEntryId is null ? -1 : category.Entries.FindIndex(entry => entry.Id == oldEntryId);
		_entryIndex = preservedEntry >= 0 ? preservedEntry : Math.Clamp(_entryIndex, 0, category.Entries.Count - 1);
	}

	private void AddPlayerInventoryCategories(Player player)
	{
		AddItemCategory("hotbar", "Hotbar", player.inventory, ItemSlot.Context.InventoryItem, 0, 10, index => $"slot {index + 1}", canFavorite: true);
		AddItemCategory("inventory", "Inventory", player.inventory, ItemSlot.Context.InventoryItem, 10, 40, index => $"slot {index - 9}", canFavorite: true);

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
		AddCategory("coins-ammo", "Coins and Ammo", currencyEntries);

		AddCategory("trash", "Trash",
		[
			new AccessibleInventoryEntry(
				"trash-slot",
				() => DescribeItem("slot 1", player.trashItem),
				() => DescribeItemDetails(player.trashItem),
				() => LeftClickTrash(player),
				() => RightClickTrash(player),
				selectionDetails: () => DescribeItemDetails(player.trashItem, includeSummary: false))
		]);
	}

	private void AddContextCategories(Player player)
	{
		AddNpcConversationCategory(player);

		if (player.chest != -1)
		{
			ChestUI.GetContainerUsageInfo(out _, out Item[] container);
			int context = player.chest >= 0 ? ItemSlot.Context.ChestItem : ItemSlot.Context.BankItem;
			string containerName = GetContainerName(player);
			AddItemCategory("container", containerName, container, context, 0, container.Length, index => $"row {index / 10 + 1}, column {index % 10 + 1}");
			AddContainerActions(player, containerName);
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
			AddCategory("shop", "Shop", entries);
		}

		if (Main.InGuideCraftMenu)
		{
			AddCategory("guide", "Guide crafting",
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
			AddCategory("reforge", "Reforge",
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
	}

	private void AddNpcConversationCategory(Player player)
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

		AddCategory("npc-conversation", "NPC Conversation", entries);
	}

	private void AddEquipmentCategories(Player player)
	{
		AddItemCategory("armor", "Armor", player.armor, ItemSlot.Context.EquipArmor, 0, 3, ArmorSlotName, enabled: index => player.IsItemSlotUnlockedAndUsable(index) || Main.mouseItem.IsAir);
		AddItemCategory("accessories", "Accessories", player.armor, ItemSlot.Context.EquipAccessory, 3, 7, index => $"Accessory slot {index - 2}", enabled: index => player.IsItemSlotUnlockedAndUsable(index) || Main.mouseItem.IsAir);
		AddItemCategory("vanity-armor", "Vanity Armor", player.armor, ItemSlot.Context.EquipArmorVanity, 10, 3, VanityArmorSlotName, enabled: index => player.IsItemSlotUnlockedAndUsable(index) || Main.mouseItem.IsAir);
		AddItemCategory("vanity-accessories", "Vanity Accessories", player.armor, ItemSlot.Context.EquipAccessoryVanity, 13, 7, index => $"Vanity accessory slot {index - 12}", enabled: index => player.IsItemSlotUnlockedAndUsable(index) || Main.mouseItem.IsAir);
		AddItemCategory("equipment-dyes", "Equipment Dyes", player.dye, ItemSlot.Context.EquipDye, 0, player.dye.Length, DyeSlotName, enabled: index => player.IsItemSlotUnlockedAndUsable(index) || Main.mouseItem.IsAir);

		string[] miscNames = ["Pet", "Light pet", "Minecart", "Mount", "Grappling hook"];
		int[] miscContexts = [ItemSlot.Context.EquipPet, ItemSlot.Context.EquipLight, ItemSlot.Context.EquipMinecart, ItemSlot.Context.EquipMount, ItemSlot.Context.EquipGrapple];
		List<AccessibleInventoryEntry> miscEntries = [];
		for (int index = 0; index < player.miscEquips.Length; index++)
		{
			int captured = index;
			miscEntries.Add(ItemEntry($"misc-{captured}", () => $"{miscNames[captured]} slot", player.miscEquips, miscContexts[captured], captured));
		}
		AddCategory("misc-equipment", "Equipment", miscEntries);
		AddItemCategory("misc-dyes", "Equipment Dyes for Pets, Mounts, and Hooks", player.miscDyes, ItemSlot.Context.EquipMiscDye, 0, player.miscDyes.Length, index => $"Dye for {miscNames[index]}");

		AddModAccessoryCategories(player);
		AddLoadoutAndVisibilityCategory(player);
	}

	private void AddModAccessoryCategories(Player player)
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

		AddCategory("mod-accessories", "Modded Accessories", functional);
		AddCategory("mod-vanity-accessories", "Modded Vanity Accessories", vanity);
		AddCategory("mod-accessory-dyes", "Modded Accessory Dyes", dyeEntries);
	}

	private void AddLoadoutAndVisibilityCategory(Player player)
	{
		List<AccessibleInventoryEntry> entries = [];
		for (int index = 0; index < player.Loadouts.Length; index++)
		{
			int captured = index;
			entries.Add(new AccessibleInventoryEntry(
				$"loadout-{captured}",
				() => $"Loadout {captured + 1}, {(player.CurrentLoadoutIndex == captured ? "selected" : "not selected")}",
				() => $"Switch to equipment loadout {captured + 1}.",
				() => player.TrySwitchingLoadout(captured)));
		}

		for (int index = 3; index < player.hideVisibleAccessory.Length; index++)
		{
			int captured = index;
			entries.Add(new AccessibleInventoryEntry(
				$"visibility-accessory-{captured}",
				() => $"Accessory slot {captured - 2} visuals, {(player.hideVisibleAccessory[captured] ? "hidden" : "visible")}",
				() => "Toggle whether this functional accessory is shown on the character.",
				() => ToggleAccessoryVisibility(player, captured)));
		}

		entries.Add(new AccessibleInventoryEntry(
			"visibility-pet",
			() => $"Pet visuals, {(player.hideMisc[0] ? "hidden" : "visible")}",
			() => "Toggle pet visibility.",
			player.TogglePet));
		entries.Add(new AccessibleInventoryEntry(
			"visibility-light-pet",
			() => $"Light pet visuals, {(player.hideMisc[1] ? "hidden" : "visible")}",
			() => "Toggle light pet visibility.",
			player.ToggleLight));
		if (player.unlockedSuperCart)
		{
			entries.Add(new AccessibleInventoryEntry(
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
			entries.Add(new AccessibleInventoryEntry(
				$"visibility-mod-{captured}",
				() => $"{SplitWords(slot.Name)} visuals, {(slot.HideVisuals ? "hidden" : "visible")}",
				() => "Toggle whether this modded accessory is shown on the character.",
				() => slot.HideVisuals = !slot.HideVisuals));
		}

		AddCategory("loadouts-visibility", "Loadouts and Equipment Visibility", entries);
	}

	private void AddStatusCategories(Player player)
	{
		List<AccessibleInventoryEntry> buffs = [];
		for (int index = 0; index < Player.MaxBuffs; index++)
		{
			if (player.buffType[index] <= 0)
			{
				continue;
			}
			int captured = index;
			buffs.Add(new AccessibleInventoryEntry(
				$"buff-{captured}",
				() => DescribeBuff(player, captured),
				() => DescribeBuffDetails(player, captured),
				secondary: () => RemoveBuff(player, captured)));
		}
		AddCategory("buffs", "Buffs and Debuffs", buffs);

		AddInfoDisplayCategory(player);
		AddBuilderToggleCategory();
		AddMultiplayerCategory(player);
	}

	private void AddInfoDisplayCategory(Player player)
	{
		if (InfoDisplaysField?.GetValue(null) is not IEnumerable displayObjects)
		{
			return;
		}

		List<AccessibleInventoryEntry> entries = [];
		foreach (object? displayObject in displayObjects)
		{
			if (displayObject is not InfoDisplay display || !InfoDisplayLoader.Active(display))
			{
				continue;
			}
			entries.Add(new AccessibleInventoryEntry(
				$"info-{display.Type}",
				() => DescribeInfoDisplay(player, display),
				() => $"Toggle the {display.DisplayName.Value} informational display.",
				() => player.hideInfo[display.Type] = !player.hideInfo[display.Type]));
		}
		AddCategory("info-displays", "Informational Accessories", entries);
	}

	private void AddBuilderToggleCategory()
	{
		if (BuilderTogglesField?.GetValue(null) is not IEnumerable toggleObjects)
		{
			return;
		}

		List<AccessibleInventoryEntry> entries = [];
		foreach (object? toggleObject in toggleObjects)
		{
			if (toggleObject is not BuilderToggle toggle || !BuilderToggleLoader.Active(toggle))
			{
				continue;
			}
			entries.Add(new AccessibleInventoryEntry(
				$"builder-{toggle.Type}",
				toggle.DisplayValue,
				toggle.DisplayValue,
				() => CycleBuilderToggle(toggle),
				toggle.OnRightClick));
		}
		AddCategory("builder-toggles", "Builder Accessory Toggles", entries);
	}

	private void AddMultiplayerCategory(Player player)
	{
		if (Main.netMode == NetmodeID.SinglePlayer)
		{
			return;
		}

		string[] teams = ["No team", "Red team", "Green team", "Blue team", "Yellow team", "Pink team"];
		List<AccessibleInventoryEntry> entries =
		[
			new AccessibleInventoryEntry(
				"pvp",
				() => $"Player versus player, {(player.hostile ? "enabled" : "disabled")}",
				() => "Toggle player versus player combat.",
				() => TogglePvp(player))
		];
		for (int index = 0; index < teams.Length; index++)
		{
			int captured = index;
			entries.Add(new AccessibleInventoryEntry(
				$"team-{captured}",
				() => $"{teams[captured]}, {(player.team == captured ? "selected" : "not selected")}",
				() => $"Join {teams[captured]}.",
				() => SetTeam(player, captured),
				enabled: player.TeamChangeAllowed));
		}
		AddCategory("multiplayer", "Multiplayer", entries);
	}

	private void AddCraftingCategories(Player player)
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
		AddCategory("crafting", Main.InGuideCraftMenu ? "Guide Recipes" : "Crafting", entries);
	}

	private void AddActionCategories(Player player)
	{
		List<AccessibleInventoryEntry> actions =
		[
			new("sort-inventory", () => Language.GetTextValue("GameUI.SortInventory"), () => "Sort the main inventory.", ItemSorting.SortInventory),
			new("sort-ammo", () => "Sort ammo", () => "Consolidate and sort the ammo slots.", ItemSorting.SortAmmo),
			new("settings", () => Lang.menu[14].Value, () => "Open the accessible in-game settings menu.", OpenSettings, opensSubmenu: true),
			new("close-inventory", () => Lang.menu[118].Value, () => "Close the inventory and return to gameplay.", player.ToggleInv),
		];
		if (player.chest == -1 && Main.npcShop == 0)
		{
			actions.Insert(0, new AccessibleInventoryEntry(
				"quick-stack-nearby",
				() => Language.GetTextValue("GameUI.QuickStackToNearby"),
				() => "Quick stack matching inventory items into nearby chests.",
				() => QuickStackNearby(player)));
		}

		AddCategory("inventory-actions", "Inventory Actions", actions);
	}

	private void AddContainerActions(Player player, string containerName)
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
		AddCategory("container-actions", $"{containerName} Actions", actions);
	}

	private void AddItemCategory(
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
		AddCategory(id, name, entries);
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
			() => CombineDetails(DescribeItemDetails(items[index], includeSummary: false), extraDetails?.Invoke()));
	}

	private void AddCategory(string id, string name, List<AccessibleInventoryEntry> entries)
	{
		if (entries.Count > 0)
		{
			_categories.Add(new AccessibleInventoryCategory(id, name, entries));
		}
	}

	private AccessibleInventoryCategory CurrentCategory => _categories[_categoryIndex];

	private AccessibleInventoryEntry CurrentEntry => CurrentCategory.Entries[_entryIndex];

	private int CurrentLevelIndex => _categoryOpen ? _entryIndex : _categoryIndex;

	private int CurrentLevelCount => _categoryOpen ? CurrentCategory.Entries.Count : _categories.Count;

	private void MoveCategory(int offset)
	{
		_categoryIndex = (_categoryIndex + offset + _categories.Count) % _categories.Count;
		_entryIndex = 0;
		SoundEngine.PlaySound(SoundID.MenuTick);
		AnnounceCategory();
	}

	private void OpenCategory()
	{
		_categoryOpen = true;
		_entryIndex = Math.Clamp(_entryIndex, 0, CurrentCategory.Entries.Count - 1);
		SoundEngine.PlaySound(SoundID.MenuOpen);
		_lastSemanticState = GetSemanticState();
		TerrariumMod.ScreenReader.Output($"Level 1, {CurrentCategory.Name}. {DescribeSelection()}");
	}

	private void CloseCategory()
	{
		_categoryOpen = false;
		SoundEngine.PlaySound(SoundID.MenuClose);
		AnnounceCategory();
	}

	private void MoveVertical(int direction)
	{
		if (!_categoryOpen)
		{
			MoveCategory(direction);
			return;
		}

		int count = CurrentCategory.Entries.Count;
		if (count <= 1)
		{
			return;
		}
		SetEntry((_entryIndex + direction + count) % count);
	}

	private void SetCurrentLevelSelection(int index)
	{
		if (_categoryOpen)
		{
			SetEntry(index);
			return;
		}

		SetCategory(index);
	}

	private void SetCategory(int index)
	{
		index = Math.Clamp(index, 0, _categories.Count - 1);
		if (index == _categoryIndex)
		{
			return;
		}
		_categoryIndex = index;
		_entryIndex = 0;
		SoundEngine.PlaySound(SoundID.MenuTick);
		AnnounceCategory();
	}

	private void SetEntry(int index)
	{
		index = Math.Clamp(index, 0, CurrentCategory.Entries.Count - 1);
		if (index == _entryIndex)
		{
			return;
		}
		_entryIndex = index;
		SoundEngine.PlaySound(SoundID.MenuTick);
		AnnounceSelection();
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

		Action? action = secondary ? entry.Secondary : entry.Primary;
		action ??= secondary ? entry.Primary : entry.Secondary;
		if (action is null)
		{
			TerrariumMod.ScreenReader.Output($"{entry.Label()}, no {(secondary ? "secondary" : "primary")} action.");
			return;
		}

		action();
		if (CanNavigateInventory())
		{
			RebuildCategories();
			SoundEngine.PlaySound(SoundID.MenuTick);
			AnnounceSelection();
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
		string currentLevel = _categoryOpen
			? $"Level 1, {CurrentCategory.Name}. {DescribeSelection()}"
			: $"Level 0. {DescribeCategorySelection()}";
		TerrariumMod.ScreenReader.Output(
			$"Inventory tree help. {currentLevel} " +
			"At level 0, Up and Down move between categories and Right Arrow or Enter opens the focused category. At level 1, Up and Down move through that category's entries and Left Arrow returns to level 0. Right Arrow opens an entry announced as a submenu, such as Settings. Both levels wrap. Home and End move to the first and last option, and Page Up and Page Down move by ten options. At level 1, Enter performs the normal left click action and Shift Enter performs the normal right click action, such as splitting stacks or removing a removable buff. F toggles favorite for inventory items. R reads the full item tooltip or entry details. Escape uses Terraria's normal inventory close control.");
	}

	private void AnnounceCategory()
	{
		_lastSemanticState = GetSemanticState();
		TerrariumMod.ScreenReader.Output(DescribeCategorySelection());
	}

	private void AnnounceSelection()
	{
		_lastSemanticState = GetSemanticState();
		TerrariumMod.ScreenReader.Output(DescribeSelection());
	}

	private void AnnounceExternalStateChange()
	{
		string state = GetSemanticState();
		if (state == _lastSemanticState)
		{
			return;
		}
		_lastSemanticState = state;
		TerrariumMod.ScreenReader.Output(_categoryOpen ? DescribeSelection() : DescribeCategorySelection());
	}

	private string DescribeCategorySelection()
	{
		return $"{CurrentCategory.Name}, submenu, {_categoryIndex + 1} of {_categories.Count}.";
	}

	private string DescribeSelection()
	{
		AccessibleInventoryEntry entry = CurrentEntry;
		string unavailable = entry.IsEnabled ? string.Empty : ", unavailable";
		string role = entry.OpensSubmenu ? ", submenu" : string.Empty;
		string position = entry.SelectionDetails is null ? $", {_entryIndex + 1} of {CurrentCategory.Entries.Count}" : string.Empty;
		string details = entry.SelectionDetails?.Invoke() ?? string.Empty;
		if (!string.IsNullOrWhiteSpace(details))
		{
			details = $" {details}";
		}
		string held = Main.mouseItem.IsAir ? string.Empty : $" Holding {DescribeItemBrief(Main.mouseItem)}.";
		return $"{entry.Label()}{role}{unavailable}{position}.{details}{held}";
	}

	private string GetSemanticState()
	{
		if (_categories.Count == 0 || CurrentCategory.Entries.Count == 0)
		{
			return string.Empty;
		}
		if (!_categoryOpen)
		{
			return $"0|{CurrentCategory.Id}|{CurrentCategory.Name}|{_categoryIndex}|{_categories.Count}";
		}
		return $"1|{CurrentCategory.Id}|{CurrentEntry.Id}|{CurrentEntry.Label()}|{CurrentEntry.IsEnabled}|{DescribeItemBrief(Main.mouseItem)}";
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

	private static string DescribeBuff(Player player, int index)
	{
		int type = player.buffType[index];
		return type <= 0 ? "Empty buff slot" : Lang.GetBuffName(type);
	}

	private static string DescribeBuffDetails(Player player, int index)
	{
		int type = player.buffType[index];
		if (type <= 0)
		{
			return "The buff is no longer active.";
		}
		string name = Lang.GetBuffName(type);
		string tooltip = Main.GetBuffTooltip(player, type);
		int rarity = 0;
		BuffLoader.ModifyBuffText(type, ref name, ref tooltip, ref rarity);
		string duration = player.buffTime[index] > 2 && !Main.buffNoTimeDisplay[type]
			? $" {Lang.LocalizedDuration(TimeSpan.FromSeconds(player.buffTime[index] / 60d), abbreviated: false, showAllAvailableUnits: true)} remaining."
			: string.Empty;
		return $"{name}. {tooltip}{duration}";
	}

	private static void RemoveBuff(Player player, int index)
	{
		int type = player.buffType[index];
		if (type > 0 && BuffLoader.RightClick(type, index))
		{
			Main.TryRemovingBuff(index, type);
		}
	}

	private static string DescribeInfoDisplay(Player player, InfoDisplay display)
	{
		string name = display.DisplayName.Value;
		Color color = Color.White;
		Color shadow = Color.Black;
		string value = display.DisplayValue(ref color, ref shadow);
		return $"{name}, {(player.hideInfo[display.Type] ? "hidden" : "visible")}. {value}";
	}

	private static void CycleBuilderToggle(BuilderToggle toggle)
	{
		SoundStyle? sound = SoundID.MenuTick;
		if (toggle.OnLeftClick(ref sound))
		{
			toggle.CurrentState = (toggle.CurrentState + 1) % toggle.NumberOfStates;
			if (sound is SoundStyle soundStyle)
			{
				SoundEngine.PlaySound(soundStyle);
			}
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

	private static void TogglePvp(Player player)
	{
		player.hostile = !player.hostile;
		NetMessage.SendData(MessageID.TogglePVP, number: player.whoAmI);
	}

	private static void SetTeam(Player player, int team)
	{
		if (player.team == team || !player.TeamChangeAllowed())
		{
			return;
		}
		player.team = team;
		NetMessage.SendData(MessageID.PlayerTeam, number: player.whoAmI);
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

	private void RememberFocusForResume()
	{
		_resumeCategoryId = CurrentCategory.Id;
		_resumeEntryId = CurrentEntry.Id;
		_restoreFocusOnNextActivation = true;
		_suppressInventoryCloseUntilRelease = true;
	}

	private void RestoreResumeFocus()
	{
		if (!_restoreFocusOnNextActivation)
		{
			return;
		}

		int categoryIndex = _resumeCategoryId is null
			? -1
			: _categories.FindIndex(category => category.Id == _resumeCategoryId);
		if (categoryIndex >= 0)
		{
			_categoryIndex = categoryIndex;
			_categoryOpen = true;
			AccessibleInventoryCategory category = CurrentCategory;
			int entryIndex = _resumeEntryId is null
				? -1
				: category.Entries.FindIndex(entry => entry.Id == _resumeEntryId);
			_entryIndex = entryIndex >= 0 ? entryIndex : 0;
		}

		ClearResumeFocus();
	}

	private void ClearResumeFocus()
	{
		_resumeCategoryId = null;
		_resumeEntryId = null;
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

internal sealed class AccessibleInventoryCategory
{
	internal AccessibleInventoryCategory(string id, string name, List<AccessibleInventoryEntry> entries)
	{
		Id = id;
		Name = name;
		Entries = entries;
	}

	internal string Id { get; }
	internal string Name { get; }
	internal List<AccessibleInventoryEntry> Entries { get; }
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
		bool opensSubmenu = false)
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
	internal bool IsEnabled => Enabled?.Invoke() ?? true;
}
