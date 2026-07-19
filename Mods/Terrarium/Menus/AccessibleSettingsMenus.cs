#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.GameContent.UI.BigProgressBar;
using Terraria.GameInput;
using Terraria.Graphics.Light;
using Terraria.ID;
using Terraria.Localization;
using Terraria.UI;

namespace Terrarium.Menus;

internal sealed class AccessibleSettingsMenuState : AccessibleSettingsPageState
{
	internal AccessibleSettingsMenuState(AccessibleMenuController controller)
		: base(controller)
	{
	}

	protected override string Title => Lang.menu[14].Value;

	protected override void BuildEntries(List<AccessibleMenuEntry> entries)
	{
		entries.Add(new(() => Lang.menu[114].Value, () => Controller.Navigate(new AccessibleGeneralSettingsMenuState(Controller))));
		entries.Add(new(() => Lang.menu[210].Value, () => Controller.Navigate(new AccessibleInterfaceSettingsMenuState(Controller))));
		entries.Add(new(() => Lang.menu[63].Value, () => Controller.Navigate(new AccessibleVideoSettingsMenuState(Controller))));
		entries.Add(new(() => Lang.menu[65].Value, () => Controller.Navigate(new AccessibleAudioSettingsMenuState(Controller))));
		entries.Add(new(() => Lang.menu[218].Value, () => Controller.Navigate(new AccessibleCursorSettingsMenuState(Controller))));
		entries.Add(new(() => Lang.menu[219].Value, () => Controller.Navigate(new AccessibleControlsMenuState(Controller))));
		entries.Add(new(() => Lang.menu[103].Value, () => Controller.Navigate(new AccessibleLanguageMenuState(Controller))));
		entries.Add(new(() => Language.GetTextValue("tModLoader.tModLoaderSettings"), () => Controller.Navigate(new AccessibleTmlSettingsMenuState(Controller))));
	}
}

internal abstract class AccessibleSettingsPageState : AccessibleMenuState
{
	protected AccessibleSettingsPageState(AccessibleMenuController controller)
		: base(controller)
	{
	}

	protected override void GoBack()
	{
		Main.SaveSettings();
		PlayerInput.Save();
		base.GoBack();
	}

	protected static AccessibleMenuEntry Toggle(
		Func<string> label,
		Action toggle,
		Func<string>? description = null,
		Func<string>? adjustmentAnnouncement = null)
	{
		return new AccessibleMenuEntry(
			label,
			toggle,
			toggle,
			toggle,
			description,
			role: "toggle",
			adjustmentAnnouncement: adjustmentAnnouncement);
	}

	protected static string OnOff(bool value)
	{
		return Language.GetTextValue(value ? "GameUI.Enabled" : "GameUI.Disabled");
	}
}

internal sealed class AccessibleGeneralSettingsMenuState : AccessibleSettingsPageState
{
	internal AccessibleGeneralSettingsMenuState(AccessibleMenuController controller) : base(controller) { }

	protected override string Title => Lang.menu[114].Value;

	protected override void BuildEntries(List<AccessibleMenuEntry> entries)
	{
		entries.Add(Toggle(() => Main.autoSave ? Lang.menu[67].Value : Lang.menu[68].Value, () => Main.autoSave = !Main.autoSave, adjustmentAnnouncement: () => OnOff(Main.autoSave)));
		entries.Add(Toggle(() => Main.autoPause ? Lang.menu[69].Value : Lang.menu[70].Value, () => Main.autoPause = !Main.autoPause, adjustmentAnnouncement: () => OnOff(Main.autoPause)));
		entries.Add(Toggle(() => Main.mapEnabled ? Lang.menu[112].Value : Lang.menu[113].Value, () => Main.mapEnabled = !Main.mapEnabled, adjustmentAnnouncement: () => OnOff(Main.mapEnabled)));
		entries.Add(Toggle(
			() => Main.HidePassword ? Lang.menu[212].Value : Lang.menu[211].Value,
			() => Main.HidePassword = !Main.HidePassword,
			adjustmentAnnouncement: () => Main.HidePassword ? "Hidden" : "Visible"));
	}
}

