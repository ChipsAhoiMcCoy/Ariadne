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
			() => $"{ConfigFieldLabel(nameof(TerrariumClientConfig.WallToneEnabled))}: {OnOff(_pending.WallToneEnabled)}",
			ToggleWallToneEnabled,
			previousValue: ToggleWallToneEnabled,
			nextValue: ToggleWallToneEnabled,
			description: () => ConfigFieldTooltip(nameof(TerrariumClientConfig.WallToneEnabled)),
			role: "toggle",
			adjustmentAnnouncement: () => OnOff(_pending.WallToneEnabled)));
		entries.Add(new(
			() => $"{ConfigFieldLabel(nameof(TerrariumClientConfig.WallToneVolumePercent))}: {_pending.WallToneVolumePercent} percent",
			IncreaseWallToneVolume,
			previousValue: DecreaseWallToneVolume,
			nextValue: IncreaseWallToneVolume,
			description: () => ConfigFieldTooltip(nameof(TerrariumClientConfig.WallToneVolumePercent)),
			role: "slider",
			adjustmentAnnouncement: () => $"{_pending.WallToneVolumePercent} percent"));
		entries.Add(new(
			() => $"{ConfigFieldLabel(nameof(TerrariumClientConfig.WallToneRangeTiles))}: {_pending.WallToneRangeTiles} tiles",
			IncreaseWallToneRange,
			previousValue: DecreaseWallToneRange,
			nextValue: IncreaseWallToneRange,
			description: () => ConfigFieldTooltip(nameof(TerrariumClientConfig.WallToneRangeTiles)),
			role: "slider",
			adjustmentAnnouncement: () => $"{_pending.WallToneRangeTiles} tiles"));
		entries.Add(new(
			() => $"{ConfigFieldLabel(nameof(TerrariumClientConfig.WallToneSpatialization))}: {SpatializationName(_pending.WallToneSpatialization)}",
			NextSpatialization,
			previousValue: PreviousSpatialization,
			nextValue: NextSpatialization,
			description: () => ConfigFieldTooltip(nameof(TerrariumClientConfig.WallToneSpatialization)),
			role: "choice",
			adjustmentAnnouncement: () => SpatializationName(_pending.WallToneSpatialization)));
		entries.Add(new(
			() => $"{ConfigFieldLabel(nameof(TerrariumClientConfig.WallToneItdMilliseconds))}: {ItdAmount(_pending.WallToneItdMilliseconds)}",
			IncreaseWallToneItd,
			previousValue: DecreaseWallToneItd,
			nextValue: IncreaseWallToneItd,
			description: () => ConfigFieldTooltip(nameof(TerrariumClientConfig.WallToneItdMilliseconds)),
			role: "slider",
			adjustmentAnnouncement: () => ItdAmount(_pending.WallToneItdMilliseconds)));
		entries.Add(new(
			() => $"{ConfigFieldLabel(nameof(TerrariumClientConfig.HostileMobTonesEnabled))}: {OnOff(_pending.HostileMobTonesEnabled)}",
			ToggleHostileMobTonesEnabled,
			previousValue: ToggleHostileMobTonesEnabled,
			nextValue: ToggleHostileMobTonesEnabled,
			description: () => ConfigFieldTooltip(nameof(TerrariumClientConfig.HostileMobTonesEnabled)),
			role: "toggle",
			adjustmentAnnouncement: () => OnOff(_pending.HostileMobTonesEnabled)));
		entries.Add(new(
			() => $"{ConfigFieldLabel(nameof(TerrariumClientConfig.HostileMobToneVolumePercent))}: {_pending.HostileMobToneVolumePercent} percent",
			IncreaseHostileMobToneVolume,
			previousValue: DecreaseHostileMobToneVolume,
			nextValue: IncreaseHostileMobToneVolume,
			description: () => ConfigFieldTooltip(nameof(TerrariumClientConfig.HostileMobToneVolumePercent)),
			role: "slider",
			adjustmentAnnouncement: () => $"{_pending.HostileMobToneVolumePercent} percent"));
		entries.Add(new(
			() => $"{ConfigFieldLabel(nameof(TerrariumClientConfig.HostileMobMaximumEmitters))}: {_pending.HostileMobMaximumEmitters}",
			IncreaseHostileMobMaximumEmitters,
			previousValue: DecreaseHostileMobMaximumEmitters,
			nextValue: IncreaseHostileMobMaximumEmitters,
			description: () => ConfigFieldTooltip(nameof(TerrariumClientConfig.HostileMobMaximumEmitters)),
			role: "slider",
			adjustmentAnnouncement: () => $"{_pending.HostileMobMaximumEmitters} enemies"));
		entries.Add(new(
			() => $"{ConfigFieldLabel(nameof(TerrariumClientConfig.HostileMobToneSpatialization))}: {SpatializationName(_pending.HostileMobToneSpatialization)}",
			NextHostileMobSpatialization,
			previousValue: PreviousHostileMobSpatialization,
			nextValue: NextHostileMobSpatialization,
			description: () => ConfigFieldTooltip(nameof(TerrariumClientConfig.HostileMobToneSpatialization)),
			role: "choice",
			adjustmentAnnouncement: () => SpatializationName(_pending.HostileMobToneSpatialization)));
		entries.Add(new(
			() => $"{ConfigFieldLabel(nameof(TerrariumClientConfig.HostileMobToneItdMilliseconds))}: {ItdAmount(_pending.HostileMobToneItdMilliseconds)}",
			IncreaseHostileMobToneItd,
			previousValue: DecreaseHostileMobToneItd,
			nextValue: IncreaseHostileMobToneItd,
			description: () => ConfigFieldTooltip(nameof(TerrariumClientConfig.HostileMobToneItdMilliseconds)),
			role: "slider",
			adjustmentAnnouncement: () => ItdAmount(_pending.HostileMobToneItdMilliseconds)));
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
			description: () => WithStatus("Set all pending Terrarium audio options back to their defaults. Use Save changes to keep them.")));
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

	private void ToggleWallToneEnabled()
	{
		_pending.WallToneEnabled = !_pending.WallToneEnabled;
		MarkChanged();
	}

	private void IncreaseWallToneVolume()
	{
		_pending.WallToneVolumePercent = Math.Min(100, _pending.WallToneVolumePercent + 5);
		MarkChanged();
	}

	private void DecreaseWallToneVolume()
	{
		_pending.WallToneVolumePercent = Math.Max(0, _pending.WallToneVolumePercent - 5);
		MarkChanged();
	}

	private void IncreaseWallToneRange()
	{
		_pending.WallToneRangeTiles = Math.Min(30, _pending.WallToneRangeTiles + 1);
		MarkChanged();
	}

	private void DecreaseWallToneRange()
	{
		_pending.WallToneRangeTiles = Math.Max(4, _pending.WallToneRangeTiles - 1);
		MarkChanged();
	}

	private void NextSpatialization()
	{
		_pending.WallToneSpatialization = _pending.WallToneSpatialization == WallToneSpatializationMode.Binaural
			? WallToneSpatializationMode.StereoPan
			: WallToneSpatializationMode.Binaural;
		MarkChanged();
	}

	private void PreviousSpatialization()
	{
		NextSpatialization();
	}

	private void IncreaseWallToneItd()
	{
		AdjustWallToneItd(0.05f);
	}

	private void DecreaseWallToneItd()
	{
		AdjustWallToneItd(-0.05f);
	}

	private void AdjustWallToneItd(float adjustment)
	{
		float adjusted = Math.Clamp(_pending.WallToneItdMilliseconds + adjustment, 0f, 1f);
		_pending.WallToneItdMilliseconds = MathF.Round(adjusted * 20f) / 20f;
		MarkChanged();
	}

	private void ToggleHostileMobTonesEnabled()
	{
		_pending.HostileMobTonesEnabled = !_pending.HostileMobTonesEnabled;
		MarkChanged();
	}

	private void IncreaseHostileMobToneVolume()
	{
		_pending.HostileMobToneVolumePercent = Math.Min(100, _pending.HostileMobToneVolumePercent + 5);
		MarkChanged();
	}

	private void DecreaseHostileMobToneVolume()
	{
		_pending.HostileMobToneVolumePercent = Math.Max(0, _pending.HostileMobToneVolumePercent - 5);
		MarkChanged();
	}

	private void IncreaseHostileMobMaximumEmitters()
	{
		_pending.HostileMobMaximumEmitters = Math.Min(4, _pending.HostileMobMaximumEmitters + 1);
		MarkChanged();
	}

	private void DecreaseHostileMobMaximumEmitters()
	{
		_pending.HostileMobMaximumEmitters = Math.Max(1, _pending.HostileMobMaximumEmitters - 1);
		MarkChanged();
	}

	private void NextHostileMobSpatialization()
	{
		_pending.HostileMobToneSpatialization = _pending.HostileMobToneSpatialization == WallToneSpatializationMode.Binaural
			? WallToneSpatializationMode.StereoPan
			: WallToneSpatializationMode.Binaural;
		MarkChanged();
	}

	private void PreviousHostileMobSpatialization()
	{
		NextHostileMobSpatialization();
	}

	private void IncreaseHostileMobToneItd()
	{
		AdjustHostileMobToneItd(0.05f);
	}

	private void DecreaseHostileMobToneItd()
	{
		AdjustHostileMobToneItd(-0.05f);
	}

	private void AdjustHostileMobToneItd(float adjustment)
	{
		float adjusted = Math.Clamp(_pending.HostileMobToneItdMilliseconds + adjustment, 0f, 1f);
		_pending.HostileMobToneItdMilliseconds = MathF.Round(adjusted * 20f) / 20f;
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
		_pending.WallToneEnabled = defaults.WallToneEnabled;
		_pending.WallToneVolumePercent = defaults.WallToneVolumePercent;
		_pending.WallToneRangeTiles = defaults.WallToneRangeTiles;
		_pending.WallToneSpatialization = defaults.WallToneSpatialization;
		_pending.WallToneItdMilliseconds = defaults.WallToneItdMilliseconds;
		_pending.HostileMobTonesEnabled = defaults.HostileMobTonesEnabled;
		_pending.HostileMobToneVolumePercent = defaults.HostileMobToneVolumePercent;
		_pending.HostileMobMaximumEmitters = defaults.HostileMobMaximumEmitters;
		_pending.HostileMobToneSpatialization = defaults.HostileMobToneSpatialization;
		_pending.HostileMobToneItdMilliseconds = defaults.HostileMobToneItdMilliseconds;
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
		return _pending.WallToneEnabled != _active.WallToneEnabled ||
			_pending.WallToneVolumePercent != _active.WallToneVolumePercent ||
			_pending.WallToneRangeTiles != _active.WallToneRangeTiles ||
			_pending.WallToneSpatialization != _active.WallToneSpatialization ||
			_pending.WallToneItdMilliseconds != _active.WallToneItdMilliseconds ||
			_pending.HostileMobTonesEnabled != _active.HostileMobTonesEnabled ||
			_pending.HostileMobToneVolumePercent != _active.HostileMobToneVolumePercent ||
			_pending.HostileMobMaximumEmitters != _active.HostileMobMaximumEmitters ||
			_pending.HostileMobToneSpatialization != _active.HostileMobToneSpatialization ||
			_pending.HostileMobToneItdMilliseconds != _active.HostileMobToneItdMilliseconds;
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
		return value ? "On" : "Off";
	}

	private static string SpatializationName(WallToneSpatializationMode mode)
	{
		return Language.GetTextValue(
			$"Mods.Terrarium.Configs.WallToneSpatializationMode.{mode}.Label");
	}

	private static string ItdAmount(float milliseconds)
	{
		return $"{milliseconds:0.00} milliseconds";
	}

	private static string ConfigFieldLabel(string fieldName)
	{
		return Language.GetTextValue(
			$"Mods.Terrarium.Configs.TerrariumClientConfig.{fieldName}.Label");
	}

	private static string ConfigFieldTooltip(string fieldName)
	{
		return Language.GetTextValue(
			$"Mods.Terrarium.Configs.TerrariumClientConfig.{fieldName}.Tooltip");
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
