#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.GameContent.UI.States;
using Terraria.ID;
using Terraria.IO;
using Terraria.Localization;
using Terraria.ModLoader;

namespace Ariadne.Menus;

internal enum MultiplayerIntent
{
	SinglePlayer,
	JoinByIp,
	HostAndPlay,
}

internal sealed class AccessiblePlayerSelectMenuState : AccessibleMenuState
{
	private readonly MultiplayerIntent _intent;
	private readonly Dictionary<PlayerFileData, AccessiblePlayerActionsMenuState> _actionMenus = [];
	private readonly Dictionary<string, int> _actionIndices = new(StringComparer.OrdinalIgnoreCase);
	private List<PlayerFileData> _players = [];

	internal AccessiblePlayerSelectMenuState(AccessibleMenuController controller, MultiplayerIntent intent)
		: base(controller)
	{
		_intent = intent;
	}

	protected override string Title => Language.GetTextValue("UI.SelectPlayer");

	public override void OnActivate()
	{
		Main.LoadPlayers();
		Main.ActivePlayerFileData = new PlayerFileData();
		_players = [.. Main.PlayerList
			.OrderByDescending(player => player.IsFavorite)
			.ThenBy(player => player.Name, StringComparer.CurrentCultureIgnoreCase)
			.ThenBy(player => player.GetFileName(), StringComparer.OrdinalIgnoreCase)];
		_actionMenus.Clear();
		base.OnActivate();
	}

	protected override void BuildEntries(List<AccessibleMenuEntry> entries)
	{
		entries.Add(new(
			() => Language.GetTextValue("UI.New"),
			() => Controller.Navigate(new AccessibleCharacterCreationMenuState(Controller))));

		foreach (PlayerFileData player in _players)
		{
			PlayerFileData capturedPlayer = player;
			AccessiblePlayerActionsMenuState actionMenu = GetActionMenu(capturedPlayer);
			List<AccessibleMenuEntry> actions = actionMenu.GetActionEntries();
			string fileKey = capturedPlayer.GetFileName();
			_actionIndices[fileKey] = ((GetActionIndex(fileKey) % actions.Count) + actions.Count) % actions.Count;

			AccessibleMenuEntry CurrentAction()
			{
				int index = Math.Clamp(GetActionIndex(fileKey), 0, actions.Count - 1);
				return actions[index];
			}

			entries.Add(new(
				() => ActionLabel(capturedPlayer.Name, CurrentAction()),
				() => ActivateFileAction(CurrentAction()),
				previousValue: () => CycleAction(fileKey, actions.Count, -1),
				nextValue: () => CycleAction(fileKey, actions.Count, 1),
				description: () => ActionDescription(DescribePlayer(capturedPlayer), CurrentAction(), GetActionIndex(fileKey), actions.Count),
				role: "character with actions",
				adjustmentAnnouncement: () => ActionAdjustmentAnnouncement(CurrentAction())));
		}
	}