internal sealed class AccessibleInterfaceSettingsMenuState : AccessibleSettingsPageState
{
	internal AccessibleInterfaceSettingsMenuState(AccessibleMenuController controller) : base(controller) { }

	protected override string Title => Lang.menu[210].Value;

	protected override void BuildEntries(List<AccessibleMenuEntry> entries)
	{
		entries.Add(Toggle(() => Main.showItemText ? Lang.menu[71].Value : Lang.menu[72].Value, () => Main.showItemText = !Main.showItemText, adjustmentAnnouncement: () => OnOff(Main.showItemText)));
		entries.Add(new(
			() => $"{Lang.menu[123].Value} {Lang.menu[124 + Main.invasionProgressMode].Value}",
			CycleInvasionProgress,
			CycleInvasionProgressBack,
			CycleInvasionProgress,
			role: "choice",
			adjustmentAnnouncement: () => Lang.menu[124 + Main.invasionProgressMode].Value));
		entries.Add(Toggle(() => Main.placementPreview ? Lang.menu[128].Value : Lang.menu[129].Value, () => Main.placementPreview = !Main.placementPreview, adjustmentAnnouncement: () => OnOff(Main.placementPreview)));
		entries.Add(Toggle(() => ItemSlot.Options.HighlightNewItems ? Lang.inter[117].Value : Lang.inter[116].Value, () => ItemSlot.Options.HighlightNewItems = !ItemSlot.Options.HighlightNewItems, adjustmentAnnouncement: () => OnOff(ItemSlot.Options.HighlightNewItems)));
		entries.Add(Toggle(() => Main.MouseShowBuildingGrid ? Lang.menu[229].Value : Lang.menu[230].Value, () => Main.MouseShowBuildingGrid = !Main.MouseShowBuildingGrid, adjustmentAnnouncement: () => OnOff(Main.MouseShowBuildingGrid)));
		entries.Add(Toggle(() => Main.GamepadDisableInstructionsDisplay ? Lang.menu[241].Value : Lang.menu[242].Value, () => Main.GamepadDisableInstructionsDisplay = !Main.GamepadDisableInstructionsDisplay, adjustmentAnnouncement: () => OnOff(!Main.GamepadDisableInstructionsDisplay)));
		entries.Add(new(
			() => Language.GetTextValue("UI.SelectMapBorder", Language.GetTextValue("UI.MinimapFrame_" + Main.MinimapFrameManagerInstance.ActiveSelectionKeyName)),
			Main.MinimapFrameManagerInstance.CycleSelection,
			previousValue: Main.MinimapFrameManagerInstance.CycleSelection,
			nextValue: Main.MinimapFrameManagerInstance.CycleSelection,
			role: "choice",
			adjustmentAnnouncement: () => Language.GetTextValue("UI.MinimapFrame_" + Main.MinimapFrameManagerInstance.ActiveSelectionKeyName)));
		entries.Add(new(
			() => Language.GetTextValue("UI.SelectHealthStyle", Main.ResourceSetsManager.ActiveSet.DisplayedName),
			Main.ResourceSetsManager.CycleResourceSet,
			previousValue: Main.ResourceSetsManager.CycleResourceSet,
			nextValue: Main.ResourceSetsManager.CycleResourceSet,
			role: "choice",
			adjustmentAnnouncement: () => Main.ResourceSetsManager.ActiveSet.DisplayedName));
		entries.Add(Toggle(
			() => Language.GetTextValue(BigProgressBarSystem.ShowText ? "UI.ShowBossLifeTextOn" : "UI.ShowBossLifeTextOff"),
			BigProgressBarSystem.ToggleShowText,
			adjustmentAnnouncement: () => OnOff(BigProgressBarSystem.ShowText)));
		entries.Add(Toggle(
			() => $"Opaque tooltip backgrounds: {OnOff(Main.SettingsEnabled_OpaqueBoxBehindTooltips)}",
			() => Main.SettingsEnabled_OpaqueBoxBehindTooltips = !Main.SettingsEnabled_OpaqueBoxBehindTooltips,
			adjustmentAnnouncement: () => OnOff(Main.SettingsEnabled_OpaqueBoxBehindTooltips)));
	}

