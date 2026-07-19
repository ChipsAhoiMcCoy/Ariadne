#nullable enable

using Terraria;
using Terraria.ModLoader;

namespace Terrarium.Menus;

[Autoload(Side = ModSide.Client)]
internal sealed class AccessibleMainMenuSystem : ModSystem
{
	private AccessibleMenuController? _controller;
	private bool _loggedActivation;

	public override void Load()
	{
		_controller = new AccessibleMenuController();
	}

	public override void PostUpdateInput()
	{
		if (!Main.gameMenu || _controller is null)
		{
			return;
		}

		if (Main.menuMode == 31)
		{
			_controller.ShowServerPasswordRequest();
			return;
		}

		if (Main.menuMode == 10000 ||
			(Main.menuMode == 888 && Main.MenuUI.CurrentState?.GetType().FullName == "Terraria.ModLoader.UI.UIMods"))
		{
			_controller.Replace(new AccessibleManageModsMenuState(_controller));
			return;
		}

		if (Main.menuMode == 10027 ||
			(Main.menuMode == 888 && Main.MenuUI.CurrentState?.GetType().FullName == "Terraria.ModLoader.Config.UI.UIModConfigList"))
		{
			_controller.Replace(new AccessibleModConfigListMenuState(_controller));
			return;
		}

		if (Main.menuMode is 14 or 15 or 882 || (Main.netMode == 1 && Main.menuMode < 10000 && Main.menuMode != 888))
		{
			_controller.ShowConnectionStatus();
			return;
		}

		if (Main.menuMode != 0)
		{
			return;
		}

		// Menu mode 888 delegates drawing and input to Main.MenuUI. Routing mode 0
		// here prevents the legacy main-menu rows from being drawn or activated.
		_controller.ShowRoot();

		if (!_loggedActivation)
		{
			Mod.Logger.Info("Terrarium custom main menu activated.");
			_loggedActivation = true;
		}
	}

	public override void Unload()
	{
		_controller = null;
		_loggedActivation = false;
	}
}
