#nullable enable

using Microsoft.Xna.Framework.Input;
using Terraria;
using Ariadne.Menus;

namespace Ariadne.Ingame.Scanner;

internal sealed class ScannerSession
{
	private readonly AccessibleMenuController _menuController;
	private readonly AccessibleInventoryController _inventoryController;
	private readonly ScannerTeleportCoordinator _teleportCoordinator = new();
	private bool _isOpen;

	internal ScannerSession(AccessibleMenuController menuController, AccessibleInventoryController inventoryController)
	{
		_menuController = menuController;
		_inventoryController = inventoryController;
	}

	internal bool IsOpen => _isOpen;

	internal void Open()
	{
		ScannerSnapshot snapshot = ScannerSnapshotBuilder.Capture();
		if (snapshot.TargetCount == 0)
		{
			AriadneMod.ScreenReader.Output("No lit targets found on screen.");
			return;
		}

		_isOpen = true;
		_menuController.ShowRoot(new ScannerRootMenuState(_menuController, this, snapshot));
	}

	internal void OpenCategory(ScannerCategory category)
	{
		_menuController.Navigate(new ScannerCategoryMenuState(_menuController, this, category));
	}

	internal void Close()
	{
		_inventoryController.SuppressInventoryToggleUntilRelease();
		_isOpen = false;
		_menuController.Close();
		Main.playerInventory = false;
	}

	internal void ConsumeInput()
	{
		if (_isOpen)
		{
			AccessibleInputSuppression.ConsumeMenuNavigationAndLetterTriggers(Keyboard.GetState());
			AccessibleInputSuppression.ConsumeMovementTriggers();
		}
	}

	internal ScannerActivationResult Activate(ScannerTarget target)
	{
		ScannerActivationResult result = _teleportCoordinator.Activate(
			target,
			Close,
			_inventoryController.RequestSemanticFocusPath);
		if (result.Success)
		{
			AriadneMod.ScreenReader.Output(result.Message);
		}
		return result;
	}

	internal void SynchronizeState()
	{
		if (_isOpen && !_menuController.IsActive)
		{
			_isOpen = false;
		}
	}

	internal void UpdateVerification() => _teleportCoordinator.UpdateVerification();

	internal void Reset()
	{
		_isOpen = false;
		_teleportCoordinator.Reset();
	}

}