	private static void CycleInvasionProgress() => Main.invasionProgressMode = (Main.invasionProgressMode + 1) % 3;

	private static void CycleInvasionProgressBack() => Main.invasionProgressMode = (Main.invasionProgressMode + 2) % 3;
}

internal sealed class AccessibleVideoSettingsMenuState : AccessibleSettingsPageState
{
	internal AccessibleVideoSettingsMenuState(AccessibleMenuController controller) : base(controller) { }

	protected override string Title => Lang.menu[63].Value;

	protected override void BuildEntries(List<AccessibleMenuEntry> entries)
	{
		entries.Add(Toggle(
			() => Main.graphics.IsFullScreen ? Lang.menu[49].Value : Lang.menu[50].Value,
			Main.ToggleFullScreen,
			adjustmentAnnouncement: () => OnOff(Main.graphics.IsFullScreen)));
		entries.Add(new(
			() => Lang.menu[247 + (int)Main.FrameSkipMode].Value,
			Main.CycleFrameSkipMode,
			previousValue: Main.CycleFrameSkipMode,
			nextValue: Main.CycleFrameSkipMode,
			role: "choice",
			adjustmentAnnouncement: () => Main.FrameSkipMode.ToString()));
		entries.Add(new(
			() => Language.GetTextValue("UI.LightMode_" + Lighting.Mode),
			Lighting.NextLightMode,
			previousValue: Lighting.NextLightMode,
			nextValue: Lighting.NextLightMode,
			role: "choice"));
		entries.Add(new(
			QualityLabel,
			NextQuality,
			PreviousQuality,
			NextQuality,
			role: "choice"));
		entries.Add(Toggle(() => Main.BackgroundEnabled ? Lang.menu[100].Value : Lang.menu[101].Value, () => Main.BackgroundEnabled = !Main.BackgroundEnabled, adjustmentAnnouncement: () => OnOff(Main.BackgroundEnabled)));
		entries.Add(Toggle(() => ChildSafety.Disabled ? Lang.menu[132].Value : Lang.menu[133].Value, () => ChildSafety.Disabled = !ChildSafety.Disabled, adjustmentAnnouncement: () => OnOff(ChildSafety.Disabled)));
		entries.Add(Toggle(() => Main.SettingsEnabled_MinersWobble ? Lang.menu[250].Value : Lang.menu[251].Value, () => Main.SettingsEnabled_MinersWobble = !Main.SettingsEnabled_MinersWobble, adjustmentAnnouncement: () => OnOff(Main.SettingsEnabled_MinersWobble)));
		entries.Add(Toggle(
			() => Language.GetTextValue(Main.SettingsEnabled_TilesSwayInWind ? "UI.TilesSwayInWindOn" : "UI.TilesSwayInWindOff"),
			() => Main.SettingsEnabled_TilesSwayInWind = !Main.SettingsEnabled_TilesSwayInWind,
			adjustmentAnnouncement: () => OnOff(Main.SettingsEnabled_TilesSwayInWind)));
		entries.Add(Toggle(
			() => Language.GetTextValue("GameUI.StormEffects", OnOff(Main.UseStormEffects)),
			() => Main.UseStormEffects = !Main.UseStormEffects,
			adjustmentAnnouncement: () => OnOff(Main.UseStormEffects)));
		entries.Add(Toggle(
			() => Language.GetTextValue("GameUI.HeatDistortion", OnOff(Main.UseHeatDistortion)),
			() => Main.UseHeatDistortion = !Main.UseHeatDistortion,
			adjustmentAnnouncement: () => OnOff(Main.UseHeatDistortion)));
		entries.Add(new(
			() => Language.GetTextValue("GameUI.WaveQuality", WaveQualityName()),
			NextWaveQuality,
			PreviousWaveQuality,
			NextWaveQuality,
			role: "choice"));
	}

