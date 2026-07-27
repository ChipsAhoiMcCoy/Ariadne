#nullable enable

using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.Audio;
using Terraria.ID;
using Ariadne.Accessibility;
using Ariadne.Menus;

namespace Ariadne.Ingame.Waypoints;

internal sealed class WaypointMenuState : AccessibleMenuState
{
	private readonly WaypointSession _session;
	private bool _actionsPaneActive;
	private Waypoint? _actionWaypoint;
	private int _listSelection;
	private int _lastSelectedIndex = -1;
	private bool _deleteArmed;
	private string _lastResult = string.Empty;

	internal WaypointMenuState(AccessibleMenuController controller, WaypointSession session)
		: base(controller)
	{
		_session = session;
	}

	protected override string Title => _actionsPaneActive && _actionWaypoint is not null
		? $"Waypoint actions for {_actionWaypoint.Name}"
		: $"Waypoints, {DescribeCount(_session.Store.Waypoints.Count)}";

	protected override int? HierarchyLevel => _actionsPaneActive ? 1 : 0;

	protected override bool RightArrowOpensSubmenu => false;

	protected override string AdditionalControlHint => "    Tab: actions";

	protected override string AdditionalNavigationInstructions =>
		" Add waypoint here is always the first option. Enter on a saved waypoint travels to it, and Tab opens actions for it.";

	protected override void BuildEntries(List<AccessibleMenuEntry> entries)
	{
		if (_actionsPaneActive && _actionWaypoint is not null)
		{
			BuildActionEntries(entries, _actionWaypoint);
			return;
		}

		entries.Add(new AccessibleMenuEntry(
			() => "Add waypoint here",
			AddWaypointAtPlayer,
			description: () => "Save the player's current position as a new waypoint and name it.",
			enabled: CanAddWaypoint,
			role: "button"));

		foreach (Waypoint waypoint in _session.Store.Waypoints)
		{
			Waypoint captured = waypoint;
			entries.Add(new AccessibleMenuEntry(
				() => captured.Name,
				() => Travel(captured),
				description: () => "Press Enter to travel here. Press Tab for waypoint actions.",
				role: "action",
				adjustmentAnnouncement: () => _lastResult));
		}
	}

	// Travel and delete report their own outcome, so activation must not overwrite it with the
	// refreshed selection.
	protected override bool ActivationAdjustsValue(AccessibleMenuEntry entry) =>
		entry.Role == "action" || entry.Role == "destructive";

	protected override bool PlaysActivationTick(AccessibleMenuEntry entry) => false;