	private AccessiblePlayerActionsMenuState GetActionMenu(PlayerFileData player)
	{
		if (!_actionMenus.TryGetValue(player, out AccessiblePlayerActionsMenuState? menu))
		{
			menu = new AccessiblePlayerActionsMenuState(Controller, player, SelectPlayer);
			_actionMenus[player] = menu;
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

	private void SelectPlayer(PlayerFileData data)
	{
		Main.ClearPendingPlayerSelectCallbacks();
		Main.menuMultiplayer = _intent != MultiplayerIntent.SinglePlayer;
		Main.menuServer = _intent == MultiplayerIntent.HostAndPlay;
		Main.SelectPlayer(data);

		switch (_intent)
		{
			case MultiplayerIntent.SinglePlayer:
			case MultiplayerIntent.HostAndPlay:
				Main.LoadWorlds();
				Controller.Navigate(new AccessibleWorldSelectMenuState(Controller, _intent));
				break;
			case MultiplayerIntent.JoinByIp:
				Controller.Navigate(new AccessibleServerAddressMenuState(Controller));
				break;
		}
	}

	private static string DescribePlayer(PlayerFileData data)
	{
		Player player = data.Player;
		string difficulty = player.difficulty switch
		{
			PlayerDifficultyID.SoftCore => Language.GetTextValue("UI.Softcore"),
			PlayerDifficultyID.MediumCore => Language.GetTextValue("UI.Mediumcore"),
			PlayerDifficultyID.Hardcore => Language.GetTextValue("UI.Hardcore"),
			PlayerDifficultyID.Creative => Language.GetTextValue("UI.Creative"),
			_ => $"Unknown difficulty {player.difficulty}",
		};
		string favorite = data.IsFavorite ? " Favorite." : string.Empty;
		string cloud = data.IsCloudSave ? " Cloud save." : " Local save.";
		string status = player.loadStatus == 0 ? string.Empty : $" Load error {player.loadStatus}.";
		return $"{difficulty}. {player.statLifeMax2} maximum life. {player.statManaMax2} maximum mana.{favorite}{cloud}{status}";
	}
}

internal sealed class AccessibleCharacterCreationMenuState : AccessibleMenuState
{
	private static readonly MethodInfo? SetupStartingInventory = typeof(UICharacterCreation).GetMethod(
		"SetupPlayerStatsAndInventoryBasedOnDifficulty",
		BindingFlags.NonPublic | BindingFlags.Instance);

	private string _name = string.Empty;
	private int _difficulty;
	private readonly Player _player = new();

	internal AccessibleCharacterCreationMenuState(AccessibleMenuController controller)
		: base(controller)
	{
	}

	protected override string Title => "Create Character";

	protected override void BuildEntries(List<AccessibleMenuEntry> entries)
	{
		entries.Add(new(
			() => $"Name: {(string.IsNullOrWhiteSpace(_name) ? "not set" : _name)}",
			EditName,
			role: "edit field"));
		entries.Add(new(
			() => $"Difficulty: {DifficultyName(_difficulty)}",
			CycleDifficulty,
			previousValue: PreviousDifficulty,
			nextValue: CycleDifficulty,
			description: () => DifficultyDescription(_difficulty),
			role: "choice",
			adjustmentAnnouncement: DifficultyAdjustmentAnnouncement));
		entries.Add(new(
			() => "Appearance",
			() => Controller.Navigate(new AccessibleCharacterAppearanceMenuState(Controller, _player)),
			description: () => "Choose hair, clothing style, and character colors."));
		entries.Add(new(
			() => Language.GetTextValue("UI.Create"),
			CreateCharacter));
	}

	private void EditName()
	{
		Controller.Navigate(new AccessibleTextInputState(
			Controller,
			Lang.menu[45].Value,
			_name,
			20,
			value =>
			{
				_name = value;
				Controller.Back();
			}));
	}

	private void CycleDifficulty()
	{
		_difficulty = (_difficulty + 1) % 4;
	}

	private void PreviousDifficulty()
	{
		_difficulty = (_difficulty + 3) % 4;
	}

	private void CreateCharacter()
	{
		string name = _name.Trim();
		if (string.IsNullOrWhiteSpace(name))
		{
			Announce("Enter a character name before creating the character.");
			return;
		}
		if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
		{
			Announce("That character name contains a character which cannot be used in a file name.");
			return;
		}
		if (Main.PlayerList.Any(player => string.Equals(player.Name, name, StringComparison.CurrentCultureIgnoreCase)))
		{
			Announce("A character with that name already exists.");
			return;
		}
		if (SetupStartingInventory is null)
		{
			Announce("Character creation is unavailable because this tModLoader version changed its initialization API.");
			return;
		}

		try
		{
			_player.name = name;
			UICharacterCreation initializer = new(_player);
			_player.difficulty = (byte)_difficulty;
			SetupStartingInventory.Invoke(initializer, null);
			PlayerFileData.CreateAndSave(_player);
			Main.LoadPlayers();
			Announce($"Created {name}.");
			Controller.Back();
		}
		catch (Exception exception)
		{
			Announce("The character could not be created. See the tModLoader client log.");
			ModContent.GetInstance<AriadneMod>().Logger.Error("Custom character creation failed.", exception);
		}
	}

	private static string DifficultyName(int difficulty)
	{
		return difficulty switch
		{
			PlayerDifficultyID.SoftCore => Language.GetTextValue("UI.Softcore"),
			PlayerDifficultyID.MediumCore => Language.GetTextValue("UI.Mediumcore"),
			PlayerDifficultyID.Hardcore => Language.GetTextValue("UI.Hardcore"),
			PlayerDifficultyID.Creative => Language.GetTextValue("UI.Creative"),
			_ => difficulty.ToString(),
		};
	}

	private static string DifficultyDescription(int difficulty)
	{
		return difficulty switch
		{
			PlayerDifficultyID.SoftCore => "Drops coins on death.",
			PlayerDifficultyID.MediumCore => "Drops items on death.",
			PlayerDifficultyID.Hardcore => "Death is permanent.",
			PlayerDifficultyID.Creative => "Journey character; can only enter Journey worlds.",
			_ => string.Empty,
		};
	}

	private string DifficultyAdjustmentAnnouncement()
	{
		return $"{DifficultyName(_difficulty)}. {DifficultyDescription(_difficulty)}";
	}
}

internal sealed class AccessibleCharacterAppearanceMenuState : AccessibleMenuState
{
	private static readonly HashSet<int> PlanteraHairstyles = [145, 162, 163, 164];
	private readonly Player _player;
	private HashSet<int> _availableHairstyles = [];
	private int _hairCandidate;
	private bool _hairInitialized;

	internal AccessibleCharacterAppearanceMenuState(AccessibleMenuController controller, Player player)
		: base(controller)
	{
		_player = player;
	}

	protected override string Title => "Character Appearance";

	public override void OnActivate()
	{
		Main.Hairstyles.UpdateUnlocks();
		_availableHairstyles = [.. Main.Hairstyles.AvailableHairstyles];
		if (!_hairInitialized)
		{
			_hairCandidate = Math.Clamp(_player.hair, 0, Math.Max(0, HairLoader.Count - 1));
			_hairInitialized = true;
		}
		base.OnActivate();
	}

	protected override void BuildEntries(List<AccessibleMenuEntry> entries)
	{
		entries.Add(new(
			() => HairLabel(_hairCandidate),
			SelectHairCandidate,
			previousValue: PreviousHair,
			nextValue: NextHair,
			description: () => HairDescription(_hairCandidate),
			role: "choice",
			adjustmentAnnouncement: HairAdjustmentAnnouncement));
		entries.Add(new(
			() => $"Clothing style {_player.skinVariant + 1} of 10",
			NextClothing,
			previousValue: PreviousClothing,
			nextValue: NextClothing,
			description: () => CharacterAppearanceDescriptions.GetClothingStyle(_player.skinVariant),
			role: "choice",
			adjustmentAnnouncement: ClothingAdjustmentAnnouncement));
		entries.Add(ColorEntry("Hair color", () => _player.hairColor, value => _player.hairColor = value));
		entries.Add(ColorEntry("Eye color", () => _player.eyeColor, value => _player.eyeColor = value));
		entries.Add(ColorEntry("Skin color", () => _player.skinColor, value => _player.skinColor = value));
		entries.Add(ColorEntry("Shirt color", () => _player.shirtColor, value => _player.shirtColor = value));
		entries.Add(ColorEntry("Undershirt color", () => _player.underShirtColor, value => _player.underShirtColor = value));
		entries.Add(ColorEntry("Pants color", () => _player.pantsColor, value => _player.pantsColor = value));
		entries.Add(ColorEntry("Shoe color", () => _player.shoeColor, value => _player.shoeColor = value));
		entries.Add(new(() => "Randomize appearance", Randomize));
	}

	private AccessibleMenuEntry ColorEntry(string name, Func<Color> get, Action<Color> set)
	{
		return new AccessibleMenuEntry(
			() => $"{name}: {ColorName(get())}",
			() => Controller.Navigate(new AccessibleColorEditorMenuState(Controller, name, get, set)),
			description: () =>
			{
				Color color = get();
				return $"Red {color.R}, green {color.G}, blue {color.B}.";
			});
	}

	private void NextClothing() => _player.skinVariant = (_player.skinVariant + 1) % 10;

	private void PreviousClothing() => _player.skinVariant = (_player.skinVariant + 9) % 10;

	private string ClothingAdjustmentAnnouncement()
	{
		return $"Clothing style {_player.skinVariant + 1} of 10. " +
			CharacterAppearanceDescriptions.GetClothingStyle(_player.skinVariant);
	}

	private void NextHair() => ChangeHairCandidate(1);

	private void PreviousHair() => ChangeHairCandidate(-1);

	private void ChangeHairCandidate(int offset)
	{
		if (HairLoader.Count == 0)
		{
			return;
		}
		_hairCandidate = (_hairCandidate + offset + HairLoader.Count) % HairLoader.Count;
		if (_availableHairstyles.Contains(_hairCandidate))
		{
			_player.hair = _hairCandidate;
		}
	}

	private void SelectHairCandidate()
	{
		if (_availableHairstyles.Contains(_hairCandidate))
		{
			_player.hair = _hairCandidate;
			return;
		}
		Announce($"{HairLabel(_hairCandidate)}. {HairDescription(_hairCandidate)}");
	}

	private void Randomize()
	{
		if (Main.Hairstyles.AvailableHairstyles.Count > 0)
		{
			int hairPosition = Main.rand.Next(Main.Hairstyles.AvailableHairstyles.Count);
			_player.hair = Main.Hairstyles.AvailableHairstyles[hairPosition];
			_hairCandidate = _player.hair;
		}
		_player.skinVariant = Main.rand.Next(10);
		_player.hairColor = RandomColor();
		_player.eyeColor = RandomColor();
		_player.skinColor = new Color(Main.rand.Next(80, 256), Main.rand.Next(55, 211), Main.rand.Next(35, 181));
		_player.shirtColor = RandomColor();
		_player.underShirtColor = RandomColor();
		_player.pantsColor = RandomColor();
		_player.shoeColor = RandomColor();
	}

	private static Color RandomColor() => new(Main.rand.Next(40, 256), Main.rand.Next(40, 256), Main.rand.Next(40, 256));

	private static string ColorName(Color color) => $"red {color.R}, green {color.G}, blue {color.B}";

	private string HairLabel(int hairId)
	{
		ModHair? modHair = HairLoader.GetHair(hairId);
		string position = $"Hairstyle {hairId + 1} of {HairLoader.Count}";
		string name = modHair is null
			? position
			: $"{position}: {modHair.PrettyPrintName()} from {modHair.Mod.DisplayName}";
		if (!_availableHairstyles.Contains(hairId))
		{
			return $"{name}, unavailable";
		}
		return hairId == _player.hair ? $"{name}, selected" : name;
	}

	private string HairAdjustmentAnnouncement()
	{
		ModHair? modHair = HairLoader.GetHair(_hairCandidate);
		string position = $"Hairstyle {_hairCandidate + 1} of {HairLoader.Count}";
		string value = modHair is null
			? position
			: $"{position}, {modHair.PrettyPrintName()} from {modHair.Mod.DisplayName}";
		return $"{value}. {HairDescription(_hairCandidate)}";
	}

	private string HairDescription(int hairId)
	{
		string? appearance = CharacterAppearanceDescriptions.GetHairstyle(hairId);
		string availability = HairAvailabilityDescription(hairId);
		return string.IsNullOrWhiteSpace(appearance)
			? availability
			: $"{appearance} {availability}";
	}

	private string HairAvailabilityDescription(int hairId)
	{
		if (_availableHairstyles.Contains(hairId))
		{
			return hairId == _player.hair
				? "Currently selected."
				: "Available during character creation.";
		}

		if (hairId is >= 123 and <= 132)
		{
			return "Unavailable during character creation. Defeat the Martian Madness event, then visit the Stylist to select this hairstyle.";
		}
		if (hairId == 133)
		{
			return "Unavailable during character creation. Defeat the Martian Madness event and Moon Lord, then visit the Stylist to select this hairstyle.";
		}
		if (PlanteraHairstyles.Contains(hairId))
		{
			return "Unavailable during character creation. Defeat Plantera, then visit the Stylist to select this hairstyle.";
		}

		ModHair? modHair = HairLoader.GetHair(hairId);
		if (modHair is not null)
		{
			string conditions = string.Join(
				", ",
				modHair.GetUnlockConditions()
					.Select(condition => condition.Description.Value)
					.Where(description => !string.IsNullOrWhiteSpace(description)));
			return string.IsNullOrWhiteSpace(conditions)
				? $"Unavailable during character creation. The {modHair.Mod.DisplayName} mod makes this hairstyle available through the Stylist."
				: $"Unavailable during character creation. Visit the Stylist after meeting these conditions: {conditions}.";
		}

		return "Unavailable during character creation. Unlock this hairstyle in a world, then select it through the Stylist.";
	}
}

internal sealed class AccessibleColorEditorMenuState : AccessibleMenuState
{
	private readonly string _name;
	private readonly Func<Color> _get;
	private readonly Action<Color> _set;

	internal AccessibleColorEditorMenuState(AccessibleMenuController controller, string name, Func<Color> get, Action<Color> set)
		: base(controller)
	{
		_name = name;
		_get = get;
		_set = set;
	}

	protected override string Title => _name;

	protected override void BuildEntries(List<AccessibleMenuEntry> entries)
	{
		entries.Add(Channel("Red", color => color.R, (color, value) => new Color(value, color.G, color.B)));
		entries.Add(Channel("Green", color => color.G, (color, value) => new Color(color.R, value, color.B)));
		entries.Add(Channel("Blue", color => color.B, (color, value) => new Color(color.R, color.G, value)));
	}

	private AccessibleMenuEntry Channel(string name, Func<Color, byte> getChannel, Func<Color, byte, Color> replace)
	{
		void Down()
		{
			Color color = _get();
			_set(replace(color, (byte)Math.Max(0, getChannel(color) - 5)));
		}
		void Up()
		{
			Color color = _get();
			_set(replace(color, (byte)Math.Min(255, getChannel(color) + 5)));
		}
		return new AccessibleMenuEntry(
			() => $"{name}: {getChannel(_get())}",
			Up,
			Down,
			Up,
			role: "slider");
	}
}