	private static string QualityLabel() => Main.qaStyle switch
	{
		0 => Lang.menu[59].Value,
		1 => Lang.menu[60].Value,
		2 => Lang.menu[61].Value,
		_ => Lang.menu[62].Value,
	};

	private static void NextQuality() => Main.qaStyle = (Main.qaStyle + 1) % 4;

	private static void PreviousQuality() => Main.qaStyle = (Main.qaStyle + 3) % 4;

	private static void NextWaveQuality() => Main.WaveQuality = (Main.WaveQuality + 1) % 4;

	private static void PreviousWaveQuality() => Main.WaveQuality = (Main.WaveQuality + 3) % 4;

	private static string WaveQualityName() => Main.WaveQuality switch
	{
		1 => Language.GetTextValue("GameUI.QualityLow"),
		2 => Language.GetTextValue("GameUI.QualityMedium"),
		3 => Language.GetTextValue("GameUI.QualityHigh"),
		_ => Language.GetTextValue("GameUI.QualityOff"),
	};
}

internal sealed class AccessibleAudioSettingsMenuState : AccessibleSettingsPageState
{
	internal AccessibleAudioSettingsMenuState(AccessibleMenuController controller) : base(controller) { }

	protected override string Title => Lang.menu[65].Value;

	protected override void BuildEntries(List<AccessibleMenuEntry> entries)
	{
		entries.Add(Volume("Music volume", () => Main.musicVolume, value => Main.musicVolume = value));
		entries.Add(Volume("Sound volume", () => Main.soundVolume, value => Main.soundVolume = value));
		entries.Add(Volume("Ambient volume", () => Main.ambientVolume, value => Main.ambientVolume = value));
	}

	private static AccessibleMenuEntry Volume(string name, Func<float> get, Action<float> set)
	{
		void Down() => set(Math.Clamp(get() - 0.05f, 0f, 1f));
		void Up() => set(Math.Clamp(get() + 0.05f, 0f, 1f));
		return new AccessibleMenuEntry(
			() => $"{name}: {MathF.Round(get() * 100f)} percent",
			Up,
			Down,
			Up,
			role: "slider");
	}
}

internal sealed class AccessibleCursorSettingsMenuState : AccessibleSettingsPageState
{
	internal AccessibleCursorSettingsMenuState(AccessibleMenuController controller) : base(controller) { }

	protected override string Title => Lang.menu[218].Value;

	protected override void BuildEntries(List<AccessibleMenuEntry> entries)
	{
		entries.Add(Toggle(
			() => Main.cSmartCursorModeIsToggleAndNotHold ? Lang.menu[121].Value : Lang.menu[122].Value,
			() => Main.cSmartCursorModeIsToggleAndNotHold = !Main.cSmartCursorModeIsToggleAndNotHold));
		entries.Add(Toggle(
			() => Player.SmartCursorSettings.SmartAxeAfterPickaxe ? Lang.menu[214].Value : Lang.menu[213].Value,
			() => Player.SmartCursorSettings.SmartAxeAfterPickaxe = !Player.SmartCursorSettings.SmartAxeAfterPickaxe));
		entries.Add(Toggle(
			() => Player.SmartCursorSettings.SmartBlocksEnabled ? Lang.menu[215].Value : Lang.menu[216].Value,
			() => Player.SmartCursorSettings.SmartBlocksEnabled = !Player.SmartCursorSettings.SmartBlocksEnabled));
	}
}

