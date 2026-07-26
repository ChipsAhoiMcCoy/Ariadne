#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Terraria;
using Terraria.GameContent.UI.States;
using Terraria.IO;
using Terraria.Localization;
using Terraria.ModLoader;
using Terraria.Social;

namespace Ariadne.Menus;

internal sealed class AccessibleWorldSelectMenuState : AccessibleMenuState
{
	private readonly MultiplayerIntent _intent;
	private readonly Dictionary<WorldFileData, AccessibleWorldActionsMenuState> _actionMenus = [];
	private readonly Dictionary<string, int> _actionIndices = new(StringComparer.OrdinalIgnoreCase);
	private List<WorldFileData> _worlds = [];

	internal AccessibleWorldSelectMenuState(AccessibleMenuController controller, MultiplayerIntent intent)
		: base(controller)
	{
		_intent = intent;
	}

	protected override string Title => Language.GetTextValue("UI.SelectWorld");

	public override void OnActivate()
	{
		Main.LoadWorlds();
		_worlds = [.. Main.WorldList
			.OrderByDescending(CanPlay)
			.ThenByDescending(world => world.IsFavorite)
			.ThenBy(world => world.Name, StringComparer.CurrentCultureIgnoreCase)
			.ThenBy(world => world.GetFileName(), StringComparer.OrdinalIgnoreCase)];
		_actionMenus.Clear();
		base.OnActivate();
	}

	protected override void BuildEntries(List<AccessibleMenuEntry> entries)
	{
		entries.Add(new(
			() => Language.GetTextValue("UI.New"),
			() => Controller.Navigate(new AccessibleWorldCreationMenuState(Controller))));

		foreach (WorldFileData world in _worlds)
		{
			WorldFileData capturedWorld = world;
			AccessibleWorldActionsMenuState actionMenu = GetActionMenu(capturedWorld);
			List<AccessibleMenuEntry> actions = actionMenu.GetActionEntries();
			string fileKey = capturedWorld.GetFileName();
			_actionIndices[fileKey] = ((GetActionIndex(fileKey) % actions.Count) + actions.Count) % actions.Count;

			AccessibleMenuEntry CurrentAction()
			{
				int index = Math.Clamp(GetActionIndex(fileKey), 0, actions.Count - 1);
				return actions[index];
			}

			entries.Add(new(
				() => ActionLabel(capturedWorld.Name, CurrentAction()),
				() => ActivateFileAction(CurrentAction()),
				previousValue: () => CycleAction(fileKey, actions.Count, -1),
				nextValue: () => CycleAction(fileKey, actions.Count, 1),
				description: () => ActionDescription(DescribeWorld(capturedWorld), CurrentAction(), GetActionIndex(fileKey), actions.Count),
				role: "world with actions",
				adjustmentAnnouncement: () => ActionAdjustmentAnnouncement(CurrentAction())));
		}
	}

	private AccessibleWorldActionsMenuState GetActionMenu(WorldFileData world)
	{
		if (!_actionMenus.TryGetValue(world, out AccessibleWorldActionsMenuState? menu))
		{
			menu = new AccessibleWorldActionsMenuState(Controller, world, SelectWorld, CanPlay);
			_actionMenus[world] = menu;
		}
		return menu;
	}

	private int GetActionIndex(string fileKey) => _actionIndices.GetValueOrDefault(fileKey);

	private void CycleAction(string fileKey, int count, int offset)
	{
		_actionIndices[fileKey] = (GetActionIndex(fileKey) + offset + count) % count;
	}

	private void ActivateFileAction(AccessibleMenuEntry action)
	{
		if (!action.IsEnabled)
		{
			Announce($"{action.Label()}, unavailable. {action.Description?.Invoke()}");
			return;
		}
		(action.Activate ?? action.NextValue ?? action.PreviousValue)?.Invoke();
	}

	private static string ActionLabel(string fileName, AccessibleMenuEntry action)
	{
		string unavailable = action.IsEnabled ? string.Empty : ", unavailable";
		return $"{fileName}. Action: {action.Label()}{unavailable}";
	}

