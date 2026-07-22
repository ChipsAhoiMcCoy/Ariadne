#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent.Bestiary;
using Terraria.GameContent.Creative;
using Terraria.GameContent.ItemDropRules;
using Terraria.GameContent.UI;
using Terraria.GameContent.UI.Elements;
using Terraria.GameContent.UI.States;
using Terraria.GameInput;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;
using Terraria.UI;
using Terrarium.Accessibility;

namespace Terrarium.Menus;

/// <summary>
/// Provides a semantic keyboard layer for stock and mod-provided UIState screens
/// that Terrarium has not replaced with a purpose-built accessible state.
/// </summary>
[Autoload(Side = ModSide.Client)]
internal sealed class AccessibleExternalUISystem : ModSystem
{
	private const int IngameBestiaryHierarchyLevel = 2;
	private static readonly FieldInfo? CreativeUiStateField = typeof(CreativeUI).GetField("_uiState", BindingFlags.Instance | BindingFlags.NonPublic);
	private static UIState? _returnToInventoryWhenClosed;
	private static int? _nextIngameHierarchyLevel;
	private static int? _nextJourneyPowerCategory;
	private readonly AccessibleExternalUIController _controller = new();

	internal static void OpenIngameStateFromInventory(UIState state)
	{
		_returnToInventoryWhenClosed = state;
		IngameFancyUI.OpenUIState(state);
	}

	internal static void SetNextIngameHierarchyLevel(int level)
	{
		_nextIngameHierarchyLevel = level;
	}

	internal static void SetNextJourneyPowerCategory(int category)
	{
		_nextJourneyPowerCategory = category;
	}

	internal static bool TryToggleJourneyInfectionSpread()
	{
		return CreativeUiStateField?.GetValue(Main.CreativeMenu) is UICreativePowersMenu journeyPowers &&
			AccessibleExternalUIController.TryToggleJourneyInfectionSpread(journeyPowers);
	}

	internal static bool TryGetJourneyEnemyDifficultySlider(out Func<float> getValue, out Action<float> setValue)
	{
		if (CreativeUiStateField?.GetValue(Main.CreativeMenu) is UICreativePowersMenu journeyPowers)
		{
			return AccessibleExternalUIController.TryGetJourneyEnemyDifficultySlider(journeyPowers, out getValue, out setValue);
		}

		getValue = null!;
		setValue = null!;
		return false;
	}

	public override void PostUpdateInput()
	{
		if (Main.gameMenu)
		{
			_returnToInventoryWhenClosed = null;
			_nextIngameHierarchyLevel = null;
			_nextJourneyPowerCategory = null;
		}
		else if (_returnToInventoryWhenClosed is not null && Main.InGameUI.CurrentState is null)
		{
			_returnToInventoryWhenClosed = null;
			Main.playerInventory = true;
		}

		UIState? state;
		if (Main.gameMenu)
		{
			state = Main.MenuUI.CurrentState;
		}
		else if (Main.InGameUI.CurrentState is not null)
		{
			state = Main.InGameUI.CurrentState;
		}
		else if (Main.playerInventory && Main.CreativeMenu.Enabled && !Main.CreativeMenu.Blocked)
		{
			state = CreativeUiStateField?.GetValue(Main.CreativeMenu) as UIState;
		}
		else
		{
			state = null;
		}

		if (state is null || IsTerrariumState(state))
		{
			_controller.Deactivate();
			_nextIngameHierarchyLevel = null;
			return;
		}

		int? hierarchyLevel = Main.gameMenu
			? null
			: _nextIngameHierarchyLevel ?? (state is UIBestiaryTest ? IngameBestiaryHierarchyLevel : null);
		int? journeyPowerCategory = state is UICreativePowersMenu ? _nextJourneyPowerCategory : null;
		_controller.Update(state, hierarchyLevel, journeyPowerCategory);
		if (journeyPowerCategory is not null)
		{
			_nextJourneyPowerCategory = null;
		}
	}

	public override void Unload()
	{
		_returnToInventoryWhenClosed = null;
		_nextIngameHierarchyLevel = null;
		_nextJourneyPowerCategory = null;
		_controller.Deactivate();
	}

	private static bool IsTerrariumState(UIState state)
	{
		return state.GetType().Namespace?.StartsWith("Terrarium", StringComparison.Ordinal) == true;
	}
}

internal sealed class AccessibleExternalUIController
{
	private const int PageSize = 10;
	private const long RebuildIntervalMilliseconds = 350;
	private const long StatusAnnouncementIntervalMilliseconds = 1_000;
	private const long NavigationRepeatDelayMilliseconds = 450;
	private const long NavigationRepeatIntervalMilliseconds = 85;
	private const int MaximumSpokenLabelLength = 420;

	private static readonly BindingFlags InstanceMembers = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
	private static readonly FieldInfo?[] InteractionFields =
	[
		typeof(UIElement).GetField("m_OnLeftMouseDown", InstanceMembers)!,
		typeof(UIElement).GetField("m_OnLeftMouseUp", InstanceMembers)!,
		typeof(UIElement).GetField("m_OnLeftClick", InstanceMembers)!,
		typeof(UIElement).GetField("m_OnRightMouseDown", InstanceMembers)!,
		typeof(UIElement).GetField("m_OnRightMouseUp", InstanceMembers)!,
		typeof(UIElement).GetField("m_OnRightClick", InstanceMembers)!,
	];
	private static readonly string[] SemanticMemberNames =
	[
		"Text", "HoverText", "AltHoverText", "Tooltip", "Description", "DisplayName",
		"DisplayNameClean", "FriendlyName", "Title", "Name", "HintText", "CurrentString",
		"OptionValue", "ModName", "Author",
	];
	private static readonly Dictionary<Type, MemberInfo[]> SemanticMembersByType = [];
	private static readonly Regex ColorTagPattern = new(@"\[c/[0-9A-Fa-f]{6}:(.*?)\]", RegexOptions.Compiled);
	private static readonly Regex ChatTagPattern = new(@"\[[^\]]+:[^\]]*\]", RegexOptions.Compiled);

	private readonly List<AccessibleExternalControl> _controls = [];
	private UIState? _state;
	private UIElement? _hoveredElement;
	private KeyboardState _previousKeyboard;
	private Keys? _repeatingKey;
	private long _nextRepeatAt;
	private long _nextRebuildAt;
	private long _nextStatusAnnouncementAt;
	private int _selectedIndex;
	private string _lastSelectionState = string.Empty;
	private string _lastStatus = string.Empty;
	private string? _lastEditValue;
	private int? _hierarchyLevel;
	private int? _journeyCategoryOpenedFromInventory;

	internal void Update(UIState state, int? hierarchyLevel, int? journeyCategoryOpenedFromInventory = null)
	{
		KeyboardState keyboard = Keyboard.GetState();
		long now = Environment.TickCount64;
		if (!ReferenceEquals(state, _state) || journeyCategoryOpenedFromInventory is not null)
		{
			Activate(state, keyboard, now, hierarchyLevel, journeyCategoryOpenedFromInventory);
			return;
		}

		if (now >= _nextRebuildAt)
		{
			RebuildControls(preserveSelection: true);
			_nextRebuildAt = now + RebuildIntervalMilliseconds;
		}

		if (PlayerInput.WritingText)
		{
			MonitorTextEditing();
			_previousKeyboard = keyboard;
			return;
		}

		_lastEditValue = null;
		if (_repeatingKey is Keys repeatingKey && keyboard.IsKeyUp(repeatingKey))
		{
			_repeatingKey = null;
		}

		ConsumeNavigationTriggers();
		ConsumeBoundLetterTriggers(keyboard);
		bool handled = HandleInput(keyboard, now);
		if (!handled)
		{
			AnnounceChangedSelection();
			AnnounceChangedStatus(now);
		}

		_previousKeyboard = keyboard;
	}

	internal void Deactivate()
	{
		if (_hoveredElement is not null)
		{
			TryMouseOut(_hoveredElement);
		}

		_state = null;
		_hoveredElement = null;
		_controls.Clear();
		_selectedIndex = 0;
		_repeatingKey = null;
		_lastSelectionState = string.Empty;
		_lastStatus = string.Empty;
		_lastEditValue = null;
		_hierarchyLevel = null;
		_journeyCategoryOpenedFromInventory = null;
	}

	private void Activate(UIState state, KeyboardState keyboard, long now, int? hierarchyLevel, int? journeyCategoryOpenedFromInventory)
	{
		Deactivate();
		_state = state;
		_hierarchyLevel = hierarchyLevel;
		if (state is UICreativePowersMenu journeyPowers)
		{
			ResetJourneyPowerHierarchy(journeyPowers);
			if (journeyCategoryOpenedFromInventory is int category && TryOpenJourneyPowerCategory(journeyPowers, category))
			{
				_journeyCategoryOpenedFromInventory = category;
			}
		}
		if (!Main.gameMenu && state is UIBestiaryTest)
		{
			SoundEngine.PlaySound(SoundID.MenuOpen);
		}
		_previousKeyboard = keyboard;
		_nextRebuildAt = now + RebuildIntervalMilliseconds;
		_nextStatusAnnouncementAt = now + StatusAnnouncementIntervalMilliseconds;
		RebuildControls(preserveSelection: false);
		_lastStatus = DescribeStatus(state);

		string title = DescribeScreenTitle(state);
		string hierarchy = _hierarchyLevel is int level ? $" Level {level}." : string.Empty;
		if (_controls.Count == 0)
		{
			string status = string.IsNullOrWhiteSpace(_lastStatus) ? "No actionable controls were discovered." : _lastStatus;
			TerrariumMod.ScreenReader.Output($"{title}. {status}{hierarchy} Escape uses this screen's normal back or cancel action. F1 reads accessible screen controls.");
			return;
		}

		FocusSelectedControl();
		_lastSelectionState = GetSelectionState();
		TerrariumMod.ScreenReader.Output(
			$"{title}. {DescribeSelection()}{hierarchy} " +
			"Use Up and Down Arrow keys to move, Left and Right Arrow keys to adjust sliders or navigate the menu tree, Enter to activate, Shift Enter for the alternate action, letter keys to jump by name, Control R for details, Escape for the screen's normal back action, and F1 for help.");
	}