internal sealed class AccessibleControlsMenuState : AccessibleSettingsPageState
{
	internal AccessibleControlsMenuState(AccessibleMenuController controller) : base(controller) { }

	protected override string Title => Lang.menu[219].Value;

	protected override void BuildEntries(List<AccessibleMenuEntry> entries)
	{
		entries.Add(new(() => "Gameplay keyboard bindings", () => Controller.Navigate(new AccessibleKeyBindingsMenuState(Controller, InputMode.Keyboard))));
		entries.Add(new(() => "Menu keyboard bindings", () => Controller.Navigate(new AccessibleKeyBindingsMenuState(Controller, InputMode.KeyboardUI))));
	}
}

internal sealed class AccessibleKeyBindingsMenuState : AccessibleSettingsPageState
{
	private readonly InputMode _inputMode;

	internal AccessibleKeyBindingsMenuState(AccessibleMenuController controller, InputMode inputMode) : base(controller)
	{
		_inputMode = inputMode;
	}

	protected override string Title => _inputMode == InputMode.Keyboard ? "Gameplay Keyboard Bindings" : "Menu Keyboard Bindings";

	protected override void BuildEntries(List<AccessibleMenuEntry> entries)
	{
		KeyConfiguration configuration = PlayerInput.CurrentProfile.InputModes[_inputMode];
		foreach (string trigger in configuration.KeyStatus.Keys.OrderBy(key => key, StringComparer.CurrentCultureIgnoreCase))
		{
			string capturedTrigger = trigger;
			entries.Add(new(
				() => $"{FriendlyTriggerName(capturedTrigger)}: {BindingText(configuration.KeyStatus[capturedTrigger])}",
				() => Controller.Navigate(new AccessibleKeyCaptureState(Controller, _inputMode, capturedTrigger)),
				description: () => $"Press Enter, then press a new key for {FriendlyTriggerName(capturedTrigger)}."));
		}
	}

	private static string BindingText(List<string> bindings) => bindings.Count == 0 ? "unbound" : string.Join(", ", bindings);

	private static string FriendlyTriggerName(string trigger)
	{
		return string.Concat(trigger.Select((character, index) => index > 0 && char.IsUpper(character) ? $" {character}" : character.ToString()));
	}
}

internal sealed class AccessibleKeyCaptureState : UIState
{
	private readonly AccessibleMenuController _controller;
	private readonly InputMode _inputMode;
	private readonly string _trigger;
	private KeyboardState _previousKeyboard;

	internal AccessibleKeyCaptureState(AccessibleMenuController controller, InputMode inputMode, string trigger)
	{
		_controller = controller;
		_inputMode = inputMode;
		_trigger = trigger;
	}

	public override void OnActivate()
	{
		_previousKeyboard = Keyboard.GetState();
		TerrariumMod.ScreenReader.Output($"Press a key for {_trigger}. Press Escape to cancel, Delete to clear the binding, or F1 for contextual help.");
	}

	public override void Update(GameTime gameTime)
	{
		base.Update(gameTime);
		KeyboardState keyboard = Keyboard.GetState();
		Keys[] newlyPressed = keyboard.GetPressedKeys().Where(key => _previousKeyboard.IsKeyUp(key)).ToArray();
		if (newlyPressed.Length == 0)
		{
			_previousKeyboard = keyboard;
			return;
		}

		Keys key = newlyPressed[0];
		if (key == Keys.F1)
		{
			SoundEngine.PlaySound(SoundID.MenuOpen);
			_controller.Navigate(new AccessibleContextHelpMenuState(
				_controller,
				$"Bind {_trigger}",
				[
					new("Any key", $"Press a key to replace the current keyboard binding for {_trigger}."),
					new("Delete or Backspace", "Clear the binding so this control is unbound."),
					new("Escape", "Cancel and keep the existing binding."),
					new("F1", "Open this help screen. F1 is reserved for contextual help while using Terrarium menus."),
				]));
			return;
		}
		if (key == Keys.Escape)
		{
			_controller.Back();
			return;
		}

		List<string> bindings = PlayerInput.CurrentProfile.InputModes[_inputMode].KeyStatus[_trigger];
		bindings.Clear();
		if (key != Keys.Delete && key != Keys.Back)
		{
			bindings.Add(key.ToString());
			TerrariumMod.ScreenReader.Output($"{_trigger} bound to {key}.");
		}
		else
		{
			TerrariumMod.ScreenReader.Output($"{_trigger} is now unbound.");
		}
		PlayerInput.Save();
		_controller.Back();
	}
}

