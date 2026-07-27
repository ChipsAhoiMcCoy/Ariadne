#nullable enable

using Microsoft.Xna.Framework.Input;
using Terraria;
using Ariadne.Menus;

namespace Ariadne.Ingame.Waypoints;

internal sealed class WaypointSession
{
	private readonly AccessibleMenuController _menuController;
	private readonly AccessibleInventoryController _inventoryController;
	private readonly WaypointStore _store;
	private readonly WaypointTravelCoordinator _travelCoordinator = new();
	private bool _isOpen;

	internal WaypointSession(
		AccessibleMenuController menuController,
		AccessibleInventoryController inventoryController,
		WaypointStore store)
	{
		_menuController = menuController;
		_inventoryController = inventoryController;
		_store = store;
	}

	internal bool IsOpen => _isOpen;

	internal WaypointStore Store => _store;

	internal void Open()
	{
		_isOpen = true;
		_menuController.ShowRoot(new WaypointMenuState(_menuController, this));
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

	/// <summary>
	/// The caller announces the result so it lands after the menu has finished rebuilding,
	/// rather than being interrupted by the refreshed selection.
	/// </summary>
	internal WaypointTravelResult Travel(Waypoint waypoint)
	{
		return _travelCoordinator.Travel(waypoint, Close);
	}

	internal void SynchronizeState()
	{
		if (_isOpen && !_menuController.IsActive)
		{
			_isOpen = false;
		}
	}

	internal void UpdateVerification() => _travelCoordinator.UpdateVerification();

	internal void Reset()
	{
		_isOpen = false;
		_travelCoordinator.Reset();
	}
}
