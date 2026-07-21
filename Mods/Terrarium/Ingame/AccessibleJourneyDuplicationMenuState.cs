#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.Audio;
using Terraria.DataStructures;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.UI;
using Terrarium.Menus;

namespace Terrarium.Ingame;

internal sealed class AccessibleJourneyDuplicationMenuState : AccessibleMenuState
{
	private readonly List<Item> _researchedItems = [];
	private readonly int _hierarchyLevel;
	private bool _actionsPaneActive;
	private int _itemSelection;
	private Item? _actionItem;
	private string _lastResult = string.Empty;

	internal AccessibleJourneyDuplicationMenuState(AccessibleMenuController controller, int hierarchyLevel)
		: base(controller)
	{
		_hierarchyLevel = hierarchyLevel;
	}

	protected override string Title => _actionsPaneActive && _actionItem is not null
		? $"Duplication actions for {_actionItem.AffixName()}"
		: $"Journey Duplication, {_researchedItems.Count} researched items";

	protected override bool KeepsInventoryOpen => true;

	protected override int? HierarchyLevel => _hierarchyLevel;

	protected override bool RightArrowOpensSubmenu => false;

	protected override string AdditionalControlHint => "    Tab: pane";

	protected override string AdditionalNavigationInstructions =>
		" Tab switches between researched items and alternate duplication actions.";

	public override void OnActivate()
	{
		RefreshResearchedItems();
		base.OnActivate();
	}

	protected override void BuildEntries(List<AccessibleMenuEntry> entries)
	{
		if (_actionsPaneActive && _actionItem is not null)
		{
			BuildActionEntries(entries, _actionItem);
			return;
		}

		foreach (Item item in _researchedItems)
		{
			Item captured = item;
			entries.Add(new AccessibleMenuEntry(
				() => captured.AffixName(),
				() => DuplicateToCursor(captured, captured.maxStack),
				description: () => $"Press Enter to duplicate a full stack of {captured.maxStack} onto the cursor. Press Tab for alternate actions and item details. {DescribeCursor()}",
				enabled: () => CanDuplicateToCursor(captured),
				role: "action",
				adjustmentAnnouncement: () => _lastResult));
		}
	}

	protected override bool ActivationAdjustsValue(AccessibleMenuEntry entry) => entry.Activate is not null;

	protected override bool PlaysActivationTick(AccessibleMenuEntry entry) => false;

	protected override bool HandleAdditionalInput(KeyboardState keyboard, GameTime gameTime)
	{
		if (Pressed(keyboard, Keys.Left))
		{
			GoBack();
			return true;
		}
		if (!Pressed(keyboard, Keys.Tab))
		{
			return false;
		}

		if (_actionsPaneActive)
		{
			CloseActionsPane();
		}
		else
		{
			OpenActionsPane();
		}
		return true;
	}

	protected override void GoBack()
	{
		if (_actionsPaneActive)
		{
			CloseActionsPane();
			return;
		}
		base.GoBack();
	}

	protected override void AddContextHelpTopics(List<AccessibleHelpTopic> topics)
	{
		topics.Add(new(
			"Tab",
			_actionsPaneActive
				? "Return to the researched-items pane and the item that opened these actions."
				: "Open alternate actions for the focused item. Enter on the researched-items pane always duplicates one full stack onto the cursor."));
	}

	private void OpenActionsPane()
	{
		if (_researchedItems.Count == 0)
		{
			Announce("There are no researched items with duplication actions.");
			return;
		}

		_itemSelection = SelectedIndex;
		_actionItem = _researchedItems[_itemSelection].Clone();
		_actionsPaneActive = true;
		_lastResult = string.Empty;
		RebuildEntries();
		SetSelectionWithoutAnnouncement(0);
		SoundEngine.PlaySound(SoundID.MenuOpen);
		Announce($"Actions pane for {_actionItem.AffixName()}. {DescribeSelection()} Tab returns to the researched-items pane.");
	}

	private void CloseActionsPane()
	{
		_actionsPaneActive = false;
		_actionItem = null;
		RebuildEntries();
		SetSelectionWithoutAnnouncement(_itemSelection);
		SoundEngine.PlaySound(SoundID.MenuClose);
		Announce($"Researched-items pane. {DescribeSelection()}");
	}