internal sealed class AccessibleLanguageMenuState : AccessibleSettingsPageState
{
	private static readonly string[] LanguageKeys =
	[
		"Language.English",
		"Language.German",
		"Language.Italian",
		"Language.French",
		"Language.Spanish",
		"Language.Russian",
		"Language.Chinese",
		"Language.Portuguese",
		"Language.Polish",
	];

	internal AccessibleLanguageMenuState(AccessibleMenuController controller) : base(controller) { }

	protected override string Title => Lang.menu[103].Value;

	protected override void BuildEntries(List<AccessibleMenuEntry> entries)
	{
		for (int index = 0; index < LanguageKeys.Length; index++)
		{
			int languageId = index + 1;
			string key = LanguageKeys[index];
			entries.Add(new(
				() => Language.GetTextValue(key),
				() => SetLanguage(languageId),
				role: "choice"));
		}
	}

	private void SetLanguage(int languageId)
	{
		LanguageManager.Instance.SetLanguage(languageId);
		Main.SaveSettings();
		RebuildEntries(announceSelection: true);
	}
}

internal sealed class AccessibleTmlSettingsMenuState : AccessibleSettingsPageState
{
	private static readonly Type ModLoaderType = typeof(Terraria.ModLoader.ModLoader);
	private static readonly Type? ModNetType = ModLoaderType.Assembly.GetType("Terraria.ModLoader.ModNet");

	internal AccessibleTmlSettingsMenuState(AccessibleMenuController controller) : base(controller) { }

	protected override string Title => Language.GetTextValue("tModLoader.tModLoaderSettings");

	protected override void BuildEntries(List<AccessibleMenuEntry> entries)
	{
		AddBoolean(entries, ModNetType, "downloadModsFromServers", "tModLoader.DownloadFromServers");
		AddBoolean(entries, ModLoaderType, "autoReloadRequiredModsLeavingModsScreen", "tModLoader.AutomaticallyReloadRequiredModsLeavingModsScreen");
		AddBoolean(entries, ModLoaderType, "removeForcedMinimumZoom", "tModLoader.RemoveForcedMinimumZoom");
		AddBoolean(entries, ModLoaderType, "notifyNewMainMenuThemes", "tModLoader.ShowModMenuNotifications");
		AddBoolean(entries, ModLoaderType, "showNewUpdatedModsInfo", "tModLoader.ShowNewUpdatedModsInfo");
		AddBoolean(entries, ModLoaderType, "showConfirmationWindowWhenEnableDisableAllMods", "tModLoader.ShowConfirmationWindowWhenEnableDisableAllMods");
	}

	private static void AddBoolean(List<AccessibleMenuEntry> entries, Type? owner, string fieldName, string localizationPrefix)
	{
		System.Reflection.FieldInfo? field = owner?.GetField(fieldName, System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
		if (field?.FieldType != typeof(bool))
		{
			return;
		}

		bool Get() => (bool)(field.GetValue(null) ?? false);
		void ToggleValue() => field.SetValue(null, !Get());
		entries.Add(Toggle(
			() => Language.GetTextValue(localizationPrefix + (Get() ? "Yes" : "No")),
			ToggleValue));
	}
}