	private static string ActionAdjustmentAnnouncement(AccessibleMenuEntry action)
	{
		string unavailable = action.IsEnabled ? string.Empty : ", unavailable";
		return $"{action.Label()}{unavailable}";
	}

	private static string ActionDescription(string fileDescription, AccessibleMenuEntry action, int index, int count)
	{
		string unavailable = action.IsEnabled ? string.Empty : " This action is unavailable.";
		string description = action.Description?.Invoke() ?? string.Empty;
		return $"{fileDescription} Current action {index + 1} of {count}: {action.Label()}.{unavailable} {description} Left and Right change the action; Enter activates it.";
	}

	private void SelectWorld(WorldFileData data)
	{
		if (!CanPlay(data))
		{
			Announce("This character and world are not compatible, or a mod rejected this combination.");
			return;
		}

		data.SetAsActive();
		Main.clrInput();
		Main.GetInputText(string.Empty);
		if (_intent == MultiplayerIntent.HostAndPlay)
		{
			Controller.Navigate(new AccessibleHostConfigurationMenuState(Controller));
			return;
		}

		Controller.ClearHistory();
		Main.MenuUI.SetState(null);
		Main.menuMode = 10;
		WorldGen.playWorld();
	}

	private static bool CanPlay(WorldFileData world)
	{
		if (!world.IsValid || Main.ActivePlayerFileData?.Player is null)
		{
			return false;
		}

		bool journeyPlayer = Main.ActivePlayerFileData.Player.difficulty == 3;
		bool journeyWorld = world.GameMode == 3;
		return Main.RegisteredGameModes.ContainsKey(world.GameMode) &&
			journeyPlayer == journeyWorld &&
			SystemLoader.CanWorldBePlayed(Main.ActivePlayerFileData, world, out _);
	}

	private static string DescribeWorld(WorldFileData data)
	{
		string difficulty = data.GameMode switch
		{
			0 => Language.GetTextValue("UI.Normal"),
			1 => Language.GetTextValue("UI.Expert"),
			2 => Language.GetTextValue("UI.Master"),
			3 => Language.GetTextValue("UI.Creative"),
			_ => $"Unknown difficulty {data.GameMode}",
		};
		string evil = data.HasCrimson ? "Crimson" : "Corruption";
		string hardmode = data.IsHardMode ? " Hardmode." : string.Empty;
		string favorite = data.IsFavorite ? " Favorite." : string.Empty;
		string cloud = data.IsCloudSave ? " Cloud save." : " Local save.";
		string warnings = AccessibleWorldFileDiagnostics.WarningSummary(data);
		return $"{difficulty}. {data.WorldSizeName}. {evil}.{hardmode}{favorite}{cloud}{warnings}";
	}
}

internal sealed class AccessibleWorldCreationMenuState : AccessibleMenuState
{
	private string _name = string.Empty;
	private string _seed = string.Empty;
	private int _size = 1;
	private int _difficulty;
	private int _evil = -1;

	internal AccessibleWorldCreationMenuState(AccessibleMenuController controller)
		: base(controller)
	{
	}

	protected override string Title => "Create World";

	public override void OnActivate()
	{
		if (string.IsNullOrWhiteSpace(_name))
		{
			_name = $"{Lang.gen[57].Value} {Main.WorldList.Count + 1}";
		}
		base.OnActivate();
	}

