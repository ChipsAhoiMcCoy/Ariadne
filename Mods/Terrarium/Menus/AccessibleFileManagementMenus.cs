#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using ReLogic.OS;
using Terraria;
using Terraria.IO;
using Terraria.Localization;
using Terraria.ModLoader;
using Terraria.Social;
using Terraria.Utilities;

namespace Terrarium.Menus;

internal sealed class AccessiblePlayerActionsMenuState : AccessibleMenuState
{
	private static readonly MethodInfo? ErasePlayer = typeof(Main).GetMethod("ErasePlayer", BindingFlags.NonPublic | BindingFlags.Static);
	private readonly PlayerFileData _player;
	private readonly Action<PlayerFileData> _play;

	internal AccessiblePlayerActionsMenuState(AccessibleMenuController controller, PlayerFileData player, Action<PlayerFileData> play)
		: base(controller)
	{
		_player = player;
		_play = play;
	}

	protected override string Title => _player.Name;

	internal List<AccessibleMenuEntry> GetActionEntries()
	{
		List<AccessibleMenuEntry> entries = [];
		BuildEntries(entries);
		return entries;
	}

	protected override void BuildEntries(List<AccessibleMenuEntry> entries)
	{
		entries.Add(new(
			() => "Play",
			() => _play(_player),
			description: () => _player.Player.loadStatus == 0 ? "Select this character." : $"Character load error {_player.Player.loadStatus}.",
			enabled: () => _player.Player.loadStatus == 0));
		entries.Add(new(
			() => _player.IsFavorite ? Language.GetTextValue("UI.Unfavorite") : Language.GetTextValue("UI.Favorite"),
			ToggleFavorite,
			previousValue: ToggleFavorite,
			nextValue: ToggleFavorite,
			role: "toggle"));
		if (SocialAPI.Cloud is not null)
		{
			entries.Add(new(
				() => Language.GetTextValue(_player.IsCloudSave ? "UI.MoveOffCloud" : "UI.MoveToCloud"),
				ToggleCloud,
				description: () => CloudDescription(_player)));
		}
		entries.Add(new(() => Language.GetTextValue("UI.Rename"), Rename));
		entries.Add(new(
			() => Language.GetTextValue("UI.Delete"),
			ConfirmDelete,
			description: () => _player.IsFavorite ? "Unfavorite this character before deleting it." : "Permanently delete this character.",
			enabled: () => !_player.IsFavorite));
	}

	private void ToggleFavorite() => _player.ToggleFavorite();

	private void Rename()
	{
		Controller.Navigate(new AccessibleTextInputState(
			Controller,
			Lang.menu[45].Value,
			_player.Name,
			20,
			name =>
			{
				if (string.IsNullOrWhiteSpace(name))
				{
					Announce("The character name cannot be empty.");
					return;
				}
				if (Main.PlayerList.Any(other => other != _player && string.Equals(other.Name, name, StringComparison.CurrentCultureIgnoreCase)))
				{
					Announce("A character with that name already exists.");
					return;
				}
				_player.Rename(name);
				Controller.Back();
			}));
	}

	private void ToggleCloud()
	{
		try
		{
			if (_player.IsCloudSave)
			{
				_player.MoveToLocal();
			}
			else
			{
				if (!AccessibleCloudSaveSupport.HasSufficientStorage(_player, recalculate: true))
				{
					Announce(Language.GetTextValue("tModLoader.CloudWarning"));
					return;
				}
				_player.MoveToCloud();
			}
		}
		catch (Exception exception)
		{
			Announce("The character could not be moved. See the tModLoader client log.");
			ModContent.GetInstance<TerrariumMod>().Logger.Error("Could not move character save.", exception);
		}
	}

	private static string CloudDescription(FileData file)
	{
		if (file.IsCloudSave)
		{
			return "Move this character save back to local storage.";
		}
		return AccessibleCloudSaveSupport.HasSufficientStorage(file, recalculate: false)
			? "Move this character save to cloud storage."
			: Language.GetTextValue("tModLoader.CloudWarning");
	}

	private void ConfirmDelete()
	{
		Controller.Navigate(new AccessibleConfirmationMenuState(
			Controller,
			$"Delete {_player.Name}?",
			"This permanently deletes the character and its map data.",
			Delete));
	}

