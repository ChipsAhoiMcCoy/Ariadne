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
using Terraria.ModLoader.Config.UI;
using Ariadne.Configs;

namespace Ariadne.Menus;

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
		if (configs.Count == 1 && configs[0].Config is AriadneClientConfig ariadneConfig)
		{
			controller.Navigate(new AccessibleAriadneConfigMenuState(controller, ariadneConfig));
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
		string editor = view.Config is AriadneClientConfig
			? "Opens Ariadne's accessible configuration editor."
			: "Opens tModLoader's standard configuration editor.";
		return $"{scope} {editor}";
	}

	private void OpenConfig(AccessibleModConfigView view)
	{
		if (view.Config is AriadneClientConfig ariadneConfig)
		{
			Controller.Navigate(new AccessibleAriadneConfigMenuState(Controller, ariadneConfig));
			return;
		}

		view.Config.Open(
			onClose: () => Controller.Replace(new AccessibleModConfigListMenuState(Controller, _selectedModName)),
			playSound: false);
	}
}

internal sealed class AccessibleAriadneConfigMenuState : AccessibleMenuState
{
	private readonly AriadneClientConfig _active;
	private AriadneClientConfig _pending;
	private string _lastStatus = string.Empty;

	internal AccessibleAriadneConfigMenuState(AccessibleMenuController controller, AriadneClientConfig active)
		: base(controller)
	{
		_active = active;
		_pending = Clone(active);
	}

	protected override string Title => AccessibleModConfigCatalog.ConfigName(_active);