	private bool HandleInput(KeyboardState keyboard, long now)
	{
		if (Pressed(keyboard, Keys.F1))
		{
			ReadHelp();
			return true;
		}

		if (_controls.Count == 0)
		{
			return false;
		}

		if (NavigationTriggered(keyboard, Keys.Up, now))
		{
			MoveSelection(-1);
		}
		else if (NavigationTriggered(keyboard, Keys.Down, now))
		{
			MoveSelection(1);
		}
		else if (Pressed(keyboard, Keys.Home))
		{
			SetSelection(0);
		}
		else if (Pressed(keyboard, Keys.End))
		{
			SetSelection(_controls.Count - 1);
		}
		else if (Pressed(keyboard, Keys.PageUp))
		{
			SetSelection(Math.Max(0, _selectedIndex - PageSize));
		}
		else if (Pressed(keyboard, Keys.PageDown))
		{
			SetSelection(Math.Min(_controls.Count - 1, _selectedIndex + PageSize));
		}
		else if (NavigationTriggered(keyboard, Keys.Left, now))
		{
			if (!TryAdjustSelectedSlider(-0.05f))
			{
				NavigateBack();
			}
		}
		else if (NavigationTriggered(keyboard, Keys.Right, now))
		{
			if (!TryAdjustSelectedSlider(0.05f) && CanOpenSelectedSubmenu())
			{
				ActivateSelection(secondary: false);
			}
		}
		else if (Pressed(keyboard, Keys.Enter))
		{
			ActivateSelection(IsShiftDown(keyboard));
			Main.chatRelease = false;
		}
		else if (IsControlDown(keyboard) && Pressed(keyboard, Keys.R))
		{
			TerrariumMod.ScreenReader.Output(_controls[_selectedIndex].Details());
		}
		else if (FirstLetterNavigator.TryGetPressedLetter(keyboard, _previousKeyboard, out _, out char letter))
		{
			NavigateByFirstLetter(letter);
		}
		else
		{
			return false;
		}

		return true;
	}

	private void RebuildControls(bool preserveSelection)
	{
		if (_state is null)
		{
			return;
		}

		string? selectedId = preserveSelection && _controls.Count > 0
			? _controls[Math.Clamp(_selectedIndex, 0, _controls.Count - 1)].StableId
			: null;

		_controls.Clear();
		if (_state is UIBestiaryTest bestiary)
		{
			BuildBestiaryControls(bestiary);
			RestoreSelection(selectedId);
			return;
		}

		foreach (UIElement element in EnumerateDescendants(_state))
		{
			if (!IsActionable(element) || ShouldHideControl(_state, element))
			{
				continue;
			}

			string label = DescribeElement(element, includeAllText: false);
			string details = DescribeElement(element, includeAllText: true);
			_controls.Add(new AccessibleExternalControl(
				element,
				GetElementStableId(element),
				label,
				() => string.IsNullOrWhiteSpace(details) ? label : details,
				DescribeRole(element)));
		}

		if (_state is UICreativePowersMenu)
		{
			ApplyJourneyPowerHierarchy();
		}

		RestoreSelection(selectedId);
	}

	private void RestoreSelection(string? selectedId)
	{
		if (_controls.Count == 0)
		{
			_selectedIndex = 0;
			_lastSelectionState = string.Empty;
			return;
		}

		int preservedIndex = selectedId is null
			? -1
			: _controls.FindIndex(control => control.StableId == selectedId);
		_selectedIndex = preservedIndex >= 0
			? preservedIndex
			: 0;
		if (selectedId is not null && preservedIndex < 0)
		{
			FocusSelectedControl();
		}
	}

	private void BuildBestiaryControls(UIBestiaryTest bestiary)
	{
		List<BestiaryEntry> entries = GetFieldValue(bestiary.GetType(), bestiary, "_workingSetEntries") as List<BestiaryEntry> ?? [];
		for (int index = 0; index < entries.Count; index++)
		{
			BestiaryEntry entry = entries[index];
			string label = DescribeBestiaryEntryLabel(entry);
			string? cachedDetails = null;
			Func<string> getDetails = () => cachedDetails ??= DescribeBestiaryEntry(entry).Details;
			int capturedIndex = index;
			_controls.Add(new AccessibleExternalControl(
				null,
				$"bestiary-entry-{capturedIndex}-{label}",
				label,
				getDetails,
				"entry",
				_ => SelectBestiaryEntry(bestiary, entry, capturedIndex),
				() => FocusBestiaryEntry(bestiary, entry, capturedIndex)));
		}
	}

	private static (string Label, string Details) DescribeBestiaryEntry(BestiaryEntry entry)
	{
		BestiaryUICollectionInfo collectionInfo = GetBestiaryCollectionInfo(entry);
		int? displayIndex = entry.Info.OfType<IBestiaryEntryDisplayIndex>().Select(info => (int?)info.BestiaryDisplayIndex).FirstOrDefault();
		string label;
		try
		{
			label = entry.Icon?.GetHoverText(collectionInfo) ?? string.Empty;
		}
		catch
		{
			label = string.Empty;
		}
		if (string.IsNullOrWhiteSpace(label) || label.Trim('?').Length == 0)
		{
			label = displayIndex is int knownIndex ? $"Unknown entry {knownIndex}" : "Unknown entry";
		}

		List<string> details = [label, DescribeBestiaryUnlockState(collectionInfo.UnlockState)];
		List<string> tags = [];
		int unidentifiedDropCount = 0;
		foreach (IBestiaryInfoElement infoElement in entry.Info)
		{
			try
			{
				if (infoElement is IUpdateBeforeSorting updateBeforeSorting)
				{
					updateBeforeSorting.UpdateBeforeSorting();
				}

				switch (infoElement)
				{
					case NPCStatsReportInfoElement stats when collectionInfo.UnlockState >= BestiaryEntryUnlockState.CanShowStats_2:
						details.Add(DescribeBestiaryStats(stats));
						continue;
					case NPCStatsReportInfoElement:
						continue;
					case NPCKillCounterInfoElement killCounter:
						if (TryDescribeBestiaryKillCount(killCounter, out string killCount))
						{
							details.Add(killCount);
						}
						continue;
					case ItemDropBestiaryInfoElement itemDrop:
						if (collectionInfo.UnlockState == BestiaryEntryUnlockState.CanShowStats_2 &&
							TryGetVisibleBestiaryDropInfo(itemDrop, out _))
						{
							unidentifiedDropCount++;
						}
						else if (TryDescribeBestiaryDrop(itemDrop, collectionInfo, out string drop))
						{
							details.Add(drop);
						}
						continue;
					case ItemFromCatchingNPCBestiaryInfoElement catchItem:
						if (TryDescribeBestiaryCatchItem(catchItem, collectionInfo, out string caughtAs))
						{
							details.Add(caughtAs);
						}
						continue;
					case BossBestiaryInfoElement when collectionInfo.UnlockState >= BestiaryEntryUnlockState.CanShowPortraitOnly_1:
						details.Add("Boss");
						continue;
					case RareSpawnBestiaryInfoElement rareSpawn when collectionInfo.UnlockState >= BestiaryEntryUnlockState.CanShowPortraitOnly_1:
						details.Add($"Rare enemy, rarity level {rareSpawn.RarityLevel}");
						continue;
				}

				UIElement? element = infoElement.ProvideUIElement(collectionInfo);
				if (element is null)
				{
					continue;
				}
				List<string> text = [];
				CollectText(element, text);
				string semanticText = JoinSpokenSentences(text);
				if (infoElement is IFilterInfoProvider)
				{
					string? tag = infoElement is IProvideSearchFilterString searchProvider
						? searchProvider.GetSearchString(ref collectionInfo)
						: null;
					AddText(tags, string.IsNullOrWhiteSpace(tag) ? semanticText : tag);
					continue;
				}
				if (!string.IsNullOrWhiteSpace(semanticText) && !semanticText.Equals(label, StringComparison.OrdinalIgnoreCase))
				{
					details.Add(semanticText);
				}
			}
			catch
			{
				// A mod-provided Bestiary information element must not break the entire list.
			}
		}
		if (unidentifiedDropCount > 0)
		{
			details.Add($"{unidentifiedDropCount} {(unidentifiedDropCount == 1 ? "drop remains" : "drops remain")} unidentified");
		}
		if (tags.Count > 0)
		{
			details.Add($"Bestiary tags: {JoinSpokenList(tags)}");
		}

		return (label, JoinSpokenSentences(details));
	}

	private static string DescribeBestiaryEntryLabel(BestiaryEntry entry)
	{
		BestiaryUICollectionInfo collectionInfo = GetBestiaryCollectionInfo(entry);
		string label;
		try
		{
			label = entry.Icon?.GetHoverText(collectionInfo) ?? string.Empty;
		}
		catch
		{
			label = string.Empty;
		}
		if (!string.IsNullOrWhiteSpace(label) && label.Trim('?').Length > 0)
		{
			return label;
		}

		int? displayIndex = entry.Info.OfType<IBestiaryEntryDisplayIndex>().Select(info => (int?)info.BestiaryDisplayIndex).FirstOrDefault();
		return displayIndex is int knownIndex ? $"Unknown entry {knownIndex}" : "Unknown entry";
	}

	private static BestiaryUICollectionInfo GetBestiaryCollectionInfo(BestiaryEntry entry)
	{
		BestiaryUICollectionInfo collectionInfo;
		try
		{
			collectionInfo = entry.UIInfoProvider?.GetEntryUICollectionInfo() ?? default;
		}
		catch
		{
			collectionInfo = default;
		}
		collectionInfo.OwnerEntry = entry;
		return collectionInfo;
	}