	protected override bool HandleAdditionalInput(KeyboardState keyboard, GameTime gameTime)
	{
		if (SelectedIndex != _lastSelectedIndex)
		{
			_lastSelectedIndex = SelectedIndex;
			_deleteArmed = false;
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
		_session.Close();
	}

	protected override void AddContextHelpTopics(List<AccessibleHelpTopic> topics)
	{
		topics.Add(new(
			"Tab",
			_actionsPaneActive
				? "Return to the waypoint list and the waypoint that opened these actions."
				: "Open actions for the focused waypoint: travel, rename, delete, and a coordinate readout."));
		topics.Add(new(
			"Add waypoint here",
			"Saves the player's current tile. A suggested name built from the biome and position is filled in for you, so Enter alone accepts it."));
		topics.Add(new(
			"Delete",
			"Press Enter once to arm the deletion and hear a confirmation, then Enter again to remove the waypoint. Moving to another option cancels it."));
		topics.Add(new(
			"Storage",
			"Waypoints belong to this world and are shared by every character. They are saved outside the world file, so they work on servers that do not have Ariadne installed."));
	}

	private void OpenActionsPane()
	{
		if (SelectedIndex == 0 || _session.Store.Waypoints.Count == 0)
		{
			Announce("Focus a saved waypoint to open its actions.");
			return;
		}

		_listSelection = SelectedIndex;
		_actionWaypoint = _session.Store.Waypoints[SelectedIndex - 1];
		_actionsPaneActive = true;
		_deleteArmed = false;
		_lastResult = string.Empty;
		RebuildEntries();
		SetSelectionWithoutAnnouncement(0);
		_lastSelectedIndex = 0;
		SoundEngine.PlaySound(SoundID.MenuOpen);
		Announce($"Actions pane for {_actionWaypoint.Name}. {DescribeSelection()} Tab returns to the waypoint list.");
	}

	private void CloseActionsPane()
	{
		_actionsPaneActive = false;
		_actionWaypoint = null;
		_deleteArmed = false;
		RebuildEntries();
		SetSelectionWithoutAnnouncement(_listSelection);
		_lastSelectedIndex = SelectedIndex;
		SoundEngine.PlaySound(SoundID.MenuClose);
		Announce($"Waypoint list. {DescribeSelection()}");
	}

	private void BuildActionEntries(List<AccessibleMenuEntry> entries, Waypoint waypoint)
	{
		entries.Add(new AccessibleMenuEntry(
			() => "Travel here",
			() => Travel(waypoint),
			description: () => "Warp to this waypoint, or to the nearest safe footing if the terrain has changed.",
			role: "action",
			adjustmentAnnouncement: () => _lastResult));
		entries.Add(new AccessibleMenuEntry(
			() => "Rename",
			() => BeginRename(waypoint),
			description: () => $"Change the name of {waypoint.Name}.",
			role: "button"));
		entries.Add(new AccessibleMenuEntry(
			() => _deleteArmed ? "Delete, press Enter again to confirm" : "Delete",
			() => Delete(waypoint),
			description: () => _deleteArmed
				? $"Press Enter again to remove {waypoint.Name}. Move to another option to cancel."
				: $"Remove {waypoint.Name}. You will be asked to confirm.",
			role: "destructive",
			adjustmentAnnouncement: () => _lastResult));
		entries.Add(new AccessibleMenuEntry(
			() => "Coordinates",
			description: () => DescribeLocation(waypoint),
			role: "information"));
	}

	private static bool CanAddWaypoint()
	{
		Player player = Main.LocalPlayer;
		return !Main.gameMenu && player.active && !player.dead;
	}

	private void AddWaypointAtPlayer()
	{
		if (!CanAddWaypoint())
		{
			Announce("A waypoint cannot be saved right now.");
			return;
		}

		Player player = Main.LocalPlayer;
		Point tile = player.Bottom.ToTileCoordinates();
		string suggestion = SuggestName(player, tile);
		Controller.Navigate(new AccessibleTextInputState(
			Controller,
			"Name this waypoint",
			suggestion,
			Waypoint.MaximumNameLength,
			name =>
			{
				Waypoint added = _session.Store.Add(
					string.IsNullOrWhiteSpace(name) ? suggestion : name,
					tile.X,
					tile.Y);
				Controller.Back();
				Announce($"Saved waypoint {added.Name}. {DescribeLocation(added)}");
			}));
	}

	private void BeginRename(Waypoint waypoint)
	{
		Controller.Navigate(new AccessibleTextInputState(
			Controller,
			$"Rename {waypoint.Name}",
			waypoint.Name,
			Waypoint.MaximumNameLength,
			name =>
			{
				string previousName = waypoint.Name;
				_session.Store.Rename(waypoint, name);
				Controller.Back();
				Announce(waypoint.Name == previousName
					? $"Kept the name {waypoint.Name}."
					: $"Renamed {previousName} to {waypoint.Name}.");
			}));
	}

	private void Delete(Waypoint waypoint)
	{
		if (!_deleteArmed)
		{
			_deleteArmed = true;
			_lastResult = $"Delete {waypoint.Name}? Press Enter again to confirm, or move to another option to cancel.";
			return;
		}

		string name = waypoint.Name;
		_session.Store.Remove(waypoint);
		_deleteArmed = false;
		SoundEngine.PlaySound(SoundID.MenuClose);
		_lastResult = $"Deleted {name}.";
		_actionsPaneActive = false;
		_actionWaypoint = null;
		RebuildEntries();
		SetSelectionWithoutAnnouncement(_listSelection);
		_lastSelectedIndex = SelectedIndex;
	}

	private void Travel(Waypoint waypoint)
	{
		WaypointTravelResult result = _session.Travel(waypoint);
		_lastResult = result.Message;
	}

	private static string SuggestName(Player player, Point tile)
	{
		BiomeStatusSnapshot biome = BiomeStatusFormatter.Capture(player, includeElevation: true);
		return $"{biome.Description} at {tile.X}, {tile.Y}";
	}

	private static string DescribeLocation(Waypoint waypoint)
	{
		Vector2 world = new(waypoint.TileX * 16f + 8f, waypoint.TileY * 16f);
		return $"{WorldPositionFormatter.DescribeCoordinates(world)} {WorldPositionFormatter.DescribeRelativePosition(world)}.";
	}

	private static string DescribeCount(int count)
	{
		return count == 1 ? "1 saved" : $"{count} saved";
	}
}