	protected override void BuildEntries(List<AccessibleMenuEntry> entries)
	{
		AddToggle(entries, nameof(AriadneClientConfig.RadarEnabled),
			() => _pending.RadarEnabled, value => _pending.RadarEnabled = value);
		AddPercentSlider(entries, nameof(AriadneClientConfig.RadarVolumePercent),
			() => _pending.RadarVolumePercent, value => _pending.RadarVolumePercent = value);
		AddSlider(entries, nameof(AriadneClientConfig.RadarRangeTiles),
			() => _pending.RadarRangeTiles, value => _pending.RadarRangeTiles = value,
			minimum: 10, maximum: 60, step: 5, format: value => $"{value} tiles");
		AddToggle(entries, nameof(AriadneClientConfig.RadarSweepSpeechEnabled),
			() => _pending.RadarSweepSpeechEnabled, value => _pending.RadarSweepSpeechEnabled = value);
		AddToggle(entries, nameof(AriadneClientConfig.RadarDetectsOresAndValuables),
			() => _pending.RadarDetectsOresAndValuables, value => _pending.RadarDetectsOresAndValuables = value);
		AddToggle(entries, nameof(AriadneClientConfig.RadarDetectsContainers),
			() => _pending.RadarDetectsContainers, value => _pending.RadarDetectsContainers = value);
		AddToggle(entries, nameof(AriadneClientConfig.RadarDetectsCreatures),
			() => _pending.RadarDetectsCreatures, value => _pending.RadarDetectsCreatures = value);
		AddToggle(entries, nameof(AriadneClientConfig.RadarDetectsEnemies),
			() => _pending.RadarDetectsEnemies, value => _pending.RadarDetectsEnemies = value);
		AddToggle(entries, nameof(AriadneClientConfig.RadarDetectsDroppedItems),
			() => _pending.RadarDetectsDroppedItems, value => _pending.RadarDetectsDroppedItems = value);
		AddToggle(entries, nameof(AriadneClientConfig.RadarDetectsLiquids),
			() => _pending.RadarDetectsLiquids, value => _pending.RadarDetectsLiquids = value);
		AddToggle(entries, nameof(AriadneClientConfig.RadarDetectsTreesAndPlants),
			() => _pending.RadarDetectsTreesAndPlants, value => _pending.RadarDetectsTreesAndPlants = value);
		AddToggle(entries, nameof(AriadneClientConfig.RadarDetectsPlacedObjects),
			() => _pending.RadarDetectsPlacedObjects, value => _pending.RadarDetectsPlacedObjects = value);
		AddToggle(entries, nameof(AriadneClientConfig.BiomeAnnouncementsEnabled),
			() => _pending.BiomeAnnouncementsEnabled, value => _pending.BiomeAnnouncementsEnabled = value);
		AddToggle(entries, nameof(AriadneClientConfig.CursorEarconsEnabled),
			() => _pending.CursorEarconsEnabled, value => _pending.CursorEarconsEnabled = value);
		AddPercentSlider(entries, nameof(AriadneClientConfig.CursorEarconVolumePercent),
			() => _pending.CursorEarconVolumePercent, value => _pending.CursorEarconVolumePercent = value);
		AddToggle(entries, nameof(AriadneClientConfig.CursorCoordinateAnnouncementsEnabled),
			() => _pending.CursorCoordinateAnnouncementsEnabled, value => _pending.CursorCoordinateAnnouncementsEnabled = value);
		AddToggle(entries, nameof(AriadneClientConfig.RelativeCoordinateReadoutEnabled),
			() => _pending.RelativeCoordinateReadoutEnabled, value => _pending.RelativeCoordinateReadoutEnabled = value);
		AddToggle(entries, nameof(AriadneClientConfig.LowHealthHeartbeatEnabled),
			() => _pending.LowHealthHeartbeatEnabled, value => _pending.LowHealthHeartbeatEnabled = value);
		AddPercentSlider(entries, nameof(AriadneClientConfig.LowHealthHeartbeatVolumePercent),
			() => _pending.LowHealthHeartbeatVolumePercent, value => _pending.LowHealthHeartbeatVolumePercent = value);
		AddToggle(entries, nameof(AriadneClientConfig.LowHealthAnnouncementsEnabled),
			() => _pending.LowHealthAnnouncementsEnabled, value => _pending.LowHealthAnnouncementsEnabled = value);
		AddToggle(entries, nameof(AriadneClientConfig.BreathAnnouncementsEnabled),
			() => _pending.BreathAnnouncementsEnabled, value => _pending.BreathAnnouncementsEnabled = value);
		AddToggle(entries, nameof(AriadneClientConfig.HotbarAnnouncementsEnabled),
			() => _pending.HotbarAnnouncementsEnabled, value => _pending.HotbarAnnouncementsEnabled = value);
		AddToggle(entries, nameof(AriadneClientConfig.ItemPickupAnnouncementsEnabled),
			() => _pending.ItemPickupAnnouncementsEnabled, value => _pending.ItemPickupAnnouncementsEnabled = value);
		AddToggle(entries, nameof(AriadneClientConfig.SummonAnnouncementsEnabled),
			() => _pending.SummonAnnouncementsEnabled, value => _pending.SummonAnnouncementsEnabled = value);
		AddColumnSlider(entries, nameof(AriadneClientConfig.InventoryColumnCount),
			() => _pending.InventoryColumnCount, value => _pending.InventoryColumnCount = value);
		AddColumnSlider(entries, nameof(AriadneClientConfig.HotbarColumnCount),
			() => _pending.HotbarColumnCount, value => _pending.HotbarColumnCount = value);
		AddColumnSlider(entries, nameof(AriadneClientConfig.CraftingColumnCount),
			() => _pending.CraftingColumnCount, value => _pending.CraftingColumnCount = value);
		AddColumnSlider(entries, nameof(AriadneClientConfig.StorageColumnCount),
			() => _pending.StorageColumnCount, value => _pending.StorageColumnCount = value);
		AddColumnSlider(entries, nameof(AriadneClientConfig.ShopColumnCount),
			() => _pending.ShopColumnCount, value => _pending.ShopColumnCount = value);
		AddToggle(entries, nameof(AriadneClientConfig.FootstepSoundsEnabled),
			() => _pending.FootstepSoundsEnabled, value => _pending.FootstepSoundsEnabled = value);
		AddPercentSlider(entries, nameof(AriadneClientConfig.FootstepVolumePercent),
			() => _pending.FootstepVolumePercent, value => _pending.FootstepVolumePercent = value);
		AddToggle(entries, nameof(AriadneClientConfig.MovementBumpTonesEnabled),
			() => _pending.MovementBumpTonesEnabled, value => _pending.MovementBumpTonesEnabled = value);
		AddPercentSlider(entries, nameof(AriadneClientConfig.MovementBumpVolumePercent),
			() => _pending.MovementBumpVolumePercent, value => _pending.MovementBumpVolumePercent = value);
		AddToggle(entries, nameof(AriadneClientConfig.WallToneEnabled),
			() => _pending.WallToneEnabled, value => _pending.WallToneEnabled = value);
		AddPercentSlider(entries, nameof(AriadneClientConfig.WallToneVolumePercent),
			() => _pending.WallToneVolumePercent, value => _pending.WallToneVolumePercent = value);
		AddSlider(entries, nameof(AriadneClientConfig.WallToneRangeTiles),
			() => _pending.WallToneRangeTiles, value => _pending.WallToneRangeTiles = value,
			minimum: 4, maximum: 30, step: 1, format: value => $"{value} tiles");
		AddToggle(entries, nameof(AriadneClientConfig.SpatialAudioDistanceAttenuationEnabled),
			() => _pending.SpatialAudioDistanceAttenuationEnabled, value => _pending.SpatialAudioDistanceAttenuationEnabled = value);
		AddToggle(entries, nameof(AriadneClientConfig.ElevationMovementCuesEnabled),
			() => _pending.ElevationMovementCuesEnabled, value => _pending.ElevationMovementCuesEnabled = value);
		AddPercentSlider(entries, nameof(AriadneClientConfig.ElevationMovementCueVolumePercent),
			() => _pending.ElevationMovementCueVolumePercent, value => _pending.ElevationMovementCueVolumePercent = value);
		AddToggle(entries, nameof(AriadneClientConfig.DropWarningsEnabled),
			() => _pending.DropWarningsEnabled, value => _pending.DropWarningsEnabled = value);
		AddPercentSlider(entries, nameof(AriadneClientConfig.DropWarningVolumePercent),
			() => _pending.DropWarningVolumePercent, value => _pending.DropWarningVolumePercent = value);
		AddSlider(entries, nameof(AriadneClientConfig.DropDetectionRangeTiles),
			() => _pending.DropDetectionRangeTiles, value => _pending.DropDetectionRangeTiles = value,
			minimum: 3, maximum: 60, step: 1, format: value => $"{value} tiles");
		AddSlider(entries, nameof(AriadneClientConfig.DropWarningLookaheadTiles),
			() => _pending.DropWarningLookaheadTiles, value => _pending.DropWarningLookaheadTiles = value,
			minimum: 1, maximum: 12, step: 1, format: value => $"{value} tiles");
		AddToggle(entries, nameof(AriadneClientConfig.TraversalLandmarkCuesEnabled),
			() => _pending.TraversalLandmarkCuesEnabled, value => _pending.TraversalLandmarkCuesEnabled = value);
		AddPercentSlider(entries, nameof(AriadneClientConfig.TraversalLandmarkCueVolumePercent),
			() => _pending.TraversalLandmarkCueVolumePercent, value => _pending.TraversalLandmarkCueVolumePercent = value);
		AddToggle(entries, nameof(AriadneClientConfig.HostileMobTonesEnabled),
			() => _pending.HostileMobTonesEnabled, value => _pending.HostileMobTonesEnabled = value);
		AddPercentSlider(entries, nameof(AriadneClientConfig.HostileMobToneVolumePercent),
			() => _pending.HostileMobToneVolumePercent, value => _pending.HostileMobToneVolumePercent = value);
		AddPercentSlider(entries, nameof(AriadneClientConfig.FreecamBeaconVolumePercent),
			() => _pending.FreecamBeaconVolumePercent, value => _pending.FreecamBeaconVolumePercent = value);
		AddToggle(entries, nameof(AriadneClientConfig.SpatialAudioItdEnabled),
			() => _pending.SpatialAudioItdEnabled, value => _pending.SpatialAudioItdEnabled = value);
		entries.Add(new(
			() => $"{ConfigFieldLabel(nameof(AriadneClientConfig.SpatialAudioItdStrengthMilliseconds))}: {ItdAmount(_pending.SpatialAudioItdStrengthMilliseconds)}",
			IncreaseSpatialAudioItdStrength,
			previousValue: DecreaseSpatialAudioItdStrength,
			nextValue: IncreaseSpatialAudioItdStrength,
			description: () => ConfigFieldTooltip(nameof(AriadneClientConfig.SpatialAudioItdStrengthMilliseconds)),
			role: "slider",
			adjustmentAnnouncement: () => ItdAmount(_pending.SpatialAudioItdStrengthMilliseconds)));
		entries.Add(new(
			() => "Save changes",
			Save,
			description: SaveDescription,
			enabled: HasChanges));
		entries.Add(new(
			() => "Revert unsaved changes",
			Revert,
			description: () => WithStatus("Restore the values from the currently saved Ariadne configuration."),
			enabled: HasChanges));
		entries.Add(new(
			() => "Restore defaults",
			RestoreDefaults,
			description: () => WithStatus("Set all pending Ariadne accessibility options back to their defaults. Use Save changes to keep them.")));
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
			"Choose Yes to discard the pending Ariadne configuration changes and return to the configuration list.",
			DiscardAndGoBack));
	}

	/// <summary>
	/// Declares one Boolean option. Building every option through these two helpers
	/// keeps a new setting to a single line and, with the pending comparison and
	/// default restore now driven from the config object itself, removes the older
	/// arrangement's habit of leaving an option half-wired across five places.
	/// </summary>
	private void AddToggle(
		List<AccessibleMenuEntry> entries,
		string field,
		Func<bool> read,
		Action<bool> write)
	{
		void Toggle()
		{
			write(!read());
			MarkChanged();
		}

		entries.Add(new(
			() => $"{ConfigFieldLabel(field)}: {OnOff(read())}",
			Toggle,
			previousValue: Toggle,
			nextValue: Toggle,
			description: () => ConfigFieldTooltip(field),
			role: "toggle",
			adjustmentAnnouncement: () => OnOff(read())));
	}

	private void AddPercentSlider(
		List<AccessibleMenuEntry> entries,
		string field,
		Func<int> read,
		Action<int> write)
	{
		AddSlider(entries, field, read, write, 0, 100, 5, value => $"{value} percent");
	}

	private void AddColumnSlider(
		List<AccessibleMenuEntry> entries,
		string field,
		Func<int> read,
		Action<int> write)
	{
		AddSlider(entries, field, read, write, 1, 10, 1,
			value => $"{value} {(value == 1 ? "column" : "columns")}");
	}

	private void AddSlider(
		List<AccessibleMenuEntry> entries,
		string field,
		Func<int> read,
		Action<int> write,
		int minimum,
		int maximum,
		int step,
		Func<int, string> format)
	{
		void Adjust(int direction)
		{
			write(Math.Clamp(read() + direction * step, minimum, maximum));
			MarkChanged();
		}

		entries.Add(new(
			() => $"{ConfigFieldLabel(field)}: {format(read())}",
			() => Adjust(1),
			previousValue: () => Adjust(-1),
			nextValue: () => Adjust(1),
			description: () => ConfigFieldTooltip(field),
			role: "slider",
			adjustmentAnnouncement: () => format(read())));
	}

	private void IncreaseSpatialAudioItdStrength()
	{
		AdjustSpatialAudioItdStrength(0.05f);
	}

	private void DecreaseSpatialAudioItdStrength()
	{
		AdjustSpatialAudioItdStrength(-0.05f);
	}

	private void AdjustSpatialAudioItdStrength(float adjustment)
	{
		float adjusted = Math.Clamp(_pending.SpatialAudioItdStrengthMilliseconds + adjustment, 0f, 1f);
		_pending.SpatialAudioItdStrengthMilliseconds = MathF.Round(adjusted * 20f) / 20f;
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
		// A fresh instance already carries every property initializer, so restoring
		// from one covers options this screen has not been taught about yet.
		_pending = Clone(new AriadneClientConfig());
		_lastStatus = "Defaults restored as pending values.";
	}

	private void DiscardAndGoBack()
	{
		_pending = Clone(_active);
		// This leaves both the confirmation and editor, but is one user action.
		Controller.Back(playSound: false);
		Controller.Back();
	}

	private void MarkChanged()
	{
		_lastStatus = string.Empty;
	}

	private bool HasChanges()
	{
		// ObjectEquals on the two top-level ModConfig objects uses ordinary reference
		// equality and would therefore report a change even immediately after a
		// successful save. Compare the actual Ariadne setting members instead, while
		// retaining tModLoader's recursive comparison for member values.
		foreach (PropertyFieldWrapper member in ConfigManager.GetFieldsAndProperties(typeof(AriadneClientConfig)))
		{
			if (!member.CanWrite || member.MemberInfo.DeclaringType != typeof(AriadneClientConfig))
			{
				continue;
			}
			if (!ConfigManager.ObjectEquals(member.GetValue(_pending), member.GetValue(_active)))
			{
				return true;
			}
		}

		return false;
	}

	private string SaveDescription()
	{
		return WithStatus("Persist the pending Ariadne configuration values.");
	}

	private string WithStatus(string description)
	{
		return string.IsNullOrWhiteSpace(_lastStatus) ? description : $"{description} {_lastStatus}";
	}

	private static AriadneClientConfig Clone(AriadneClientConfig config)
	{
		return (AriadneClientConfig)ConfigManager.GeneratePopulatedClone(config);
	}

	private static string OnOff(bool value)
	{
		return value ? "On" : "Off";
	}

	private static string ItdAmount(float milliseconds)
	{
		return $"{milliseconds:0.00} milliseconds";
	}

	private static string ConfigFieldLabel(string fieldName)
	{
		return Language.GetTextValue(
			$"Mods.Ariadne.Configs.AriadneClientConfig.{fieldName}.Label");
	}

	private static string ConfigFieldTooltip(string fieldName)
	{
		return Language.GetTextValue(
			$"Mods.Ariadne.Configs.AriadneClientConfig.{fieldName}.Tooltip");
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