	private static string DescribeBestiaryUnlockState(BestiaryEntryUnlockState unlockState)
	{
		return unlockState switch
		{
			BestiaryEntryUnlockState.NotKnownAtAll_0 => "Not yet discovered",
			BestiaryEntryUnlockState.CanShowPortraitOnly_1 => "Portrait unlocked; stats and drops remain unknown",
			BestiaryEntryUnlockState.CanShowStats_2 => "Stats unlocked; drop identities remain unknown",
			BestiaryEntryUnlockState.CanShowDropsWithoutDropRates_3 => "Drops unlocked; drop rates remain unknown",
			BestiaryEntryUnlockState.CanShowDropsWithDropRates_4 => "Fully unlocked, including drop rates",
			_ => $"Discovery level: {Humanize(unlockState.ToString())}",
		};
	}

	private static string DescribeBestiaryStats(NPCStatsReportInfoElement stats)
	{
		string knockbackCategory = stats.KnockbackResist > 0.8f
			? Language.GetTextValue("BestiaryInfo.KnockbackHigh")
			: stats.KnockbackResist > 0.4f
				? Language.GetTextValue("BestiaryInfo.KnockbackMedium")
				: stats.KnockbackResist > 0f
					? Language.GetTextValue("BestiaryInfo.KnockbackLow")
					: Language.GetTextValue("BestiaryInfo.KnockbackNone");
		string value = stats.MonetaryValue > 0f ? $", value {DescribeCoinValue((long)stats.MonetaryValue)}" : string.Empty;
		return $"Stats: life {stats.LifeMax}, attack {stats.Damage}, defense {stats.Defense}, knockback resistance {stats.KnockbackResist:P0}, {knockbackCategory}{value}";
	}

	private static string DescribeCoinValue(long value)
	{
		int[] coins = Utils.CoinsSplit(Math.Max(0L, value));
		string[] names = ["copper", "silver", "gold", "platinum"];
		List<string> parts = [];
		for (int index = coins.Length - 1; index >= 0; index--)
		{
			if (coins[index] > 0)
			{
				parts.Add($"{coins[index]} {names[index]}");
			}
		}
		return parts.Count > 0 ? string.Join(", ", parts) : "0 copper";
	}

	private static bool TryDescribeBestiaryKillCount(NPCKillCounterInfoElement killCounter, out string description)
	{
		if (GetFieldValue(killCounter.GetType(), killCounter, "_instance") is NPC npc &&
			Main.BestiaryTracker.Kills.GetKillCount(npc) is int count && count > 0)
		{
			description = $"Defeated {count}";
			return true;
		}
		description = string.Empty;
		return false;
	}

	private static bool TryDescribeBestiaryDrop(
		ItemDropBestiaryInfoElement itemDrop,
		BestiaryUICollectionInfo collectionInfo,
		out string description)
	{
		description = string.Empty;
		if (collectionInfo.UnlockState < BestiaryEntryUnlockState.CanShowDropsWithoutDropRates_3 ||
			!TryGetVisibleBestiaryDropInfo(itemDrop, out DropRateInfo dropInfo) ||
			!ContentSamples.ItemsByType.TryGetValue(dropInfo.itemId, out Item? item))
		{
			return false;
		}

		List<string> conditions = [];
		if (dropInfo.conditions is not null)
		{
			foreach (IItemDropRuleCondition condition in dropInfo.conditions)
			{
				AddText(conditions, condition.GetConditionDescription());
			}
		}

		string quantity = dropInfo.stackMin != dropInfo.stackMax
			? $", quantity {dropInfo.stackMin} to {dropInfo.stackMax}"
			: dropInfo.stackMin > 1 ? $", quantity {dropInfo.stackMin}" : string.Empty;
		string rate = string.Empty;
		if (collectionInfo.UnlockState >= BestiaryEntryUnlockState.CanShowDropsWithDropRates_4)
		{
			string format = dropInfo.dropRate < 0.001f ? "P4" : "P";
			string percentage = dropInfo.dropRate == 1f ? "100%" : Utils.PrettifyPercentDisplay(dropInfo.dropRate, format);
			rate = $", drop chance {percentage}";
		}
		string conditionText = conditions.Count > 0
			? $", conditions: {JoinSpokenList(conditions)}"
			: string.Empty;
		description = $"Drops {item.Name}{quantity}{rate}{conditionText}";
		return true;
	}

	private static bool TryGetVisibleBestiaryDropInfo(ItemDropBestiaryInfoElement itemDrop, out DropRateInfo dropInfo)
	{
		if (GetFieldValue(itemDrop.GetType(), itemDrop, "_droprateInfo") is not DropRateInfo found)
		{
			dropInfo = default;
			return false;
		}
		if (found.conditions is not null && found.conditions.Any(condition => !condition.CanShowItemDropInUI()))
		{
			dropInfo = default;
			return false;
		}
		dropInfo = found;
		return true;
	}

	private static bool TryDescribeBestiaryCatchItem(
		ItemFromCatchingNPCBestiaryInfoElement catchItem,
		BestiaryUICollectionInfo collectionInfo,
		out string description)
	{
		description = string.Empty;
		if (collectionInfo.UnlockState < BestiaryEntryUnlockState.CanShowDropsWithoutDropRates_3 ||
			GetFieldValue(catchItem.GetType(), catchItem, "_itemType") is not int itemType ||
			!ContentSamples.ItemsByType.TryGetValue(itemType, out Item? item))
		{
			return false;
		}
		description = $"Catch item: {item.Name}";
		return true;
	}

	private static void CollectText(UIElement root, List<string> destination)
	{
		if (root is UIText rootText)
		{
			AddText(destination, rootText.Text);
		}
		else
		{
			AddSemanticMemberText(root, destination);
		}

		foreach (UIElement child in EnumerateDescendants(root))
		{
			if (child is UIText uiText)
			{
				AddText(destination, uiText.Text);
			}
			else
			{
				AddSemanticMemberText(child, destination);
			}
		}
	}

	private static string JoinSpokenSentences(IEnumerable<string> values)
	{
		List<string> sentences = [];
		HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
		foreach (string value in values)
		{
			string sentence = NormalizeSpokenSentence(value);
			if (!string.IsNullOrWhiteSpace(sentence) && seen.Add(sentence))
			{
				sentences.Add(sentence);
			}
		}
		return string.Join(" ", sentences);
	}

	private static string JoinSpokenList(IEnumerable<string> values)
	{
		List<string> items = [];
		HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
		foreach (string value in values)
		{
			string item = value.Trim().TrimEnd('.', ',', ';', ':').TrimEnd();
			if (!string.IsNullOrWhiteSpace(item) && seen.Add(item))
			{
				items.Add(item);
			}
		}
		return string.Join(", ", items);
	}

	private static string NormalizeSpokenSentence(string value)
	{
		string sentence = value.Trim();
		while (sentence.EndsWith("..", StringComparison.Ordinal))
		{
			sentence = sentence[..^1].TrimEnd();
		}
		if (sentence.Length >= 2 && sentence[^1] == '.' && sentence[^2] is '?' or '!' or '…')
		{
			sentence = sentence[..^1].TrimEnd();
		}
		if (sentence.Length > 0 && sentence[^1] is not ('.' or '?' or '!' or '…'))
		{
			sentence += ".";
		}
		return sentence;
	}

	private void SelectBestiaryEntry(UIBestiaryTest bestiary, BestiaryEntry entry, int entryIndex)
	{
		FocusBestiaryEntry(bestiary, entry, entryIndex);
		TerrariumMod.ScreenReader.Output(DescribeBestiaryEntry(entry).Details);
	}

	private static void FocusBestiaryEntry(UIBestiaryTest bestiary, BestiaryEntry entry, int entryIndex)
	{
		ShowBestiaryEntryOnNativeGrid(bestiary, entryIndex);
		UIBestiaryEntryInfoPage? infoPage = GetFieldValue(bestiary.GetType(), bestiary, "_infoSpace") as UIBestiaryEntryInfoPage;
		if (infoPage is not null)
		{
			infoPage.FillInfoForEntry(entry, new ExtraBestiaryInfoPageInformation
			{
				BestiaryProgressReport = bestiary.GetUnlockProgress(),
			});
		}
	}

	private static void ShowBestiaryEntryOnNativeGrid(UIBestiaryTest bestiary, int entryIndex)
	{
		if (GetFieldValue(bestiary.GetType(), bestiary, "_entryGrid") is not UIBestiaryEntryGrid grid)
		{
			return;
		}

		int currentIndex = GetFieldValue(grid.GetType(), grid, "_atEntryIndex") as int? ?? 0;
		grid.OffsetLibrary(entryIndex - currentIndex);
	}

	private void ApplyJourneyPowerHierarchy()
	{
		int? rootOption = _controls
			.Where(control =>
			control.Element is not null &&
			GetStripDepth(control.Element) == 0 &&
			GetOptionValueType(control.Element) == typeof(int) &&
			IsGroupOptionSelected(control.Element))
			.Select(control => control.Element!.GetType().GetProperty("OptionValue", InstanceMembers)?.GetValue(control.Element) as int?)
			.FirstOrDefault(option => option is not null);
		ApplyJourneyPowerStripSemantics(rootOption);
		if (rootOption is null)
		{
			_controls.RemoveAll(control => control.Element is null || GetStripDepth(control.Element) != 0);
			SuppressJourneyPowerRoleVerbiage();
			return;
		}

		// Slider controls are projected onto their strip-one category buttons below,
		// so Journey navigation never needs the native strip-two slider level.
		_controls.RemoveAll(control => control.Element is null || GetStripDepth(control.Element) != 1);
		SuppressJourneyPowerRoleVerbiage();
	}

	private void ApplyJourneyPowerStripSemantics(int? rootOption)
	{
		if (rootOption is null)
		{
			return;
		}

		int[] stripOneControls = Enumerable.Range(0, _controls.Count)
			.Where(index => _controls[index].Element is UIElement element && GetStripDepth(element) == 1)
			.ToArray();
		for (int position = 0; position < stripOneControls.Length; position++)
		{
			int controlIndex = stripOneControls[position];
			UIElement element = _controls[controlIndex].Element!;
			string? label = GetJourneyStripOneLabel(rootOption.Value, position, element);
			if (!string.IsNullOrWhiteSpace(label))
			{
				SetJourneyPowerControlLabel(controlIndex, label);
			}
		}

		FlattenJourneySliderControls(rootOption.Value, stripOneControls);
	}