	protected override void BuildEntries(List<AccessibleMenuEntry> entries)
	{
		entries.Add(new(() => $"Name: {_name}", EditName, role: "edit field"));
		entries.Add(new(() => $"Seed: {(string.IsNullOrEmpty(_seed) ? "random" : _seed)}", EditSeed, role: "edit field"));
		entries.Add(new(
			() => $"Size: {SizeName(_size)}",
			NextSize,
			previousValue: PreviousSize,
			nextValue: NextSize,
			role: "choice"));
		entries.Add(new(
			() => $"Difficulty: {WorldDifficultyName(_difficulty)}",
			NextDifficulty,
			previousValue: PreviousDifficulty,
			nextValue: NextDifficulty,
			role: "choice",
			adjustmentAnnouncement: () => WorldDifficultyName(_difficulty)));
		entries.Add(new(
			() => $"World evil: {EvilName(_evil)}",
			NextEvil,
			previousValue: PreviousEvil,
			nextValue: NextEvil,
			role: "choice"));
		entries.Add(new(() => Language.GetTextValue("UI.Create"), CreateWorld));
	}

	private void EditName()
	{
		Controller.Navigate(new AccessibleTextInputState(
			Controller, Lang.menu[48].Value, _name, 27,
			value => { _name = value; Controller.Back(); }));
	}

	private void EditSeed()
	{
		Controller.Navigate(new AccessibleTextInputState(
			Controller, Language.GetTextValue("UI.EnterSeed"), _seed, 40,
			value => { _seed = value; Controller.Back(); }));
	}

	private void NextSize() => _size = (_size + 1) % 3;

	private void PreviousSize() => _size = (_size + 2) % 3;

	private void NextDifficulty() => _difficulty = (_difficulty + 1) % 4;

	private void PreviousDifficulty() => _difficulty = (_difficulty + 3) % 4;

	private void NextEvil() => _evil = _evil switch { -1 => 0, 0 => 1, _ => -1 };

	private void PreviousEvil() => _evil = _evil switch { -1 => 1, 1 => 0, _ => -1 };

	private void CreateWorld()
	{
		string name = _name.Trim();
		if (string.IsNullOrWhiteSpace(name))
		{
			Announce("Enter a world name before creating the world.");
			return;
		}
		if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
		{
			Announce("That world name contains a character which cannot be used in a file name.");
			return;
		}
		if (Main.WorldList.Any(world => string.Equals(world.Name, name, StringComparison.CurrentCultureIgnoreCase)))
		{
			Announce("A world with that name already exists.");
			return;
		}

		try
		{
			(Main.maxTilesX, Main.maxTilesY) = _size switch
			{
				0 => (4200, 1200),
				1 => (6400, 1800),
				_ => (8400, 2400),
			};
			WorldGen.setWorldSize();
			Main.GameMode = _difficulty;
			WorldGen.WorldGenParam_Evil = _evil;
			UIWorldCreation.ProcessSpecialWorldSeeds(_seed);
			Main.ActiveWorldFileData = WorldFile.CreateMetadata(
				Main.worldName = name,
				SocialAPI.Cloud is not null && SocialAPI.Cloud.EnabledByDefault,
				Main.GameMode);
			if (string.IsNullOrWhiteSpace(_seed))
			{
				Main.ActiveWorldFileData.SetSeedToRandom();
			}
			else
			{
				Main.ActiveWorldFileData.SetSeed(_seed);
			}

			Controller.ClearHistory();
			Main.MenuUI.SetState(null);
			Main.menuMode = 10;
			WorldGen.CreateNewWorld();
		}
		catch (Exception exception)
		{
			Announce("The world could not be created. See the tModLoader client log.");
			ModContent.GetInstance<AriadneMod>().Logger.Error("Custom world creation failed.", exception);
		}
	}

	private static string SizeName(int size) => size switch
	{
		0 => Language.GetTextValue("UI.WorldSizeSmall"),
		1 => Language.GetTextValue("UI.WorldSizeMedium"),
		_ => Language.GetTextValue("UI.WorldSizeLarge"),
	};

	private static string WorldDifficultyName(int difficulty) => difficulty switch
	{
		0 => Language.GetTextValue("UI.Normal"),
		1 => Language.GetTextValue("UI.Expert"),
		2 => Language.GetTextValue("UI.Master"),
		_ => Language.GetTextValue("UI.Creative"),
	};

	private static string EvilName(int evil) => evil switch
	{
		0 => "Corruption",
		1 => "Crimson",
		_ => "Random",
	};
}
