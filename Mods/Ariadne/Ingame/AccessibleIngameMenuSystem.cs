#nullable enable

using Terraria;
using Terraria.GameInput;
using Terraria.ModLoader;
using Ariadne.Ingame.Scanner;
using Ariadne.Ingame.Freecam;
using Ariadne.Menus;

namespace Ariadne.Ingame;

[Autoload(Side = ModSide.Client)]
internal sealed class AccessibleIngameMenuSystem : ModSystem
{
	private AccessibleMenuController? _menuController;
	private AccessibleInventoryController? _inventoryController;
	private ScannerSession? _scannerSession;
	private bool _openingSettings;

	public override void Load()
	{
		_menuController = new AccessibleMenuController(inGame: true);
		_inventoryController = new AccessibleInventoryController(_menuController);
		_scannerSession = new ScannerSession(_menuController, _inventoryController);
	}

	public override void PostUpdateInput()
	{
		if (Main.gameMenu || _menuController is null || _inventoryController is null || _scannerSession is null)
		{
			_inventoryController?.Deactivate();
			_scannerSession?.Reset();
			_openingSettings = false;
			return;
		}

		_scannerSession.SynchronizeState();
		if (_scannerSession.IsOpen && (!Main.LocalPlayer.active || Main.LocalPlayer.dead))
		{
			_scannerSession.Close();
			_inventoryController.Deactivate();
			return;
		}
		_scannerSession.ConsumeInput();
		if (!_menuController.IsActive && ShouldOpenScanner())
		{
			_inventoryController.Deactivate();
			_scannerSession.Open();
			_scannerSession.ConsumeInput();
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

		if (!_menuController.IsActive && ShouldOpenNpcConversation(out NPC? conversationNpc) && conversationNpc is not null)
		{
			_inventoryController.OpenNpcConversation(conversationNpc, Main.npcChatText);
			return;
		}

		if (!_menuController.IsActive && ShouldOpenSign(out int signIndex))
		{
			_inventoryController.OpenSign(Main.LocalPlayer, signIndex);
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

	public override void PostUpdateEverything()
	{
		_scannerSession?.UpdateVerification();
	}

	public override void OnWorldLoad()
	{
		_scannerSession?.Reset();
	}

	public override void OnWorldUnload()
	{
		_scannerSession?.Reset();
	}

	public override void Unload()
	{
		_scannerSession = null;
		_inventoryController = null;
		_menuController = null;
		_openingSettings = false;
	}

	private static bool ShouldOpenScanner()
	{
		Player player = Main.LocalPlayer;
		return AriadneMod.OpenScannerKeybind?.JustPressed == true &&
			!FreecamSystem.IsActive &&
			player.active &&
			!player.dead &&
			!player.ghost &&
			!Main.playerInventory &&
			!Main.drawingPlayerChat &&
			!Main.editSign &&
			!Main.editChest &&
			!Main.mapFullscreen &&
			!Main.ingameOptionsWindow &&
			!Main.inFancyUI &&
			Main.InGameUI.CurrentState is null &&
			!(Main.CreativeMenu.Enabled && !Main.CreativeMenu.Blocked) &&
			!PlayerInput.WritingText;
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

	private static bool ShouldOpenNpcConversation(out NPC? npc)
	{
		npc = Main.LocalPlayer.TalkNPC;
		return npc is { active: true } &&
			!Main.playerInventory &&
			!Main.drawingPlayerChat &&
			!Main.editSign &&
			!Main.editChest &&
			!Main.mapFullscreen &&
			!Main.ingameOptionsWindow &&
			!Main.inFancyUI &&
			Main.InGameUI.CurrentState is null;
	}

	private static bool ShouldOpenSign(out int signIndex)
	{
		signIndex = Main.LocalPlayer.sign;
		return signIndex >= 0 &&
			signIndex < Main.sign.Length &&
			Main.sign[signIndex] is not null &&
			!Main.playerInventory &&
			!Main.drawingPlayerChat &&
			!Main.editSign &&
			!Main.editChest &&
			!Main.mapFullscreen &&
			!Main.ingameOptionsWindow &&
			!Main.inFancyUI &&
			Main.InGameUI.CurrentState is null;
	}
}
