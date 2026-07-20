#nullable enable

using Terraria;
using Terraria.GameInput;
using Terraria.ModLoader;
using Terrarium.Menus;

namespace Terrarium.Ingame;

[Autoload(Side = ModSide.Client)]
internal sealed class AccessibleIngameMenuSystem : ModSystem
{
	private AccessibleMenuController? _menuController;
	private AccessibleInventoryController? _inventoryController;
	private bool _openingSettings;

	public override void Load()
	{
		_menuController = new AccessibleMenuController(inGame: true);
		_inventoryController = new AccessibleInventoryController(_menuController);
	}

	public override void PostUpdateInput()
	{
		if (Main.gameMenu || _menuController is null || _inventoryController is null)
		{
			_inventoryController?.Deactivate();
			_openingSettings = false;
			return;
		}

		if (!_menuController.IsActive && ShouldOpenAccessibleMap())
		{
			PlayerInput.Triggers.Current.MapFull = false;
			PlayerInput.Triggers.JustPressed.MapFull = false;
			Main.mapFullscreen = false;
			_inventoryController.Deactivate();
			_menuController.ShowRoot(new AccessibleMapMenuState(_menuController));
			return;
		}

		if (Main.hairWindow)
		{
			Main.CancelHairWindow();
			_inventoryController.Deactivate();
			_menuController.ShowRoot(new AccessibleStylistMenuState(_menuController, Main.LocalPlayer));
			return;
		}

		if (Main.clothesWindow)
		{
			Main.CancelClothesWindow(quiet: true);
			_inventoryController.Deactivate();
			_menuController.ShowRoot(new AccessibleDresserMenuState(_menuController, Main.LocalPlayer));
			return;
		}

		if (Main.ingameOptionsWindow && !_openingSettings)
		{
			_openingSettings = true;
			IngameOptions.Close();
			_inventoryController.Deactivate();
			_menuController.ShowRoot(new AccessibleSettingsMenuState(_menuController));
			return;
		}

		if (!Main.ingameOptionsWindow)
		{
			_openingSettings = false;
		}
		if (_menuController.IsActive)
		{
			PlayerInput.Triggers.Current.Inventory = false;
			PlayerInput.Triggers.JustPressed.Inventory = false;
		}

		_inventoryController.Update();
	}

	public override void Unload()
	{
		_inventoryController = null;
		_menuController = null;
		_openingSettings = false;
	}

	private static bool ShouldOpenAccessibleMap()
	{
		if (Main.mapFullscreen)
		{
			return true;
		}

		return PlayerInput.Triggers.JustPressed.MapFull &&
			!Main.LocalPlayer.dead &&
			!Main.drawingPlayerChat &&
			!Main.editSign &&
			!Main.editChest &&
			!Main.ingameOptionsWindow &&
			!Main.inFancyUI &&
			Main.InGameUI.CurrentState is null &&
			!(Main.CreativeMenu.Enabled && !Main.CreativeMenu.Blocked) &&
			!PlayerInput.WritingText;
	}
}
