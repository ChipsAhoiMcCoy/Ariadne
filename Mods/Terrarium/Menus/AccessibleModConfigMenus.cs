#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Terraria;
using Terraria.Localization;
using Terraria.ModLoader;
using Terraria.ModLoader.Config;
using Terrarium.Configs;

namespace Terrarium.Menus;

internal sealed record AccessibleModConfigView(Mod Owner, ModConfig Config);

internal static class AccessibleModConfigCatalog
{
	private const BindingFlags StaticMembers = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
	private static readonly FieldInfo? Configs = typeof(ConfigManager).GetField("Configs", StaticMembers);

	internal static List<AccessibleModConfigView> Load(string? modName = null)
	{
		if (Configs?.GetValue(null) is not IDictionary configs)
		{
			return [];
		}

		List<AccessibleModConfigView> result = [];
		foreach (DictionaryEntry entry in configs)
		{
			if (entry.Key is not Mod owner || entry.Value is not IEnumerable ownerConfigs ||
				modName is not null && !string.Equals(owner.Name, modName, StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			foreach (object? value in ownerConfigs)
			{
				if (value is ModConfig config)
				{
					result.Add(new(owner, config));
				}
			}
		}

		return [.. result
			.OrderBy(view => view.Owner.DisplayName, StringComparer.CurrentCultureIgnoreCase)
			.ThenBy(view => ConfigName(view.Config), StringComparer.CurrentCultureIgnoreCase)];
	}

	internal static string ConfigName(ModConfig config)
	{
		return string.IsNullOrWhiteSpace(config.DisplayName.Value) ? config.Name : config.DisplayName.Value;
	}
}

internal sealed class AccessibleModConfigListMenuState : AccessibleMenuState
{
	private readonly string? _selectedModName;
	private List<AccessibleModConfigView> _configs = [];
	private string? _selectedModDisplayName;

	internal AccessibleModConfigListMenuState(AccessibleMenuController controller, string? selectedModName = null)
		: base(controller)
	{
		_selectedModName = selectedModName;
	}

	internal static void OpenForMod(AccessibleMenuController controller, string modName)
	{
		List<AccessibleModConfigView> configs = AccessibleModConfigCatalog.Load(modName);
		if (configs.Count == 1 && configs[0].Config is TerrariumClientConfig terrariumConfig)
		{
			controller.Navigate(new AccessibleTerrariumConfigMenuState(controller, terrariumConfig));
			return;
		}

		controller.Navigate(new AccessibleModConfigListMenuState(controller, modName));
	}

	protected override string Title => _selectedModDisplayName is null
		? Language.GetTextValue("tModLoader.ModConfiguration")
		: $"{Language.GetTextValue("tModLoader.ModConfiguration")}: {_selectedModDisplayName}";

	public override void OnActivate()
	{
		_configs = AccessibleModConfigCatalog.Load(_selectedModName);
		_selectedModDisplayName = _selectedModName is null ? null : _configs.FirstOrDefault()?.Owner.DisplayName ?? _selectedModName;
		base.OnActivate();
	}

	protected override void BuildEntries(List<AccessibleMenuEntry> entries)
	{
		if (_configs.Count == 0)
		{
			entries.Add(new(
				() => "No mod configurations are available",
				description: () => "No loaded mod registered a configuration in this scope.",
				enabled: () => false,
				role: "information"));
			return;
		}

		foreach (AccessibleModConfigView view in _configs)
		{
			AccessibleModConfigView capturedView = view;
			entries.Add(new(
				() => ConfigLabel(capturedView),
				() => OpenConfig(capturedView),
				description: () => ConfigDescription(capturedView),
				role: "configuration"));
		}
	}

	private string ConfigLabel(AccessibleModConfigView view)
	{
		string configName = AccessibleModConfigCatalog.ConfigName(view.Config);
		return _selectedModName is null ? $"{view.Owner.DisplayName}: {configName}" : configName;
	}

	private static string ConfigDescription(AccessibleModConfigView view)
	{
		string scope = view.Config.Mode == ConfigScope.ClientSide ? "Client-side configuration." : "Server-side configuration.";
		string editor = view.Config is TerrariumClientConfig
			? "Opens Terrarium's accessible configuration editor."
			: "Opens tModLoader's standard configuration editor.";
		return $"{scope} {editor}";
	}

	private void OpenConfig(AccessibleModConfigView view)
	{
		if (view.Config is TerrariumClientConfig terrariumConfig)
		{
			Controller.Navigate(new AccessibleTerrariumConfigMenuState(Controller, terrariumConfig));
			return;
		}

		view.Config.Open(
			onClose: () => Controller.Replace(new AccessibleModConfigListMenuState(Controller, _selectedModName)),
			playSound: false);
	}
}

internal sealed class AccessibleTerrariumConfigMenuState : AccessibleMenuState
{
	private readonly TerrariumClientConfig _active;
	private TerrariumClientConfig _pending;
	private string _lastStatus = string.Empty;

	internal AccessibleTerrariumConfigMenuState(AccessibleMenuController controller, TerrariumClientConfig active)
		: base(controller)
	{
		_active = active;
		_pending = Clone(active);
	}

	protected override string Title => AccessibleModConfigCatalog.ConfigName(_active);

	protected override void BuildEntries(List<AccessibleMenuEntry> entries)
	{
		entries.Add(new(
			() => $"Test toggle: {OnOff(_pending.TestToggle)}",
			ToggleTest,
			previousValue: ToggleTest,
			nextValue: ToggleTest,
			description: () => "A harmless test boolean used to verify accessible Mod Configuration support.",
			role: "toggle",
			adjustmentAnnouncement: () => OnOff(_pending.TestToggle)));
		entries.Add(new(
			() => $"Test level: {_pending.TestLevel}",
			IncreaseTestLevel,
			previousValue: DecreaseTestLevel,
			nextValue: IncreaseTestLevel,
			description: () => "A harmless test number from 0 through 10.",
			role: "slider",
			adjustmentAnnouncement: () => _pending.TestLevel.ToString()));
		entries.Add(new(
			() => $"Test mode: {TestModeName(_pending.TestMode)}",
			NextTestMode,
			previousValue: PreviousTestMode,
			nextValue: NextTestMode,
			description: () => "A harmless test choice used to verify cycling through configuration values.",
			role: "choice",
			adjustmentAnnouncement: () => TestModeName(_pending.TestMode)));
		entries.Add(new(
			() => "Save changes",
			Save,
			description: SaveDescription,
			enabled: HasChanges));
		entries.Add(new(
			() => "Revert unsaved changes",
			Revert,
			description: () => WithStatus("Restore the values from the currently saved Terrarium configuration."),
			enabled: HasChanges));
		entries.Add(new(
			() => "Restore defaults",
			RestoreDefaults,
			description: () => WithStatus("Set all pending Terrarium test options back to their defaults. Use Save changes to keep them.")));
	}

	protected override void GoBack()
	{
		if (!HasChanges())
		{
			base.GoBack();
			return;
		}

		Controller.Navigate(new AccessibleConfirmationMenuState(
			Controller,
			"Discard unsaved configuration changes?",
			"Choose Yes to discard the pending Terrarium configuration changes and return to the configuration list.",
			DiscardAndGoBack));
	}

	private void ToggleTest()
	{
		_pending.TestToggle = !_pending.TestToggle;
		MarkChanged();
	}

	private void IncreaseTestLevel()
	{
		_pending.TestLevel = Math.Min(10, _pending.TestLevel + 1);
		MarkChanged();
	}

	private void DecreaseTestLevel()
	{
		_pending.TestLevel = Math.Max(0, _pending.TestLevel - 1);
		MarkChanged();
	}

	private void NextTestMode()
	{
		_pending.TestMode = (TerrariumTestMode)(((int)_pending.TestMode + 1) % 3);
		MarkChanged();
	}

	private void PreviousTestMode()
	{
		_pending.TestMode = (TerrariumTestMode)(((int)_pending.TestMode + 2) % 3);
		MarkChanged();
	}

	private void Save()
	{
		string status = string.Empty;
		ConfigSaveResult result = _active.SaveChanges(
			_pending,
			(text, _) => status = text,
			silent: true);
		_lastStatus = string.IsNullOrWhiteSpace(status) ? SaveResultDescription(result) : status;
		if (result == ConfigSaveResult.Success)
		{
			_pending = Clone(_active);
		}
	}

	private void Revert()
	{
		_pending = Clone(_active);
		_lastStatus = "Unsaved changes reverted.";
	}

	private void RestoreDefaults()
	{
		TerrariumClientConfig defaults = new();
		_pending.TestToggle = defaults.TestToggle;
		_pending.TestLevel = defaults.TestLevel;
		_pending.TestMode = defaults.TestMode;
		_lastStatus = "Defaults restored as pending values.";
	}

	private void DiscardAndGoBack()
	{
		_pending = Clone(_active);
		Controller.Back();
		Controller.Back();
	}

	private void MarkChanged()
	{
		_lastStatus = string.Empty;
	}

	private bool HasChanges()
	{
		return _pending.TestToggle != _active.TestToggle ||
			_pending.TestLevel != _active.TestLevel ||
			_pending.TestMode != _active.TestMode;
	}

	private string SaveDescription()
	{
		return WithStatus("Persist the pending Terrarium configuration values.");
	}

	private string WithStatus(string description)
	{
		return string.IsNullOrWhiteSpace(_lastStatus) ? description : $"{description} {_lastStatus}";
	}

	private static TerrariumClientConfig Clone(TerrariumClientConfig config)
	{
		return (TerrariumClientConfig)ConfigManager.GeneratePopulatedClone(config);
	}

	private static string OnOff(bool value)
	{
		return Language.GetTextValue(value ? "GameUI.Enabled" : "GameUI.Disabled");
	}

	private static string TestModeName(TerrariumTestMode mode)
	{
		return mode switch
		{
			TerrariumTestMode.Brief => "Brief",
			TerrariumTestMode.Detailed => "Detailed",
			_ => "Standard",
		};
	}

	private static string SaveResultDescription(ConfigSaveResult result)
	{
		return result switch
		{
			ConfigSaveResult.NeedsReload => "These changes require reloading mods before they can be saved.",
			ConfigSaveResult.RequestSentToServer => "The configuration change request was sent to the server.",
			_ => "Configuration saved.",
		};
	}
}
