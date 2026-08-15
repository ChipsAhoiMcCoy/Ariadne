#nullable enable

using System;
using System.Collections.Generic;
using Terraria;
using Terraria.Audio;
using Terraria.DataStructures;
using Terraria.ID;
using Ariadne.Menus;

namespace Ariadne.Ingame;

internal sealed class AccessibleGuideCraftingMenuState : AccessibleMenuState
{
	private readonly AccessibleInventoryController _inventoryController;
	private readonly Player _player;
	private readonly NPC? _guide;
	private readonly string _returnDialog;

	internal AccessibleGuideCraftingMenuState(
		AccessibleMenuController controller,
		AccessibleInventoryController inventoryController,
		Player player,
		NPC? guide,
		string returnDialog)
		: base(controller)
	{
		_inventoryController = inventoryController;
		_player = player;
		_guide = guide;
		_returnDialog = returnDialog;
	}

	protected override string Title => Main.guideItem.IsAir
		? "Guide Crafting Help"
		: $"Guide Recipes Using {Main.guideItem.AffixName()}";

	public override void OnActivate()
	{
		Main.playerInventory = false;
		Main.InReforgeMenu = false;
		Main.InGuideCraftMenu = true;
		Recipe.FindRecipes();
		base.OnActivate();
	}

	public override void Update(Microsoft.Xna.Framework.GameTime gameTime)
	{
		if (!GuideIsAvailable())
		{
			CloseToGameplay("Guide crafting closed because the Guide is no longer available.");
			return;
		}
		base.Update(gameTime);
	}

	protected override void BuildEntries(List<AccessibleMenuEntry> entries)
	{
		entries.Add(new AccessibleMenuEntry(
			DescribeMaterialSlot,
			() => Controller.Navigate(new AccessibleGuideMaterialMenuState(Controller, _player)),
			description: () => Main.guideItem.IsAir
				? "Choose a material from the player inventory to list every recipe that uses it."
				: $"Change the material used for recipe help. {AccessibleInventoryController.DescribeItemDetails(Main.guideItem)}",
			role: "submenu"));

		if (!Main.guideItem.IsAir)
		{
			entries.Add(new AccessibleMenuEntry(
				() => $"Return {Main.guideItem.AffixName()} to inventory",
				ReturnMaterial,
				description: () => "Return the Guide material to the player inventory."));
		}

		if (Main.guideItem.IsAir)
		{
			entries.Add(new AccessibleMenuEntry(
				() => "No material selected",
				description: () => "Open the material slot and choose an item marked as a material.",
				role: "information"));
			return;
		}

		if (Main.numAvailableRecipes <= 0)
		{
			entries.Add(new AccessibleMenuEntry(
				() => "No matching recipes",
				description: () => $"The Guide found no recipes using {Main.guideItem.AffixName()}.",
				role: "information"));
			return;
		}

		for (int availableIndex = 0; availableIndex < Main.numAvailableRecipes; availableIndex++)
		{
			int captured = availableIndex;
			entries.Add(new AccessibleMenuEntry(
				() => AccessibleInventoryController.DescribeRecipe(captured),
				() => Main.focusRecipe = captured,
				description: () => AccessibleInventoryController.DescribeRecipeDetails(captured),
				role: "information"));
		}
	}

	protected override void GoBack()
	{
		CloseGuideCrafting();
	}

	private string DescribeMaterialSlot()
	{
		return Main.guideItem.IsAir
			? "Material slot: empty"
			: $"Material slot: {Main.guideItem.stack} {Main.guideItem.AffixName()}";
	}

	private void ReturnMaterial()
	{
		if (Main.guideItem.IsAir)
		{
			return;
		}

		string name = Main.guideItem.AffixName();
		int amountBefore = Main.guideItem.stack;
		Main.guideItem = _player.GetItem(
			_player.whoAmI,
			Main.guideItem,
			GetItemSettings.InventoryUIToInventorySettings);
		int returned = amountBefore - (Main.guideItem.IsAir ? 0 : Main.guideItem.stack);
		Recipe.FindRecipes();
		SoundEngine.PlaySound(returned > 0 ? SoundID.Grab : SoundID.MenuClose);
	}