	private void Delete()
	{
		int index = Main.PlayerList.IndexOf(_player);
		if (index < 0 || ErasePlayer is null)
		{
			Announce("The character could not be found.");
			return;
		}
		try
		{
			ErasePlayer.Invoke(null, [index]);
			Main.LoadPlayers();
			Announce($"Deleted {_player.Name}.");
			Controller.Back();
		}
		catch (Exception exception)
		{
			Announce("The character could not be deleted. See the tModLoader client log.");
			ModContent.GetInstance<TerrariumMod>().Logger.Error("Could not delete character save.", exception);
		}
	}
}

internal sealed class AccessibleWorldActionsMenuState : AccessibleMenuState
{
	private static readonly MethodInfo? EraseWorld = typeof(Main).GetMethod("EraseWorld", BindingFlags.NonPublic | BindingFlags.Static);
	private readonly WorldFileData _world;
	private readonly Action<WorldFileData> _play;
	private readonly Func<WorldFileData, bool>? _canPlay;
	private bool _seedCopied;

	internal AccessibleWorldActionsMenuState(
		AccessibleMenuController controller,
		WorldFileData world,
		Action<WorldFileData> play,
		Func<WorldFileData, bool>? canPlay = null)
		: base(controller)
	{
		_world = world;
		_play = play;
		_canPlay = canPlay;
	}

	protected override string Title => _world.Name;

	internal List<AccessibleMenuEntry> GetActionEntries()
	{
		List<AccessibleMenuEntry> entries = [];
		BuildEntries(entries);
		return entries;
	}

	public override void OnActivate()
	{
		_seedCopied = false;
		base.OnActivate();
	}

	protected override void BuildEntries(List<AccessibleMenuEntry> entries)
	{
		entries.Add(new(
			() => "Play",
			() => _play(_world),
			description: () => _canPlay?.Invoke(_world) ?? _world.IsValid
				? "Select this world."
				: "This world is invalid or is not compatible with the selected character.",
			enabled: () => _canPlay?.Invoke(_world) ?? _world.IsValid));
		entries.Add(new(
			() => _world.IsFavorite ? Language.GetTextValue("UI.Unfavorite") : Language.GetTextValue("UI.Favorite"),
			ToggleFavorite,
			previousValue: ToggleFavorite,
			nextValue: ToggleFavorite,
			role: "toggle"));
		if (SocialAPI.Cloud is not null)
		{
			entries.Add(new(
				() => Language.GetTextValue(_world.IsCloudSave ? "UI.MoveOffCloud" : "UI.MoveToCloud"),
				ToggleCloud,
				description: CloudDescription));
		}
		if (_world.HasValidSeed)
		{
			entries.Add(new(
				() => Language.GetTextValue("UI.CopySeed", _world.GetFullSeedText(allowCropping: true)),
				CopySeed,
				description: () => _seedCopied
					? Language.GetTextValue("UI.SeedCopied")
					: "Copy the full world seed to the clipboard."));
		}
		List<AccessibleDiagnosticDetail> modMismatch = AccessibleWorldFileDiagnostics.GetModMismatch(_world);
		if (modMismatch.Count > 0)
		{
			entries.Add(new(
				() => "Mod mismatch warning",
				() => Controller.Navigate(new AccessibleDiagnosticMenuState(Controller, "World Mod Mismatch", modMismatch)),
				description: () => "The loaded mods or active mod pack differ from the last time this world was played."));
		}
		List<AccessibleDiagnosticDetail> saveErrors = AccessibleWorldFileDiagnostics.GetSaveErrors(_world);
		if (saveErrors.Count > 0)
		{
			entries.Add(new(
				() => $"Mod save errors: {saveErrors.Count}",
				() => Controller.Navigate(new AccessibleDiagnosticMenuState(Controller, "World Mod Save Errors", saveErrors)),
				description: () => "One or more mods reported errors while saving custom world data."));
		}
		entries.Add(new(() => Language.GetTextValue("UI.Rename"), Rename));
		entries.Add(new(
			() => Language.GetTextValue("UI.Delete"),
			ConfirmDelete,
			description: () => _world.IsFavorite ? "Unfavorite this world before deleting it." : "Permanently delete this world.",
			enabled: () => !_world.IsFavorite));
	}

	private void ToggleFavorite() => _world.ToggleFavorite();

	private void CopySeed()
	{
		Platform.Get<IClipboard>().Value = _world.GetFullSeedText();
		_seedCopied = true;
	}

