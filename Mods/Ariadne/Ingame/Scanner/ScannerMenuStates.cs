#nullable enable

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Ariadne.Menus;

namespace Ariadne.Ingame.Scanner;

internal sealed class ScannerRootMenuState : AccessibleMenuState
{
	private readonly ScannerSession _session;
	private readonly ScannerSnapshot _snapshot;

	internal ScannerRootMenuState(AccessibleMenuController controller, ScannerSession session, ScannerSnapshot snapshot)
		: base(controller)
	{
		_session = session;
		_snapshot = snapshot;
	}

	protected override string Title => "Visible surroundings scanner";
	protected override int? HierarchyLevel => 0;
	protected override string AdditionalNavigationInstructions =>
		" The snapshot remains fixed until you close it and scan again.";

	protected override void BuildEntries(List<AccessibleMenuEntry> entries)
	{
		foreach (ScannerCategory category in _snapshot.Categories)
		{
			ScannerCategory captured = category;
			entries.Add(new AccessibleMenuEntry(
				() => $"{captured.Name}, {captured.Targets.Length} {ResultWord(captured.Targets.Length)}",
				() => _session.OpenCategory(captured),
				description: () => $"Open {captured.Name}. Nearest result: {DescribeTarget(captured.Targets[0])}",
				role: "submenu"));
		}
	}

	protected override bool HandleAdditionalInput(KeyboardState keyboard, GameTime gameTime)
	{
		if (IsControlDown(keyboard) && Pressed(keyboard, Keys.R))
		{
			ScannerCategory category = _snapshot.Categories[SelectedIndex];
			Announce($"{category.Name}, {category.Targets.Length} {ResultWord(category.Targets.Length)}. Nearest result: {DescribeTarget(category.Targets[0])}");
			return true;
		}
		return false;
	}

	protected override void GoBack() => _session.Close();

	protected override void OpenContextHelp() => Announce(ScannerHelpText());

	internal static string DescribeTarget(ScannerTarget target)
	{
		return $"{DescribeTargetIdentity(target)}, {DescribeTargetContext(target)}";
	}

	internal static string DescribeTargetIdentity(ScannerTarget target)
	{
		return target.Kind switch
		{
			ScannerTargetKind.Liquid => $"{target.Name} pool",
			ScannerTargetKind.DroppedItem => $"{target.Stack} {target.Name}",
			_ => target.Name,
		};
	}

	internal static string DescribeTargetDetails(ScannerTarget target)
	{
		string kind = target.Kind switch
		{
			ScannerTargetKind.Resource => "Ore or valuable",
			ScannerTargetKind.Liquid => "Liquid pool",
			ScannerTargetKind.Npc => "NPC",
			ScannerTargetKind.Enemy => target.IsBoss ? "Boss enemy" : "Enemy",
			ScannerTargetKind.PassiveCreature => "Passive creature",
			ScannerTargetKind.DroppedItem => "Dropped item",
			ScannerTargetKind.Container => "Container",
			ScannerTargetKind.Tree => "Tree or large plant",
			_ => "Placed object",
		};
		string action = InteractionAction(target);
		return $"{kind}. {DescribeTarget(target)}. {WorldPositionFormatter.DescribeCoordinates(target.WorldPosition)}{action}";
	}

	internal static string DescribeTargetSelectionDetails(ScannerTarget target)
	{
		return $"{DescribeTargetContext(target)}. {WorldPositionFormatter.DescribeCoordinates(target.WorldPosition)}{InteractionAction(target)}";
	}

	internal static string ScannerHelpText() =>
		"Visible surroundings scanner help. The root contains only nonempty categories and each category contains the lit targets captured when the scanner opened. " +
		"Up and Down move, Home and End jump to the list edges, Page Up and Page Down move by a page, and letter keys jump by name. " +
		"Right Arrow or Enter opens a category. Left Arrow or Escape returns or closes. Enter on a target searches for a safe landing, teleports, and performs its supported native interaction. " +
		"Control R rereads focused details and F1 repeats this help. Scanner navigation keys are consumed while this screen is open and do not also control the player. " +
		"Close and press Open Scanner again to refresh the fixed snapshot.";