	private bool GuideIsAvailable()
	{
		return _player.active && !_player.dead && _guide is { active: true } && _player.TalkNPC?.whoAmI == _guide.whoAmI;
	}

	private void CloseGuideCrafting()
	{
		Main.InGuideCraftMenu = false;
		_player.dropItemCheck();
		Recipe.FindRecipes();
		Main.playerInventory = false;
		if (GuideIsAvailable() && _guide is not null)
		{
			_inventoryController.OpenNpcConversation(_guide, _returnDialog);
			return;
		}

		CloseToGameplay(string.Empty);
	}

	private void CloseToGameplay(string announcement)
	{
		Main.InGuideCraftMenu = false;
		_player.dropItemCheck();
		_player.SetTalkNPC(-1);
		Main.npcChatText = string.Empty;
		Main.npcChatCornerItem = 0;
		Recipe.FindRecipes();
		Controller.Close();
		Main.playerInventory = false;
		if (!string.IsNullOrWhiteSpace(announcement))
		{
			AriadneMod.ScreenReader.Output(announcement);
		}
	}
}

internal sealed class AccessibleGuideMaterialMenuState : AccessibleMenuState
{
	private readonly Player _player;

	internal AccessibleGuideMaterialMenuState(AccessibleMenuController controller, Player player)
		: base(controller)
	{
		_player = player;
	}

	protected override string Title => "Choose Guide Material";

	protected override bool RightArrowOpensSubmenu => false;

	public override void OnActivate()
	{
		Main.playerInventory = false;
		Main.InGuideCraftMenu = true;
		Recipe.FindRecipes();
		base.OnActivate();
	}

	public override void Update(Microsoft.Xna.Framework.GameTime gameTime)
	{
		if (!_player.active || _player.dead || _player.TalkNPC is not { active: true })
		{
			Main.InGuideCraftMenu = false;
			_player.dropItemCheck();
			_player.SetTalkNPC(-1);
			Main.npcChatText = string.Empty;
			Controller.Close();
			Main.playerInventory = false;
			AriadneMod.ScreenReader.Output("Guide crafting closed because the Guide is no longer available.");
			return;
		}
		base.Update(gameTime);
	}

	protected override void BuildEntries(List<AccessibleMenuEntry> entries)
	{
		for (int index = 0; index < 58; index++)
		{
			if (_player.inventory[index].IsAir || !_player.inventory[index].material)
			{
				continue;
			}

			int captured = index;
			entries.Add(new AccessibleMenuEntry(
				() => DescribeInventoryMaterial(captured),
				() => ChooseMaterial(captured),
				description: () => $"Use this item for Guide recipe help. {AccessibleInventoryController.DescribeItemDetails(_player.inventory[captured])}",
				enabled: () => !_player.inventory[captured].IsAir && _player.inventory[captured].material));
		}

		if (entries.Count == 0)
		{
			entries.Add(new AccessibleMenuEntry(
				() => "No materials available",
				description: () => "The hotbar, main inventory, coin slots, and ammo slots contain no items marked as crafting materials.",
				role: "information"));
		}
	}

	private string DescribeInventoryMaterial(int index)
	{
		Item item = _player.inventory[index];
		return $"{item.stack} {item.AffixName()}, {InventorySlotName(index)}";
	}

	private void ChooseMaterial(int index)
	{
		Item item = _player.inventory[index];
		if (item.IsAir || !item.material)
		{
			return;
		}

		Utils.Swap(ref _player.inventory[index], ref Main.guideItem);
		Recipe.FindRecipes();
		SoundEngine.PlaySound(SoundID.Grab);
		// The item grab is the action feedback; do not stack a menu-close cue on it.
		Controller.Back(playSound: false);
	}

	private static string InventorySlotName(int index)
	{
		return index switch
		{
			< 10 => $"hotbar slot {index + 1}",
			< 50 => $"inventory slot {index - 9}",
			< 54 => $"coin slot {index - 49}",
			_ => $"ammo slot {index - 53}",
		};
	}
}