	private void Rename()
	{
		Controller.Navigate(new AccessibleTextInputState(
			Controller,
			Lang.menu[48].Value,
			_world.Name,
			27,
			name =>
			{
				if (string.IsNullOrWhiteSpace(name))
				{
					Announce("The world name cannot be empty.");
					return;
				}
				if (Main.WorldList.Any(other => other != _world && string.Equals(other.Name, name, StringComparison.CurrentCultureIgnoreCase)))
				{
					Announce("A world with that name already exists.");
					return;
				}
				_world.Rename(name);
				Controller.Back();
			}));
	}

	private void ToggleCloud()
	{
		try
		{
			if (_world.IsCloudSave)
			{
				_world.MoveToLocal();
			}
			else
			{
				if (!AccessibleCloudSaveSupport.HasSufficientStorage(_world, recalculate: true))
				{
					Announce(Language.GetTextValue("tModLoader.CloudWarning"));
					return;
				}
				_world.MoveToCloud();
			}
		}
		catch (Exception exception)
		{
			Announce("The world could not be moved. See the tModLoader client log.");
			ModContent.GetInstance<TerrariumMod>().Logger.Error("Could not move world save.", exception);
		}
	}

	private string CloudDescription()
	{
		if (_world.IsCloudSave)
		{
			return "Move this world save back to local storage.";
		}
		return AccessibleCloudSaveSupport.HasSufficientStorage(_world, recalculate: false)
			? "Move this world save to cloud storage."
			: Language.GetTextValue("tModLoader.CloudWarning");
	}

	private void ConfirmDelete()
	{
		Controller.Navigate(new AccessibleConfirmationMenuState(
			Controller,
			$"Delete {_world.Name}?",
			"This permanently deletes the world.",
			Delete));
	}

	private void Delete()
	{
		int index = Main.WorldList.IndexOf(_world);
		if (index < 0 || EraseWorld is null)
		{
			Announce("The world could not be found.");
			return;
		}
		try
		{
			EraseWorld.Invoke(null, [index]);
			Main.LoadWorlds();
			Announce($"Deleted {_world.Name}.");
			Controller.Back();
		}
		catch (Exception exception)
		{
			Announce("The world could not be deleted. See the tModLoader client log.");
			ModContent.GetInstance<TerrariumMod>().Logger.Error("Could not delete world save.", exception);
		}
	}
}

internal static class AccessibleCloudSaveSupport
{
	private static readonly Type? SteamType = typeof(Mod).Assembly.GetType("Terraria.ModLoader.Engine.Steam");
	private static readonly MethodInfo? RecalculateStorage = SteamType?.GetMethod(
		"RecalculateAvailableSteamCloudStorage",
		BindingFlags.Public | BindingFlags.Static);
	private static readonly MethodInfo? CheckStorage = SteamType?.GetMethod(
		"CheckSteamCloudStorageSufficient",
		BindingFlags.Public | BindingFlags.Static);

	internal static bool HasSufficientStorage(FileData file, bool recalculate)
	{
		if (SocialAPI.Cloud is null || CheckStorage is null)
		{
			return true;
		}

		try
		{
			if (recalculate)
			{
				RecalculateStorage?.Invoke(null, null);
			}
			ulong fileSize = (ulong)Math.Max(0, FileUtilities.GetFileSize(file.Path, file.IsCloudSave));
			return CheckStorage.Invoke(null, [fileSize]) is not bool sufficient || sufficient;
		}
		catch (Exception exception)
		{
			ModContent.GetInstance<TerrariumMod>().Logger.Warn(
				$"Could not check cloud storage before moving {file.Path}.",
				exception);
			return true;
		}
	}
}

internal readonly record struct AccessibleDiagnosticDetail(string Label, string Details);

internal static class AccessibleWorldFileDiagnostics
{
	private const BindingFlags InstanceMembers = BindingFlags.Instance | BindingFlags.NonPublic;
	private static readonly FieldInfo? UsedMods = typeof(WorldFileData).GetField("usedMods", InstanceMembers);
	private static readonly FieldInfo? SavedModPack = typeof(WorldFileData).GetField("modPack", InstanceMembers);
	private static readonly PropertyInfo? ModSaveErrors = typeof(WorldFileData).GetProperty("ModSaveErrors", InstanceMembers);
	private static readonly Type? ModOrganizerType = typeof(Mod).Assembly.GetType("Terraria.ModLoader.Core.ModOrganizer");
	private static readonly FieldInfo? ActiveModPack = ModOrganizerType?.GetField(
		"ModPackActive",
		BindingFlags.Static | BindingFlags.NonPublic);