	internal static string ResultWord(int count) => count == 1 ? "result" : "results";
	private static string VisibleTiles(int count) => count == 1 ? "1 visible tile" : $"{count} visible tiles";
	private static string DescribeTargetContext(ScannerTarget target)
	{
		string position = WorldPositionFormatter.DescribeRelativePosition(target.WorldPosition);
		return target.Kind switch
		{
			ScannerTargetKind.Resource or ScannerTargetKind.Liquid =>
				$"{position}, {VisibleTiles(target.VisibleTileCount)}",
			ScannerTargetKind.Npc or ScannerTargetKind.Enemy or ScannerTargetKind.PassiveCreature =>
				$"{position}, {HealthDescription(target)}{(target.IsBoss ? ", boss" : string.Empty)}",
			_ => position,
		};
	}
	private static string InteractionAction(ScannerTarget target) => target.Interaction switch
	{
		ScannerInteractionKind.TalkToNpc => " Enter moves to a safe nearby position and opens normal conversation.",
		ScannerInteractionKind.PickupItem => " Enter moves within ordinary pickup range.",
		ScannerInteractionKind.RightClickTile => " Enter moves to a safe nearby position and performs one normal right-click interaction.",
		_ => " Enter moves to a safe nearby position.",
	};
	private static string HealthDescription(ScannerTarget target) => target.MaxHealth > 0
		? $"health {Math.Max(0, target.Health)} of {target.MaxHealth}"
		: "health unavailable";
	private static bool IsControlDown(KeyboardState keyboard) =>
		keyboard.IsKeyDown(Keys.LeftControl) || keyboard.IsKeyDown(Keys.RightControl);
}

internal sealed class ScannerCategoryMenuState : AccessibleMenuState
{
	private readonly ScannerSession _session;
	private readonly ScannerCategory _category;
	private string _activationFailure = string.Empty;
	private string _failedTargetId = string.Empty;

	internal ScannerCategoryMenuState(AccessibleMenuController controller, ScannerSession session, ScannerCategory category)
		: base(controller)
	{
		_session = session;
		_category = category;
	}

	protected override string Title => $"{_category.Name}: {_category.Targets.Length} {ScannerRootMenuState.ResultWord(_category.Targets.Length)}";
	protected override int? HierarchyLevel => 1;
	protected override bool RightArrowOpensSubmenu => false;
	protected override string AdditionalNavigationInstructions =>
		" Close and scan again to refresh this fixed snapshot.";

	protected override void BuildEntries(List<AccessibleMenuEntry> entries)
	{
		foreach (ScannerTarget target in _category.Targets)
		{
			ScannerTarget captured = target;
			entries.Add(new AccessibleMenuEntry(
				() => ScannerRootMenuState.DescribeTargetIdentity(captured),
				() => Activate(captured),
				description: () => DescribeWithFailure(captured),
				role: TargetRole(captured)));
		}
	}

	protected override bool HandleAdditionalInput(KeyboardState keyboard, GameTime gameTime)
	{
		if ((keyboard.IsKeyDown(Keys.LeftControl) || keyboard.IsKeyDown(Keys.RightControl)) && Pressed(keyboard, Keys.R))
		{
			_activationFailure = string.Empty;
			Announce(ScannerRootMenuState.DescribeTargetDetails(_category.Targets[SelectedIndex]));
			return true;
		}
		return false;
	}

	protected override void OpenContextHelp() => Announce(ScannerRootMenuState.ScannerHelpText());

	private void Activate(ScannerTarget target)
	{
		_activationFailure = string.Empty;
		_failedTargetId = string.Empty;
		ScannerActivationResult result = _session.Activate(target);
		if (!result.Success)
		{
			_activationFailure = result.Message;
			_failedTargetId = target.Id;
		}
	}

	private string DescribeWithFailure(ScannerTarget target)
	{
		string failure = _failedTargetId == target.Id && !string.IsNullOrWhiteSpace(_activationFailure)
			? $"{_activationFailure} "
			: string.Empty;
		return failure + ScannerRootMenuState.DescribeTargetSelectionDetails(target);
	}

	private static string TargetRole(ScannerTarget target) => target.Kind switch
	{
		ScannerTargetKind.Npc => "NPC",
		ScannerTargetKind.Enemy => target.IsBoss ? "boss" : "enemy",
		ScannerTargetKind.PassiveCreature => "passive creature",
		ScannerTargetKind.DroppedItem => "dropped item",
		ScannerTargetKind.Container => "container",
		ScannerTargetKind.PlacedObject => "placed object",
		ScannerTargetKind.Tree => "tree",
		ScannerTargetKind.Liquid => "liquid pool",
		_ => "resource",
	};
}
