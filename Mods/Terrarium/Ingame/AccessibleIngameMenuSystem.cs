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
		DisableMap();
		_menuController = new AccessibleMenuController(inGame: true);
		_inventoryController = new AccessibleInventoryController(_menuController);
	}

	public override void PostUpdateInput()
	{
		DisableMap();
		if (Main.gameMenu || _menuController is null || _inventoryController is null)
		{
			_inventoryController?.Deactivate();
			_openingSettings = false;
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

	private static void DisableMap()
	{
		Main.mapEnabled = false;
		Main.mapFullscreen = false;
	}
}