	internal static List<AccessibleDiagnosticDetail> GetModMismatch(WorldFileData world)
	{
		if (UsedMods?.GetValue(world) is not IEnumerable<string> recordedMods)
		{
			return [];
		}

		HashSet<string> recorded = new(recordedMods, StringComparer.OrdinalIgnoreCase);
		HashSet<string> loaded = new(
			Terraria.ModLoader.ModLoader.Mods.Select(mod => mod.Name),
			StringComparer.OrdinalIgnoreCase);
		List<AccessibleDiagnosticDetail> details = [];

		string? savedPack = SavedModPack?.GetValue(world) as string;
		string? activePackPath = ActiveModPack?.GetValue(null) as string;
		string? activePack = string.IsNullOrWhiteSpace(activePackPath)
			? null
			: Path.GetFileNameWithoutExtension(activePackPath);
		if (!string.Equals(savedPack, activePack, StringComparison.OrdinalIgnoreCase))
		{
			details.Add(new(
				"Mod pack changed",
				$"This world last used mod pack {savedPack ?? "None"}. The active mod pack is {activePack ?? "None"}."));
		}

		string[] missing = recorded
			.Except(loaded, StringComparer.OrdinalIgnoreCase)
			.OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
			.ToArray();
		if (missing.Length > 0)
		{
			details.Add(new(
				$"Missing mods: {missing.Length}",
				$"Mods used last time but not loaded now: {string.Join(", ", missing)}."));
		}

		string[] added = loaded
			.Where(name => !string.Equals(name, "ModLoader", StringComparison.OrdinalIgnoreCase))
			.Except(recorded, StringComparer.OrdinalIgnoreCase)
			.OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
			.ToArray();
		if (added.Length > 0)
		{
			details.Add(new(
				$"Newly loaded mods: {added.Length}",
				$"Mods loaded now but not used last time: {string.Join(", ", added)}."));
		}

		return details;
	}

	internal static List<AccessibleDiagnosticDetail> GetSaveErrors(WorldFileData world)
	{
		if (ModSaveErrors?.GetValue(world) is not IEnumerable<KeyValuePair<string, string>> errors)
		{
			return [];
		}

		return errors
			.Select(error => new AccessibleDiagnosticDetail(error.Key, error.Value))
			.OrderBy(error => error.Label, StringComparer.OrdinalIgnoreCase)
			.ToList();
	}

	internal static string WarningSummary(WorldFileData world)
	{
		List<string> warnings = [];
		if (GetModMismatch(world).Count > 0)
		{
			warnings.Add("Mod mismatch warning");
		}
		int saveErrorCount = GetSaveErrors(world).Count;
		if (saveErrorCount > 0)
		{
			warnings.Add($"{saveErrorCount} mod save errors");
		}
		return warnings.Count == 0 ? string.Empty : $" Warning: {string.Join("; ", warnings)}.";
	}
}

internal sealed class AccessibleDiagnosticMenuState : AccessibleMenuState
{
	private readonly string _title;
	private readonly List<AccessibleDiagnosticDetail> _details;

	internal AccessibleDiagnosticMenuState(
		AccessibleMenuController controller,
		string title,
		IEnumerable<AccessibleDiagnosticDetail> details)
		: base(controller)
	{
		_title = title;
		_details = [.. details];
	}

	protected override string Title => _title;

	protected override void BuildEntries(List<AccessibleMenuEntry> entries)
	{
		foreach (AccessibleDiagnosticDetail detail in _details)
		{
			AccessibleDiagnosticDetail capturedDetail = detail;
			entries.Add(new(
				() => capturedDetail.Label,
				description: () => capturedDetail.Details,
				role: "information"));
		}
	}
}

internal sealed class AccessibleConfirmationMenuState : AccessibleMenuState
{
	private readonly string _title;
	private readonly string _message;
	private readonly Action _confirm;

	internal AccessibleConfirmationMenuState(AccessibleMenuController controller, string title, string message, Action confirm)
		: base(controller)
	{
		_title = title;
		_message = message;
		_confirm = confirm;
	}

	protected override string Title => _title;

	protected override void BuildEntries(List<AccessibleMenuEntry> entries)
	{
		entries.Add(new(() => Lang.menu[104].Value, _confirm, description: () => _message));
		entries.Add(new(() => Lang.menu[105].Value, Controller.Back, description: () => "Cancel and go back."));
	}
}