	private void BuildActionEntries(List<AccessibleMenuEntry> entries, Item item)
	{
		entries.Add(new AccessibleMenuEntry(
			() => "Take one onto cursor",
			() => DuplicateToCursor(item, 1),
			description: () => $"Duplicate one {item.AffixName()} onto the cursor. {DescribeCursor()}",
			enabled: () => CanDuplicateToCursor(item),
			role: "action",
			adjustmentAnnouncement: () => _lastResult));
		entries.Add(new AccessibleMenuEntry(
			() => $"Add full stack to inventory, {item.maxStack}",
			() => DuplicateToInventory(item),
			description: () => "Place as much as one full duplicated stack as will fit directly into the main inventory.",
			enabled: () => GetMainInventoryCapacity(item) > 0,
			role: "action",
			adjustmentAnnouncement: () => _lastResult));
		entries.Add(new AccessibleMenuEntry(
			() => "Item details",
			description: () => AccessibleInventoryController.DescribeItemDetails(item),
			role: "information"));
	}

	private void RefreshResearchedItems()
	{
		List<int> itemTypes = [];
		Main.LocalPlayerCreativeTracker.ItemSacrifices.FillListOfItemsThatCanBeObtainedInfinitely(itemTypes);
		_researchedItems.Clear();
		foreach (int itemType in itemTypes.Distinct())
		{
			if (!ContentSamples.ItemsByType.TryGetValue(itemType, out Item? sample) || sample is null || sample.IsAir)
			{
				continue;
			}
			_researchedItems.Add(sample.Clone());
		}
		_researchedItems.Sort((left, right) => string.Compare(left.AffixName(), right.AffixName(), StringComparison.CurrentCultureIgnoreCase));
	}

	private static bool CanDuplicateToCursor(Item item)
	{
		return Main.mouseItem.IsAir ||
			(Main.mouseItem.type == item.type &&
				Main.mouseItem.netID == item.netID &&
				ItemLoader.CanStack(Main.mouseItem, item) &&
				Main.mouseItem.stack < Main.mouseItem.maxStack);
	}

	private static string DescribeCursor()
	{
		return Main.mouseItem.IsAir
			? "The cursor is empty."
			: $"The cursor holds {Main.mouseItem.stack} {Main.mouseItem.AffixName()}.";
	}

	private void DuplicateToCursor(Item item, int requestedAmount)
	{
		Item duplicate = CreateDuplicate(item, requestedAmount);
		int amountBefore = Main.mouseItem.IsAir ? 0 : Main.mouseItem.stack;
		if (Main.mouseItem.IsAir)
		{
			Main.mouseItem = duplicate;
		}
		else
		{
			ItemLoader.StackItems(Main.mouseItem, duplicate, out _, infiniteSource: true);
		}

		int added = Main.mouseItem.stack - amountBefore;
		if (added > 0)
		{
			SoundEngine.PlaySound(SoundID.Grab);
		}
		_lastResult = added > 0
			? $"Duplicated {added} {item.AffixName()} onto the cursor. The cursor now holds {Main.mouseItem.stack}."
			: $"No {item.AffixName()} could be added to the cursor.";
		Recipe.FindRecipes();
	}

	private void DuplicateToInventory(Item item)
	{
		int amount = Math.Min(item.maxStack, GetMainInventoryCapacity(item));
		if (amount <= 0)
		{
			_lastResult = $"There is no room for {item.AffixName()} in the main inventory.";
			return;
		}

		Item remaining = Main.LocalPlayer.GetItem(
			Main.myPlayer,
			CreateDuplicate(item, amount),
			GetItemSettings.InventoryEntityToPlayerInventorySettings);
		int remainder = remaining.IsAir ? 0 : remaining.stack;
		int added = amount - remainder;
		_lastResult = remainder == 0
			? $"Added {added} {item.AffixName()} directly to the inventory."
			: $"Added {added} {item.AffixName()} to the inventory; {remainder} could not fit.";
		Recipe.FindRecipes();
	}

	private static Item CreateDuplicate(Item item, int stack)
	{
		Item duplicate = item.Clone();
		duplicate.stack = Math.Clamp(stack, 1, Math.Max(1, duplicate.maxStack));
		duplicate.OnCreated(new JourneyDuplicationItemCreationContext());
		return duplicate;
	}

	private static int GetMainInventoryCapacity(Item item)
	{
		int capacity = 0;
		Item[] inventory = Main.LocalPlayer.inventory;
		for (int index = 0; index < Math.Min(50, inventory.Length); index++)
		{
			Item slot = inventory[index];
			if (slot.IsAir)
			{
				capacity += item.maxStack;
			}
			else if (slot.type == item.type && slot.netID == item.netID && ItemLoader.CanStack(slot, item))
			{
				capacity += Math.Max(0, slot.maxStack - slot.stack);
			}

			if (capacity >= item.maxStack)
			{
				return item.maxStack;
			}
		}
		return capacity;
	}
}