	private static string? GetJourneyStripOneLabel(int rootOption, int position, UIElement element)
	{
		return rootOption switch
		{
			5 => position == 0 ? "Enemy difficulty" : null,
			3 => position switch
			{
				0 => GetCreativeToggleLabel(element, "CreativePowers.FreezeTime"),
				1 => GetLocalizedValue("CreativePowers.StartDayImmediately", "CreativePowers.StartDayImmediately"),
				2 => GetLocalizedValue("CreativePowers.StartNoonImmediately", "CreativePowers.StartNoonImmediately"),
				3 => GetLocalizedValue("CreativePowers.StartNightImmediately", "CreativePowers.StartNightImmediately"),
				4 => GetLocalizedValue("CreativePowers.StartMidnightImmediately", "CreativePowers.StartMidnightImmediately"),
				5 => GetCreativeSliderLabel("CreativePowers.ModifyTimeRate"),
				_ => null,
			},
			4 => position switch
			{
				0 => GetCreativeSliderLabel("CreativePowers.ModifyWindDirectionAndStrength"),
				1 => GetCreativeToggleLabel(element, "CreativePowers.FreezeWindDirectionAndStrength"),
				2 => GetCreativeSliderLabel("CreativePowers.ModifyRainPower"),
				3 => GetCreativeToggleLabel(element, "CreativePowers.FreezeRainPower"),
				_ => null,
			},
			6 => position switch
			{
				0 => GetToggleStateLabel(element, "God Mode"),
				1 => GetToggleStateLabel(element, "Increase Placement Range"),
				2 => GetCreativeSliderLabel("CreativePowers.NPCSpawnRateSlider"),
				_ => null,
			},
			_ => null,
		};
	}

	private static string GetCreativeToggleLabel(UIElement element, string powerNameKey)
	{
		string stateKey = powerNameKey + (IsGroupOptionSelected(element) ? "_Enabled" : "_Disabled");
		return GetLocalizedValue(stateKey, powerNameKey);
	}

	private static string GetToggleStateLabel(UIElement element, string label)
	{
		return $"{label} {(IsGroupOptionSelected(element) ? "On" : "Off")}";
	}

	private static string GetCreativeSliderLabel(string powerNameKey)
	{
		return NormalizeCreativePowerLabel(GetLocalizedValue(powerNameKey + "_Closed", powerNameKey));
	}

	private void SetJourneyPowerControlLabel(int index, string label)
	{
		AccessibleExternalControl control = _controls[index];
		_controls[index] = control with
		{
			Label = label,
			Details = () => label,
		};
	}

	private void FlattenJourneySliderControls(int rootOption, IReadOnlyList<int> stripOneControls)
	{
		if (_state is not UICreativePowersMenu journeyPowers)
		{
			return;
		}

		switch (rootOption)
		{
			case 3:
				ReplaceJourneySliderControl(journeyPowers, stripOneControls, position: 5, "_timeCategory", sliderOption: 1);
				break;
			case 4:
				ReplaceJourneySliderControl(journeyPowers, stripOneControls, position: 0, "_weatherCategory", sliderOption: 1);
				ReplaceJourneySliderControl(journeyPowers, stripOneControls, position: 2, "_weatherCategory", sliderOption: 2);
				break;
			case 6:
				ReplaceJourneySliderControl(journeyPowers, stripOneControls, position: 2, "_personalCategory", sliderOption: 1);
				break;
		}
	}

	private void ReplaceJourneySliderControl(
		UICreativePowersMenu journeyPowers,
		IReadOnlyList<int> stripOneControls,
		int position,
		string categoryFieldName,
		int sliderOption)
	{
		if (position < 0 || position >= stripOneControls.Count ||
			GetJourneySliderElement(journeyPowers, categoryFieldName, sliderOption) is not UIElement sliderElement ||
			!TryGetSlider(sliderElement, out Func<float>? getValue, out Action<float>? setValue))
		{
			return;
		}

		int controlIndex = stripOneControls[position];
		AccessibleExternalControl control = _controls[controlIndex];
		string label = control.Label;
		_controls[controlIndex] = control with
		{
			Details = () => DescribeJourneySlider(label, getValue),
			Role = "slider",
			Activate = _ =>
			{
				setValue(Math.Clamp(getValue() + 0.05f, 0f, 1f));
				SoundEngine.PlaySound(SoundID.MenuTick);
			},
			SliderValue = getValue,
			SetSliderValue = setValue,
		};
	}

	private static UIElement? GetJourneySliderElement(
		UICreativePowersMenu journeyPowers,
		string categoryFieldName,
		int sliderOption)
	{
		object? category = GetFieldValue(journeyPowers.GetType(), journeyPowers, categoryFieldName);
		object? sliders = category is null ? null : GetFieldValue(category.GetType(), category, "Sliders");
		if (sliders is not IDictionary sliderDictionary ||
			!sliderDictionary.Contains(sliderOption) ||
			sliderDictionary[sliderOption] is not UIElement sliderContainer)
		{
			return null;
		}

		if (TryGetSlider(sliderContainer, out _, out _))
		{
			return sliderContainer;
		}

		return EnumerateDescendants(sliderContainer)
			.FirstOrDefault(element => TryGetSlider(element, out _, out _));
	}

	private static string DescribeJourneySlider(string label, Func<float> getValue)
	{
		return $"{label}, {Math.Clamp(getValue(), 0f, 1f):P0}";
	}

	private void SuppressJourneyPowerRoleVerbiage()
	{
		for (int index = 0; index < _controls.Count; index++)
		{
			AccessibleExternalControl control = _controls[index];
			if (control.Role.Equals("toggle", StringComparison.OrdinalIgnoreCase) ||
				control.Role.Equals("submenu", StringComparison.OrdinalIgnoreCase))
			{
				_controls[index] = control with { AnnouncesRole = false };
			}
		}
	}

	private static bool TryOpenJourneyPowerCategory(UICreativePowersMenu journeyPowers, int option)
	{
		UIElement? category = EnumerateDescendants(journeyPowers).FirstOrDefault(element =>
			GetStripDepth(element) == 0 && GetIntOptionValue(element) == option);
		if (category is null)
		{
			return false;
		}

		ActivateElement(category, secondary: false, "Journey power category");
		return true;
	}

	internal static bool TryToggleJourneyInfectionSpread(UICreativePowersMenu journeyPowers)
	{
		UIElement? toggle = EnumerateDescendants(journeyPowers).FirstOrDefault(element =>
			GetStripDepth(element) == 0 && GetOptionValueType(element) == typeof(bool));
		if (toggle is null)
		{
			return false;
		}

		ActivateElement(toggle, secondary: false, "Journey infection spread");
		return true;
	}

	internal static bool TryGetJourneyEnemyDifficultySlider(
		UICreativePowersMenu journeyPowers,
		out Func<float> getValue,
		out Action<float> setValue)
	{
		UIElement? sliderElement = GetJourneySliderElement(journeyPowers, "_mainCategory", sliderOption: 5);
		if (sliderElement is not null && TryGetSlider(sliderElement, out getValue, out setValue))
		{
			return true;
		}

		getValue = null!;
		setValue = null!;
		return false;
	}

	private static void ResetJourneyPowerHierarchy(UICreativePowersMenu journeyPowers)
	{
		UIElement[] selectedCategories = EnumerateDescendants(journeyPowers)
			.Where(element => GetStripDepth(element) >= 0 &&
				GetOptionValueType(element) == typeof(int) &&
				IsGroupOptionSelected(element))
			.OrderByDescending(GetStripDepth)
			.ToArray();
		foreach (UIElement category in selectedCategories)
		{
			ActivateElement(category, secondary: false, "Journey power category");
		}
	}

	private static bool ShouldHideControl(UIState state, UIElement element)
	{
		return state is UIEmotesMenu && IsBackControl(element) ||
			state is UICreativePowersMenu && IsRedundantJourneyCategory(element);
	}

	private static bool IsRedundantJourneyCategory(UIElement element)
	{
		if (GetStripDepth(element) != 0 || GetOptionValueType(element) != typeof(int))
		{
			return false;
		}

		int? option = GetIntOptionValue(element);
		return option is 1 or 2;
	}

	private static bool IsBackControl(UIElement element)
	{
		string name = GetSnapPointName(element);
		return name.Equals("Back", StringComparison.OrdinalIgnoreCase) ||
			name.Equals("GoBack", StringComparison.OrdinalIgnoreCase) ||
			name.Equals("ExitButton", StringComparison.OrdinalIgnoreCase);
	}

	private static string GetSnapPointName(UIElement element)
	{
		return element.GetSnapPoint(out SnapPoint? snapPoint) ? snapPoint.Name : string.Empty;
	}

	private static string GetElementStableId(UIElement element)
	{
		if (element.GetSnapPoint(out SnapPoint? snapPoint))
		{
			return $"snap-{element.GetType().FullName}-{snapPoint.Name}-{snapPoint.Id}";
		}
		return $"element-{element.UniqueId}";
	}

	private static int GetStripDepth(UIElement element)
	{
		for (UIElement? current = element; current is not null; current = current.Parent)
		{
			string name = GetSnapPointName(current);
			if (name.StartsWith("strip ", StringComparison.OrdinalIgnoreCase) && int.TryParse(name.AsSpan(6), out int depth))
			{
				return depth;
			}
		}
		return -1;
	}

	private static Type? GetOptionValueType(UIElement element)
	{
		return element.GetType().GetProperty("OptionValue", InstanceMembers)?.PropertyType;
	}

	private static int? GetIntOptionValue(UIElement element)
	{
		return element.GetType().GetProperty("OptionValue", InstanceMembers)?.GetValue(element) is int option
			? option
			: null;
	}

	private static bool IsGroupOptionSelected(UIElement element)
	{
		return element.GetType().GetProperty("IsSelected", InstanceMembers)?.GetValue(element) as bool? == true;
	}

	private static bool IsActionable(UIElement element)
	{
		if (element.IgnoresMouseInteraction)
		{
			return false;
		}
		if (element is UIScrollbar)
		{
			return false;
		}

		if (InteractionFields.Any(field => field?.GetValue(element) is not null))
		{
			return true;
		}

		Type type = element.GetType();
		if (OverridesInteraction(type, nameof(UIElement.LeftClick)) ||
			OverridesInteraction(type, nameof(UIElement.RightClick)) ||
			OverridesInteraction(type, nameof(UIElement.LeftMouseDown)) ||
			OverridesInteraction(type, nameof(UIElement.RightMouseDown)))
		{
			return true;
		}

		string name = type.Name;
		return name.Contains("Button", StringComparison.OrdinalIgnoreCase) ||
			name.Contains("ItemSlot", StringComparison.OrdinalIgnoreCase) ||
			name.Contains("InputText", StringComparison.OrdinalIgnoreCase) ||
			name.Contains("SearchBar", StringComparison.OrdinalIgnoreCase) ||
			name.Contains("Slider", StringComparison.OrdinalIgnoreCase) ||
			name.Contains("Toggle", StringComparison.OrdinalIgnoreCase);
	}

	private static bool OverridesInteraction(Type type, string methodName)
	{
		MethodInfo? method = type.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public, null, [typeof(UIMouseEvent)], null);
		return method?.DeclaringType is not null && method.DeclaringType != typeof(UIElement);
	}

	private static IEnumerable<UIElement> EnumerateDescendants(UIElement root)
	{
		Stack<IEnumerator<UIElement>> stack = new();
		stack.Push(root.Children.GetEnumerator());
		while (stack.Count > 0)
		{
			IEnumerator<UIElement> enumerator = stack.Peek();
			if (!enumerator.MoveNext())
			{
				enumerator.Dispose();
				stack.Pop();
				continue;
			}

			UIElement element = enumerator.Current;
			yield return element;
			stack.Push(element.Children.GetEnumerator());
		}
	}

	private static string DescribeElement(UIElement element, bool includeAllText)
	{
		List<string> text = [];
		AddSpecialElementText(element, text);
		AddSemanticMemberText(element, text);

		foreach (UIElement child in EnumerateDescendants(element))
		{
			if (!includeAllText && IsActionable(child))
			{
				continue;
			}

			if (child is UIText uiText)
			{
				AddText(text, uiText.Text);
			}
			else if (IsTextPanel(child))
			{
				AddSemanticMemberText(child, text);
			}

			if (text.Count >= (includeAllText ? 12 : 5))
			{
				break;
			}
		}

		if (text.Count == 0 && element.GetSnapPoint(out SnapPoint? snapPoint))
		{
			AddText(text, Humanize(snapPoint.Name));
		}
		if (text.Count == 0)
		{
			AddText(text, Humanize(element.GetType().Name));
		}

		string result = string.Join(". ", text.Distinct(StringComparer.OrdinalIgnoreCase));
		return result.Length <= MaximumSpokenLabelLength ? result : result[..MaximumSpokenLabelLength].TrimEnd() + "…";
	}

	private static void AddSpecialElementText(UIElement element, List<string> destination)
	{
		Type type = element.GetType();
		AddCreativePowerText(element, destination);
		AddBestiaryFilterText(element, destination);
		if (element is UIScrollbar scrollbar)
		{
			float range = Math.Max(0f, scrollbar.MaxViewSize - scrollbar.ViewSize);
			AddText(destination, $"Scroll position {(range <= 0f ? 0f : scrollbar.ViewPosition / range):P0}");
		}
		if (type.Name == "EmoteButton" && GetFieldValue(type, element, "_emoteIndex") is int emoteIndex)
		{
			AddText(destination, Lang.GetEmojiName(emoteIndex).Value);
		}
		if (TryGetItemSlot(element, out Item[]? items, out int itemIndex, out _))
		{
			Item item = items[itemIndex];
			AddText(destination, item.IsAir ? "Empty item slot" : item.AffixName());
		}
		if (element is not UIScrollbar && TryGetSlider(element, out Func<float>? getValue, out _))
		{
			AddText(destination, $"{Math.Clamp(getValue(), 0f, 1f):P0}");
		}

		object? icon = GetFieldValue(type, element, "_icon");
		MethodInfo? hoverText = icon?.GetType().GetMethod("GetHoverText", BindingFlags.Instance | BindingFlags.Public, null, Type.EmptyTypes, null);
		if (hoverText?.ReturnType == typeof(string))
		{
			try
			{
				AddText(destination, hoverText.Invoke(icon, null) as string);
			}
			catch
			{
				// A third-party control's semantic getter must not break all UI navigation.
			}
		}
	}

	private static void AddCreativePowerText(UIElement element, List<string> destination)
	{
		Type? optionType = GetOptionValueType(element);
		if (optionType is null)
		{
			string? sliderOrPresetKey = GetCreativePowerNameKeyFromElement(element);
			if (!string.IsNullOrWhiteSpace(sliderOrPresetKey))
			{
				AddText(destination, NormalizeCreativePowerLabel(GetLocalizedValue(sliderOrPresetKey + "_Opened", sliderOrPresetKey)));
			}
			return;
		}

		bool selected = IsGroupOptionSelected(element);
		if (GetStripDepth(element) == 0)
		{
			if (optionType == typeof(int) &&
				element.GetType().GetProperty("OptionValue", InstanceMembers)?.GetValue(element) is int mainOption)
			{
				string? categoryLabel = mainOption switch
				{
					3 => "Time",
					4 => "Weather",
					5 => "Enemy difficulty",
					6 => "Personal powers",
					_ => null,
				};
				if (categoryLabel is not null)
				{
					AddText(destination, categoryLabel);
					return;
				}
			}
			else if (optionType == typeof(bool))
			{
				string infectionKey = "CreativePowers.StopBiomeSpread" + (selected ? "_Enabled" : "_Disabled");
				AddText(destination, GetLocalizedValue(infectionKey, "CreativePowers.StopBiomeSpread"));
				return;
			}
		}

		if (GetFieldValue(typeof(UIElement), element, "m_OnUpdate") is not Delegate updateHandlers)
		{
			return;
		}

		foreach (Delegate handler in updateHandlers.GetInvocationList())
		{
			object? target = handler.Target;
			if (target is null)
			{
				continue;
			}

			string? powerNameKey = GetCreativePowerNameKey(target);
			if (string.IsNullOrWhiteSpace(powerNameKey))
			{
				continue;
			}

			string stateSuffix = optionType == typeof(bool)
				? selected ? "_Enabled" : "_Disabled"
				: selected ? "_Opened" : "_Closed";
			AddText(destination, NormalizeCreativePowerLabel(GetLocalizedValue(powerNameKey + stateSuffix, powerNameKey)));
			return;
		}
	}

	private static string NormalizeCreativePowerLabel(string value)
	{
		foreach (string prefix in new[] { "Open ", "Close " })
		{
			if (value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
			{
				value = value[prefix.Length..];
				break;
			}
		}

		foreach (string suffix in new[] { " Menu", " Slider" })
		{
			if (value.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
			{
				value = value[..^suffix.Length];
				break;
			}
		}
		return value.Trim();
	}

	private static string? GetCreativePowerNameKeyFromElement(UIElement element)
	{
		string[] delegateFieldNames =
		[
			"m_OnUpdate", "m_OnLeftClick", "m_OnLeftMouseDown",
			"_getSliderValue", "_GetStatus", "_getStatus",
			"_slideKeyboardAction", "_SlideKeyboardAction", "_setStatusKeyboard",
		];
		foreach (string fieldName in delegateFieldNames)
		{
			if (GetFieldValue(element.GetType(), element, fieldName) is not Delegate handlers)
			{
				continue;
			}

			foreach (Delegate handler in handlers.GetInvocationList())
			{
				object? target = handler.Target;
				if (target is not null && GetCreativePowerNameKey(target) is string key)
				{
					return key;
				}
			}
		}
		return null;
	}

	private static string? GetCreativePowerNameKey(object target)
	{
		if (GetFieldValue(target.GetType(), target, "_powerNameKey") is string fieldKey &&
			!string.IsNullOrWhiteSpace(fieldKey))
		{
			return fieldKey;
		}

		foreach (Type current in EnumerateTypeHierarchy(target.GetType()))
		{
			MethodInfo? getButtonTextKey = current.GetMethod(
				"GetButtonTextKey",
				InstanceMembers | BindingFlags.DeclaredOnly,
				null,
				Type.EmptyTypes,
				null);
			try
			{
				if (getButtonTextKey?.Invoke(target, null) is string methodKey &&
					!string.IsNullOrWhiteSpace(methodKey))
				{
					return methodKey;
				}
			}
			catch
			{
				// Continue to captured power objects and known vanilla types.
			}
		}

		foreach (Type current in EnumerateTypeHierarchy(target.GetType()))
		{
			foreach (FieldInfo field in current.GetFields(InstanceMembers | BindingFlags.DeclaredOnly))
			{
				try
				{
					if (field.GetValue(target) is ICreativePower capturedPower &&
						!ReferenceEquals(capturedPower, target) &&
						GetCreativePowerNameKey(capturedPower) is string capturedKey)
					{
						return capturedKey;
					}
				}
				catch
				{
					// Compiler-generated handlers vary across runtime versions.
				}
			}
		}

		return target.GetType().Name switch
		{
			"FreezeTime" => "CreativePowers.FreezeTime",
			"StartDayImmediately" => "CreativePowers.StartDayImmediately",
			"StartNoonImmediately" => "CreativePowers.StartNoonImmediately",
			"StartNightImmediately" => "CreativePowers.StartNightImmediately",
			"StartMidnightImmediately" => "CreativePowers.StartMidnightImmediately",
			"ModifyTimeRate" => "CreativePowers.ModifyTimeRate",
			"ModifyWindDirectionAndStrength" => "CreativePowers.ModifyWindDirectionAndStrength",
			"FreezeWindDirectionAndStrength" => "CreativePowers.FreezeWindDirectionAndStrength",
			"ModifyRainPower" => "CreativePowers.ModifyRainPower",
			"FreezeRainPower" => "CreativePowers.FreezeRainPower",
			"GodmodePower" => "CreativePowers.Godmode",
			"FarPlacementRangePower" => "CreativePowers.InfinitePlacementRange",
			"SpawnRateSliderPerPlayerPower" => "CreativePowers.NPCSpawnRateSlider",
			"DifficultySliderPower" => "CreativePowers.DifficultySlider",
			"StopBiomeSpreadPower" => "CreativePowers.StopBiomeSpread",
			_ => null,
		};
	}

	private static void AddBestiaryFilterText(UIElement element, List<string> destination)
	{
		if (!GetSnapPointName(element).Equals("Filters", StringComparison.Ordinal) ||
			element.GetType().GetProperty("OptionValue", InstanceMembers)?.GetValue(element) is not int optionIndex ||
			optionIndex < 0)
		{
			return;
		}

		for (UIElement? parent = element.Parent; parent is not null; parent = parent.Parent)
		{
			if (parent is not UIBestiaryFilteringOptionsGrid)
			{
				continue;
			}

			object? filterer = GetFieldValue(parent.GetType(), parent, "_filterer");
			object? availableFilters = filterer is null
				? null
				: GetFieldValue(filterer.GetType(), filterer, "AvailableFilters") ??
					filterer.GetType().GetProperty("AvailableFilters", InstanceMembers)?.GetValue(filterer);
			if (availableFilters is IList filters && optionIndex < filters.Count && filters[optionIndex] is IBestiaryEntryFilter filter)
			{
				AddText(destination, Language.GetTextValue(filter.GetDisplayNameKey()));
			}
			return;
		}
	}

	private static string GetLocalizedValue(string preferredKey, string fallbackKey)
	{
		if (Language.Exists(preferredKey))
		{
			return Language.GetTextValue(preferredKey);
		}
		return Language.Exists(fallbackKey) ? Language.GetTextValue(fallbackKey) : Humanize(fallbackKey.Split('.').Last());
	}

	private static void AddSemanticMemberText(object source, List<string> destination)
	{
		Type type = source.GetType();
		if (!SemanticMembersByType.TryGetValue(type, out MemberInfo[]? members))
		{
			members = GetSemanticMembers(type);
			SemanticMembersByType[type] = members;
		}

		foreach (MemberInfo member in members)
		{
			try
			{
				if (member.Name.Equals("OptionValue", StringComparison.OrdinalIgnoreCase) &&
					type.Name.StartsWith("GroupOptionButton", StringComparison.Ordinal))
				{
					continue;
				}
				object? value = member switch
				{
					PropertyInfo property => property.GetValue(source),
					FieldInfo field => field.GetValue(source),
					_ => null,
				};
				AddText(destination, value switch
				{
					string stringValue => stringValue,
					LocalizedText localized => localized.Value,
					Enum enumValue => Humanize(enumValue.ToString()),
					_ when member.Name.Equals("OptionValue", StringComparison.OrdinalIgnoreCase) => value?.ToString(),
					_ => null,
				});
			}
			catch
			{
				// Reflection is a fallback for unknown controls; inaccessible members are skipped.
			}
		}
	}

	private static MemberInfo[] GetSemanticMembers(Type type)
	{
		List<MemberInfo> members = [];
		foreach (Type? current in EnumerateTypeHierarchy(type))
		{
			foreach (PropertyInfo property in current.GetProperties(InstanceMembers | BindingFlags.DeclaredOnly))
			{
				if (property.GetIndexParameters().Length == 0 && IsSemanticMemberName(property.Name))
				{
					members.Add(property);
				}
			}
			foreach (FieldInfo field in current.GetFields(InstanceMembers | BindingFlags.DeclaredOnly))
			{
				if (IsSemanticMemberName(field.Name))
				{
					members.Add(field);
				}
			}
		}
		return [.. members];
	}

	private static IEnumerable<Type> EnumerateTypeHierarchy(Type type)
	{
		for (Type? current = type; current is not null && current != typeof(object); current = current.BaseType)
		{
			yield return current;
		}
	}

	private static bool IsSemanticMemberName(string name)
	{
		name = name.TrimStart('_');
		return SemanticMemberNames.Contains(name, StringComparer.OrdinalIgnoreCase);
	}

	private static object? GetFieldValue(Type type, object instance, string name)
	{
		foreach (Type current in EnumerateTypeHierarchy(type))
		{
			FieldInfo? field = current.GetField(name, InstanceMembers | BindingFlags.DeclaredOnly);
			if (field is not null)
			{
				return field.GetValue(instance);
			}
		}
		return null;
	}

	private static bool IsTextPanel(UIElement element)
	{
		for (Type? type = element.GetType(); type is not null; type = type.BaseType)
		{
			if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(UITextPanel<>))
			{
				return true;
			}
		}
		return false;
	}

	private static void AddText(List<string> destination, string? value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return;
		}

		string clean = ColorTagPattern.Replace(value, "$1");
		clean = ChatTagPattern.Replace(clean, string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
		while (clean.Contains("  ", StringComparison.Ordinal))
		{
			clean = clean.Replace("  ", " ", StringComparison.Ordinal);
		}
		if (!string.IsNullOrWhiteSpace(clean))
		{
			destination.Add(clean);
		}
	}

	private static string DescribeRole(UIElement element)
	{
		string name = element.GetType().Name;
		if (element is UIScrollbar)
		{
			return "scroll bar";
		}
		if (name.Contains("Input", StringComparison.OrdinalIgnoreCase) || name.Contains("Search", StringComparison.OrdinalIgnoreCase))
		{
			return "edit field";
		}
		if (name.Contains("Slider", StringComparison.OrdinalIgnoreCase))
		{
			return "slider";
		}
		if (name.Contains("Toggle", StringComparison.OrdinalIgnoreCase) || name.Contains("Option", StringComparison.OrdinalIgnoreCase))
		{
			Type? optionType = GetOptionValueType(element);
			if (optionType == typeof(int) && GetStripDepth(element) >= 0)
			{
				return "submenu";
			}
			return optionType == typeof(bool) ? "toggle" : "choice";
		}
		if (name.Contains("Item", StringComparison.OrdinalIgnoreCase) || name.Contains("Entry", StringComparison.OrdinalIgnoreCase))
		{
			return "item";
		}
		return "button";
	}

	private void MoveSelection(int offset)
	{
		SetSelection((_selectedIndex + offset + _controls.Count) % _controls.Count);
	}

	private void SetSelection(int index)
	{
		index = Math.Clamp(index, 0, _controls.Count - 1);
		if (index == _selectedIndex)
		{
			SoundEngine.PlaySound(SoundID.MenuTick);
			AnnounceSelection();
			return;
		}

		_selectedIndex = index;
		FocusSelectedControl();
		SoundEngine.PlaySound(SoundID.MenuTick);
		AnnounceSelection();
	}

	private void NavigateByFirstLetter(char letter)
	{
		int index = FirstLetterNavigator.FindNextIndex(_controls, _selectedIndex, letter, control => control.Label);
		if (index < 0)
		{
			TerrariumMod.ScreenReader.Output($"No control starting with {char.ToUpperInvariant(letter)}.");
			return;
		}
		SetSelection(index);
	}

	private void FocusSelectedControl()
	{
		if (_controls.Count == 0)
		{
			return;
		}

		AccessibleExternalControl control = _controls[_selectedIndex];
		control.Focus?.Invoke();
		if (control.Element is not UIElement element)
		{
			if (_hoveredElement is not null)
			{
				TryMouseOut(_hoveredElement);
				_hoveredElement = null;
			}
			return;
		}

		ScrollIntoView(element);
		if (_hoveredElement is not null && !ReferenceEquals(_hoveredElement, element))
		{
			TryMouseOut(_hoveredElement);
		}

		CalculatedStyle dimensions = element.GetDimensions();
		Vector2 position = dimensions.Position() + new Vector2(dimensions.Width / 2f, dimensions.Height / 2f);
		Main.mouseX = (int)position.X;
		Main.mouseY = (int)position.Y;
		try
		{
			element.MouseOver(new UIMouseEvent(element, position));
			_hoveredElement = element;
		}
		catch
		{
			_hoveredElement = null;
		}
	}

	private static void ScrollIntoView(UIElement element)
	{
		UIElement directChild = element;
		for (UIElement? parent = element.Parent; parent is not null; parent = parent.Parent)
		{
			if (parent is UIList list)
			{
				UIElement target = directChild;
				list.Goto(candidate => ReferenceEquals(candidate, target), center: true);
				return;
			}
			directChild = parent;
		}
	}

	private static void TryMouseOut(UIElement element)
	{
		try
		{
			CalculatedStyle dimensions = element.GetDimensions();
			Vector2 position = dimensions.Position() + new Vector2(dimensions.Width / 2f, dimensions.Height / 2f);
			element.MouseOut(new UIMouseEvent(element, position));
		}
		catch
		{
			// Third-party UI callbacks are isolated from the accessibility controller.
		}
	}

	private void ActivateSelection(bool secondary)
	{
		AccessibleExternalControl control = _controls[_selectedIndex];
		if (control.Activate is not null)
		{
			control.Activate(secondary);
			_lastSelectionState = control.SliderValue is null ? GetSelectionState() : string.Empty;
			_nextRebuildAt = 0;
			return;
		}
		if (control.Element is not UIElement element)
		{
			return;
		}
		if (!secondary && _state is UIEmotesMenu && TryActivateEmote(element))
		{
			_lastSelectionState = GetSelectionState();
			return;
		}
		if (TryGetItemSlot(element, out Item[]? items, out int itemIndex, out int itemContext))
		{
			if (secondary)
			{
				ItemSlot.RightClick(items, itemContext, itemIndex);
			}
			else
			{
				ItemSlot.LeftClick(items, itemContext, itemIndex);
			}
			Recipe.FindRecipes();
			_lastSelectionState = string.Empty;
			return;
		}
		ActivateElement(element, secondary, control.Label);
		_lastSelectionState = string.Empty;
		_nextRebuildAt = 0;
	}

	private static bool TryActivateEmote(UIElement element)
	{
		Type type = element.GetType();
		if (type.Name != "EmoteButton" || GetFieldValue(type, element, "_emoteIndex") is not int emoteIndex)
		{
			return false;
		}

		EmoteBubble.MakeLocalPlayerEmote(emoteIndex);
		TerrariumMod.ScreenReader.Output($"{Lang.GetEmojiName(emoteIndex).Value} activated.");
		return true;
	}

	private static void ActivateElement(UIElement element, bool secondary, string label)
	{
		CalculatedStyle dimensions = element.GetDimensions();
		Vector2 position = dimensions.Position() + new Vector2(dimensions.Width / 2f, dimensions.Height / 2f);
		UIMouseEvent mouseEvent = new(element, position);
		bool oldDown = secondary ? Main.mouseRight : Main.mouseLeft;
		bool oldRelease = secondary ? Main.mouseRightRelease : Main.mouseLeftRelease;
		try
		{
			if (secondary)
			{
				Main.mouseRight = true;
				Main.mouseRightRelease = true;
				element.RightMouseDown(mouseEvent);
				element.RightMouseUp(mouseEvent);
				element.RightClick(mouseEvent);
			}
			else
			{
				Main.mouseLeft = true;
				Main.mouseLeftRelease = true;
				element.LeftMouseDown(mouseEvent);
				element.LeftMouseUp(mouseEvent);
				element.LeftClick(mouseEvent);
			}
		}
		catch (Exception exception)
		{
			TerrariumMod.ScreenReader.Output($"{label} could not be activated: {exception.GetBaseException().Message}");
		}
		finally
		{
			if (secondary)
			{
				Main.mouseRight = oldDown;
				Main.mouseRightRelease = oldRelease;
			}
			else
			{
				Main.mouseLeft = oldDown;
				Main.mouseLeftRelease = oldRelease;
			}
		}
	}

	private bool TryAdjustSelectedSlider(float offset)
	{
		AccessibleExternalControl control = _controls[_selectedIndex];
		if (control.SliderValue is not null && control.SetSliderValue is not null)
		{
			control.SetSliderValue(Math.Clamp(control.SliderValue() + offset, 0f, 1f));
			return true;
		}
		if (control.Element is not UIElement element)
		{
			return false;
		}
		if (!TryGetSlider(element, out Func<float>? getValue, out Action<float>? setValue))
		{
			return false;
		}

		setValue(Math.Clamp(getValue() + offset, 0f, 1f));
		return true;
	}

	private bool CanOpenSelectedSubmenu()
	{
		AccessibleExternalControl control = _controls[_selectedIndex];
		if (!control.Role.Equals("submenu", StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}

		return _state is not UICreativePowersMenu ||
			control.Element is UIElement element && GetStripDepth(element) == 0;
	}

	private void NavigateBack()
	{
		if (_state is UIBestiaryTest bestiary)
		{
			UIElement? openOptions = EnumerateDescendants(bestiary).FirstOrDefault(element =>
				element is UIBestiaryFilteringOptionsGrid or UIBestiarySortingOptionsGrid);
			if (openOptions is not null)
			{
				ActivateElement(openOptions, secondary: false, "Bestiary options");
				RebuildAndAnnounceLevel();
				return;
			}

			if (!Main.gameMenu)
			{
				// The native Bestiary back button plays MenuClose before calling
				// IngameFancyUI.Close, which plays the same sound again. Closing
				// directly keeps this consistent with one inventory back action.
				IngameFancyUI.Close();
				return;
			}
		}

		if (_state is UICreativePowersMenu journeyPowers)
		{
			UIElement? openCategory = EnumerateDescendants(journeyPowers)
				.Where(element => GetStripDepth(element) >= 0 &&
					GetOptionValueType(element) == typeof(int) &&
					IsGroupOptionSelected(element))
				.OrderByDescending(GetStripDepth)
				.FirstOrDefault();
			if (openCategory is not null)
			{
				if (_journeyCategoryOpenedFromInventory is int inventoryCategory &&
					GetStripDepth(openCategory) == 0 &&
					GetIntOptionValue(openCategory) == inventoryCategory)
				{
					Main.CreativeMenu.ToggleMenu();
					SoundEngine.PlaySound(SoundID.MenuClose);
					return;
				}

				string returnControlId = GetElementStableId(openCategory);
				ActivateElement(openCategory, secondary: false, DescribeElement(openCategory, includeAllText: false));
				RebuildAndAnnounceLevel(returnControlId);
				return;
			}

			Main.CreativeMenu.ToggleMenu();
			if (_hierarchyLevel is not null)
			{
				SoundEngine.PlaySound(SoundID.MenuClose);
			}
			return;
		}

		if (_state is not null)
		{
			UIElement? back = EnumerateDescendants(_state).FirstOrDefault(IsBackControl);
			if (back is not null)
			{
				ActivateElement(back, secondary: false, "Back");
				return;
			}
		}

		if (!Main.gameMenu)
		{
			IngameFancyUI.Close();
		}
	}

	private void RebuildAndAnnounceLevel(string? returnControlId = null)
	{
		_selectedIndex = 0;
		RebuildControls(preserveSelection: false);
		if (_controls.Count == 0)
		{
			return;
		}
		if (returnControlId is not null)
		{
			int returnIndex = _controls.FindIndex(control => control.StableId == returnControlId);
			if (returnIndex >= 0)
			{
				_selectedIndex = returnIndex;
			}
		}
		FocusSelectedControl();
		SoundEngine.PlaySound(SoundID.MenuClose);
		AnnounceSelection();
	}

	private static bool TryGetSlider(UIElement element, out Func<float> getValue, out Action<float> setValue)
	{
		if (element is UIScrollbar scrollbar)
		{
			getValue = () =>
			{
				float range = Math.Max(0f, scrollbar.MaxViewSize - scrollbar.ViewSize);
				return range <= 0f ? 0f : scrollbar.ViewPosition / range;
			};
			setValue = value => scrollbar.ViewPosition = Math.Clamp(value, 0f, 1f) * Math.Max(0f, scrollbar.MaxViewSize - scrollbar.ViewSize);
			return true;
		}

		object? getter = GetFieldValue(element.GetType(), element, "_getSliderValue") ??
			GetFieldValue(element.GetType(), element, "_GetStatus") ??
			GetFieldValue(element.GetType(), element, "_getStatus");
		object? setter = GetFieldValue(element.GetType(), element, "_slideKeyboardAction") ??
			GetFieldValue(element.GetType(), element, "_SlideKeyboardAction") ??
			GetFieldValue(element.GetType(), element, "_setStatusKeyboard");
		if (getter is Func<float> typedGetter && setter is Action<float> typedSetter)
		{
			getValue = typedGetter;
			setValue = typedSetter;
			return true;
		}

		getValue = null!;
		setValue = null!;
		return false;
	}

	private static bool TryGetItemSlot(UIElement element, out Item[] items, out int itemIndex, out int itemContext)
	{
		object? itemArray = GetFieldValue(element.GetType(), element, "_itemArray");
		object? index = GetFieldValue(element.GetType(), element, "_itemIndex");
		object? context = GetFieldValue(element.GetType(), element, "_itemSlotContext");
		if (itemArray is Item[] typedItems && index is int typedIndex && context is int typedContext &&
			typedIndex >= 0 && typedIndex < typedItems.Length)
		{
			items = typedItems;
			itemIndex = typedIndex;
			itemContext = typedContext;
			return true;
		}

		items = null!;
		itemIndex = 0;
		itemContext = 0;
		return false;
	}

	private void AnnounceSelection()
	{
		_lastSelectionState = GetSelectionState();
		TerrariumMod.ScreenReader.Output(DescribeSelection());
	}

	private void AnnounceChangedSelection()
	{
		if (_controls.Count == 0)
		{
			return;
		}

		string current = GetSelectionState();
		if (current == _lastSelectionState)
		{
			return;
		}
		_lastSelectionState = current;
		TerrariumMod.ScreenReader.Output(DescribeSelection());
	}

	private string GetSelectionState()
	{
		if (_controls.Count == 0)
		{
			return string.Empty;
		}

		AccessibleExternalControl control = _controls[_selectedIndex];
		string liveLabel = GetLiveLabel(control);
		return $"{control.StableId}|{liveLabel}|{_selectedIndex}|{_controls.Count}";
	}

	private string DescribeSelection()
	{
		AccessibleExternalControl control = _controls[_selectedIndex];
		string liveLabel = GetLiveLabel(control);
		if (_state is UIBestiaryTest)
		{
			return $"{NormalizeSpokenSentence(liveLabel)} Entry {_selectedIndex + 1} of {_controls.Count}.";
		}
		string role = control.AnnouncesRole && !string.IsNullOrWhiteSpace(control.Role) ? $", {control.Role}" : string.Empty;
		return $"{liveLabel}{role}, {_selectedIndex + 1} of {_controls.Count}.";
	}

	private string GetLiveLabel(AccessibleExternalControl control)
	{
		if (_state is UIBestiaryTest)
		{
			return control.Details();
		}
		string label = _state is UICreativePowersMenu || control.Element is null
			? control.Label
			: DescribeElement(control.Element, includeAllText: false);
		if (_state is UICreativePowersMenu)
		{
			if (control.SliderValue is not null)
			{
				return DescribeJourneySlider(label, control.SliderValue);
			}
			if (control.Element is UIElement element && TryGetSlider(element, out Func<float>? getValue, out _))
			{
				return DescribeJourneySlider(label, getValue);
			}
		}
		return label;
	}

	private void ReadHelp()
	{
		string focus = _controls.Count == 0 ? "No actionable control is currently exposed." : DescribeSelection();
		TerrariumMod.ScreenReader.Output(
			$"Accessible screen help. {focus} " +
			"Up and Down move through discovered controls. Home and End move to the first and last controls. Page Up and Page Down move by ten. " +
			"Letter keys jump to controls by name. Left and Right Arrow keys adjust sliders or navigate into and out of submenus. Enter activates the focused control, and Shift Enter performs an alternate right click. Control R reads all discovered text for the control. " +
			"Text fields use the screen's native editor after activation. Escape uses the screen's normal back or cancel behavior. This semantic adapter is the fallback for stock and mod screens without a purpose-built Terrarium menu.");
	}

	private void AnnounceChangedStatus(long now)
	{
		if (_state is null || now < _nextStatusAnnouncementAt || !ShouldAnnounceDynamicStatus(_state))
		{
			return;
		}

		_nextStatusAnnouncementAt = now + StatusAnnouncementIntervalMilliseconds;
		string status = DescribeStatus(_state);
		if (string.IsNullOrWhiteSpace(status) || status == _lastStatus)
		{
			return;
		}
		_lastStatus = status;
		TerrariumMod.ScreenReader.Output(status, interrupt: false);
	}

	private static bool ShouldAnnounceDynamicStatus(UIState state)
	{
		string name = state.GetType().Name;
		return name.Contains("Progress", StringComparison.OrdinalIgnoreCase) ||
			name.Contains("Load", StringComparison.OrdinalIgnoreCase) ||
			name.Contains("Message", StringComparison.OrdinalIgnoreCase) ||
			name.Contains("Error", StringComparison.OrdinalIgnoreCase) ||
			name.Contains("Report", StringComparison.OrdinalIgnoreCase);
	}

	private static string DescribeStatus(UIState state)
	{
		List<string> text = [];
		foreach (UIElement element in EnumerateDescendants(state))
		{
			if (element is UIText uiText)
			{
				AddText(text, uiText.Text);
			}
			else if (IsTextPanel(element))
			{
				AddSemanticMemberText(element, text);
			}
			if (text.Count >= 10)
			{
				break;
			}
		}

		string status = string.Join(". ", text.Distinct(StringComparer.OrdinalIgnoreCase));
		return status.Length <= MaximumSpokenLabelLength ? status : status[..MaximumSpokenLabelLength].TrimEnd() + "…";
	}

	private void MonitorTextEditing()
	{
		if (_state is null || !TryGetEditingValue(_state, out string value, out bool hidden))
		{
			return;
		}

		if (_lastEditValue is null)
		{
			_lastEditValue = value;
			TerrariumMod.ScreenReader.Output(hidden ? $"Edit field, {value.Length} characters." : $"Edit field. {value}");
			return;
		}
		if (value == _lastEditValue)
		{
			return;
		}

		string oldValue = _lastEditValue;
		_lastEditValue = value;
		if (hidden)
		{
			TerrariumMod.ScreenReader.Output($"{value.Length} characters.", interrupt: false);
		}
		else if (value.Length == oldValue.Length + 1 && value.StartsWith(oldValue, StringComparison.Ordinal))
		{
			TerrariumMod.ScreenReader.Output(value[^1].ToString(), interrupt: false);
		}
		else if (oldValue.Length == value.Length + 1 && oldValue.StartsWith(value, StringComparison.Ordinal))
		{
			TerrariumMod.ScreenReader.Output($"Deleted {oldValue[^1]}", interrupt: false);
		}
		else
		{
			TerrariumMod.ScreenReader.Output(value, interrupt: false);
		}
	}

	private static bool TryGetEditingValue(UIState state, out string value, out bool hidden)
	{
		hidden = state.GetType().Name == "UIVirtualKeyboard" && GetStaticBoolean(state.GetType(), "ShouldHideText");
		if (state.GetType().Name == "UIVirtualKeyboard" && TryReadStringMember(state, ["Text"], out value))
		{
			return true;
		}

		foreach (UIElement element in EnumerateDescendants(state))
		{
			string name = element.GetType().Name;
			bool editing = name.Contains("InputText", StringComparison.OrdinalIgnoreCase) ||
				name.Contains("SearchBar", StringComparison.OrdinalIgnoreCase);
			if (!editing)
			{
				continue;
			}

			object? focused = GetFieldValue(element.GetType(), element, "Focused") ??
				GetFieldValue(element.GetType(), element, "isWritingText");
			PropertyInfo? writingProperty = element.GetType().GetProperty("IsWritingText", InstanceMembers);
			if (writingProperty?.GetValue(element) is bool propertyFocused)
			{
				focused = propertyFocused;
			}
			if (focused is bool isFocused && !isFocused)
			{
				continue;
			}

			if (TryReadStringMember(element, ["Text", "CurrentString", "_currentString", "actualContents"], out value))
			{
				return true;
			}
		}

		value = string.Empty;
		return false;
	}

	private static bool TryReadStringMember(object source, IReadOnlyList<string> names, out string value)
	{
		foreach (Type current in EnumerateTypeHierarchy(source.GetType()))
		{
			foreach (string name in names)
			{
				PropertyInfo? property = current.GetProperty(name, InstanceMembers | BindingFlags.DeclaredOnly);
				if (property?.GetIndexParameters().Length == 0 && property.GetValue(source) is string propertyValue)
				{
					value = propertyValue;
					return true;
				}
				FieldInfo? field = current.GetField(name, InstanceMembers | BindingFlags.DeclaredOnly);
				if (field?.GetValue(source) is string fieldValue)
				{
					value = fieldValue;
					return true;
				}
			}
		}

		value = string.Empty;
		return false;
	}

	private static bool GetStaticBoolean(Type type, string name)
	{
		return type.GetProperty(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(null) as bool? == true ||
			type.GetField(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(null) as bool? == true;
	}

	private static string DescribeScreenTitle(UIState state)
	{
		return state.GetType().Name switch
		{
			"UIBestiaryTest" => "Bestiary",
			"UICreativePowersMenu" => "Journey powers",
			"UIEmotesMenu" => "Emotes",
			"UIWorldLoad" => "Loading world",
			"UILoadMods" => "Loading mods",
			_ => Humanize(state.GetType().Name),
		};
	}

	private static string Humanize(string value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return "Control";
		}

		value = value.TrimStart('_');
		if (value.StartsWith("UI", StringComparison.Ordinal) && value.Length > 2 && char.IsUpper(value[2]))
		{
			value = value[2..];
		}
		foreach (string suffix in new[] { "MenuState", "State", "Menu", "Button", "Element" })
		{
			if (value.EndsWith(suffix, StringComparison.Ordinal) && value.Length > suffix.Length)
			{
				value = value[..^suffix.Length];
				break;
			}
		}

		StringBuilder result = new();
		for (int index = 0; index < value.Length; index++)
		{
			char current = value[index];
			if (index > 0 && char.IsUpper(current) &&
				(char.IsLower(value[index - 1]) || index + 1 < value.Length && char.IsLower(value[index + 1])))
			{
				result.Append(' ');
			}
			result.Append(current);
		}
		return result.ToString().Trim();
	}

	private bool Pressed(KeyboardState keyboard, Keys key)
	{
		return keyboard.IsKeyDown(key) && _previousKeyboard.IsKeyUp(key);
	}

	private bool NavigationTriggered(KeyboardState keyboard, Keys key, long now)
	{
		if (keyboard.IsKeyUp(key))
		{
			return false;
		}
		if (_previousKeyboard.IsKeyUp(key))
		{
			_repeatingKey = key;
			_nextRepeatAt = now + NavigationRepeatDelayMilliseconds;
			return true;
		}
		if (_repeatingKey != key || now < _nextRepeatAt)
		{
			return false;
		}
		_nextRepeatAt = now + NavigationRepeatIntervalMilliseconds;
		return true;
	}

	private static bool IsShiftDown(KeyboardState keyboard)
	{
		return keyboard.IsKeyDown(Keys.LeftShift) || keyboard.IsKeyDown(Keys.RightShift);
	}

	private static bool IsControlDown(KeyboardState keyboard)
	{
		return keyboard.IsKeyDown(Keys.LeftControl) || keyboard.IsKeyDown(Keys.RightControl);
	}

	private static void ConsumeNavigationTriggers()
	{
		TriggersSet current = PlayerInput.Triggers.Current;
		TriggersSet justPressed = PlayerInput.Triggers.JustPressed;
		current.MenuUp = current.MenuDown = current.MenuLeft = current.MenuRight = false;
		justPressed.MenuUp = justPressed.MenuDown = justPressed.MenuLeft = justPressed.MenuRight = false;
		current.MapStyle = false;
		justPressed.MapStyle = false;
	}

	private static void ConsumeBoundLetterTriggers(KeyboardState keyboard)
	{
		if (!PlayerInput.CurrentProfile.InputModes.TryGetValue(InputMode.Keyboard, out KeyConfiguration? bindings))
		{
			return;
		}

		for (int value = (int)Keys.A; value <= (int)Keys.Z; value++)
		{
			Keys key = (Keys)value;
			if (keyboard.IsKeyUp(key))
			{
				continue;
			}

			string keyName = key.ToString();
			foreach ((string triggerName, List<string> keys) in bindings.KeyStatus)
			{
				if (!keys.Contains(keyName))
				{
					continue;
				}
				if (PlayerInput.Triggers.Current.KeyStatus.ContainsKey(triggerName))
				{
					PlayerInput.Triggers.Current.KeyStatus[triggerName] = false;
				}
				if (PlayerInput.Triggers.JustPressed.KeyStatus.ContainsKey(triggerName))
				{
					PlayerInput.Triggers.JustPressed.KeyStatus[triggerName] = false;
				}
			}
		}
	}

	private sealed record AccessibleExternalControl(
		UIElement? Element,
		string StableId,
		string Label,
		Func<string> Details,
		string Role,
		Action<bool>? Activate = null,
		Action? Focus = null,
		Func<float>? SliderValue = null,
		Action<float>? SetSliderValue = null,
		bool AnnouncesRole = true);
}
