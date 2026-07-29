#nullable enable

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent.UI.Elements;
using Terraria.ID;
using Terraria.UI;
using Ariadne.Accessibility;

namespace Ariadne.Menus;

internal readonly record struct AccessibleHelpTopic(string Control, string Explanation);

internal abstract class AccessibleMenuState : UIState
{
	private const int VisibleEntryCount = 9;
	private static readonly TimeSpan NavigationRepeatDelay = TimeSpan.FromMilliseconds(450);
	private static readonly TimeSpan NavigationRepeatInterval = TimeSpan.FromMilliseconds(85);

	private readonly List<UIText> _entryLabels = [];
	private readonly List<AccessibleMenuEntry> _entries = [];
	private UIText? _titleLabel;
	private UIText? _descriptionLabel;
	private KeyboardState _previousKeyboard;
	private Keys? _repeatingNavigationKey;
	private TimeSpan _nextNavigationRepeat;
	private int _selectedIndex;

	protected AccessibleMenuState(AccessibleMenuController controller)
	{
		Controller = controller;
	}

	protected AccessibleMenuController Controller { get; }

	protected abstract string Title { get; }

	protected virtual bool CanGoBack => true;

	protected virtual bool UsesHierarchicalNavigation => Controller.IsInGame;

	protected virtual bool RightArrowOpensSubmenu => UsesHierarchicalNavigation;

	protected virtual bool AnnouncesSubmenuRole => true;

	protected virtual bool KeepsInventoryOpen => false;

	protected virtual int? HierarchyLevel => null;

	protected virtual string AdditionalControlHint => string.Empty;

	protected virtual string AdditionalNavigationInstructions => string.Empty;

	/// <summary>
	/// What the contextual help says Enter does. A screen that claims Enter for
	/// something other than activation has to correct this, or its help contradicts
	/// the key it just described.
	/// </summary>
	protected virtual string ActivationHelp =>
		"Activate the focused option or its currently selected action.";

	protected int SelectedIndex => _selectedIndex;

	protected abstract void BuildEntries(List<AccessibleMenuEntry> entries);

	protected virtual bool ActivationAdjustsValue(AccessibleMenuEntry entry) => false;

	protected virtual bool PlaysActivationTick(AccessibleMenuEntry entry) => true;

	public override void OnInitialize()
	{
		UIPanel panel = new()
		{
			HAlign = 0.5f,
			VAlign = 0.55f,
			BackgroundColor = new Color(24, 31, 58) * 0.96f,
			BorderColor = new Color(93, 125, 213),
		};
		panel.Width.Set(720f, 0f);
		panel.Height.Set(570f, 0f);
		Append(panel);

		_titleLabel = new UIText(string.Empty, 0.8f, large: true)
		{
			HAlign = 0.5f,
			TextColor = Color.White,
		};
		_titleLabel.Top.Set(14f, 0f);
		panel.Append(_titleLabel);

		for (int index = 0; index < VisibleEntryCount; index++)
		{
			UIText label = new(string.Empty, 0.88f)
			{
				HAlign = 0.5f,
				TextOriginX = 0.5f,
			};
			label.Top.Set(72f + index * 42f, 0f);
			label.Width.Set(-34f, 1f);
			label.Height.Set(36f, 0f);
			_entryLabels.Add(label);
			panel.Append(label);
		}

		_descriptionLabel = new UIText(string.Empty, 0.72f)
		{
			HAlign = 0.5f,
			TextOriginX = 0.5f,
			TextColor = new Color(195, 205, 232),
			IsWrapped = true,
		};
		_descriptionLabel.Top.Set(462f, 0f);
		_descriptionLabel.Width.Set(-50f, 1f);
		_descriptionLabel.Height.Set(46f, 0f);
		panel.Append(_descriptionLabel);

		string controlHint =
			CanGoBack
				? UsesHierarchicalNavigation
					? RightArrowOpensSubmenu
						? $"Up/Down: move    Letters: jump    Left/Right: adjust/tree    Enter: activate    Escape: back    {ContextHelpChord.ShortName}: help"
						: $"Up/Down: move    Letters: jump    Enter: activate    Left/Escape: back    {ContextHelpChord.ShortName}: help"
					: $"Up/Down: move    Letters: jump    Left/Right: change    Enter: select    Escape: back    {ContextHelpChord.ShortName}: help"
				: $"Up/Down: move    Letters: jump    Left/Right: change    Enter: select    {ContextHelpChord.ShortName}: help";
		UIText help = new(
			controlHint + AdditionalControlHint,
			0.68f)
		{
			HAlign = 0.5f,
			TextColor = new Color(165, 177, 211),
		};
		help.Top.Set(530f, 0f);
		panel.Append(help);
	}

	public override void OnActivate()
	{
		if (KeepsInventoryOpen)
		{
			// Full-screen inventory-backed menus must restore this after
			// IngameFancyUI.OpenUIState closes the vanilla inventory.
			Main.playerInventory = true;
		}
		_previousKeyboard = Keyboard.GetState();
		_repeatingNavigationKey = null;
		RebuildEntries();

		string availability = AriadneMod.ScreenReader.IsAvailable
			? string.Empty
			: " Speech output is unavailable; see the tModLoader client log.";
		string controls = UsesHierarchicalNavigation
			? RightArrowOpensSubmenu
				? $"Use Up and Down Arrow keys to move, letter keys to jump by name, Right Arrow to open submenus, Left Arrow to go back when the focused option is not adjustable, Left and Right Arrow keys to change adjustable values, Enter to activate the focused option, Escape to go back, and {ContextHelpChord.Name} for contextual help."
				: $"Use Up and Down Arrow keys to move, letter keys to jump by name, Enter to activate options, Left Arrow or Escape to go back, and {ContextHelpChord.Name} for contextual help."
			: "Use Up and Down Arrow keys to move, letter keys to jump by name, Left and Right Arrow keys to change values, Enter to select" +
				(CanGoBack ? ", Escape to go back" : string.Empty) +
				$", and {ContextHelpChord.Name} for contextual help.";
		string hierarchy = HierarchyLevel is int level ? $" Level {level}." : string.Empty;
		AriadneMod.ScreenReader.Output($"{Title}. {DescribeSelection()}{hierarchy} {controls}{AdditionalNavigationInstructions}{availability}");
	}

	public override void Update(GameTime gameTime)
	{
		base.Update(gameTime);

		KeyboardState keyboard = Keyboard.GetState();
		if (_repeatingNavigationKey is Keys repeatingKey && keyboard.IsKeyUp(repeatingKey))
		{
			_repeatingNavigationKey = null;
		}
		if (ContextHelpChord.Pressed(keyboard, _previousKeyboard))
		{
			OpenContextHelp();
			_previousKeyboard = keyboard;
			return;
		}
		if (ContextHelpChord.ModifierHeld(keyboard))
		{
			// Alt is Ariadne's command prefix. Letting it fall through would jump the
			// selection by first letter on the way to a chord.
			_previousKeyboard = keyboard;
			return;
		}
		if (HandleAdditionalInput(keyboard, gameTime))
		{
			_previousKeyboard = keyboard;
			return;
		}
		if (_entries.Count == 0)
		{
			if (CanGoBack &&
				(Pressed(keyboard, Keys.Escape) ||
					UsesHierarchicalNavigation && Pressed(keyboard, Keys.Left)))
			{
				GoBack();
			}
			_previousKeyboard = keyboard;
			return;
		}

		if (NavigationTriggered(keyboard, Keys.Up, gameTime))
		{
			MoveSelection(-1);
		}
		else if (NavigationTriggered(keyboard, Keys.Down, gameTime))
		{
			MoveSelection(1);
		}
		else if (Pressed(keyboard, Keys.Home))
		{
			SetSelection(0);
		}
		else if (Pressed(keyboard, Keys.End))
		{
			SetSelection(_entries.Count - 1);
		}
		else if (Pressed(keyboard, Keys.PageUp))
		{
			SetSelection(Math.Max(0, _selectedIndex - VisibleEntryCount));
		}
		else if (Pressed(keyboard, Keys.PageDown))
		{
			SetSelection(Math.Min(_entries.Count - 1, _selectedIndex + VisibleEntryCount));
		}
		else if (NavigationTriggered(keyboard, Keys.Left, gameTime))
		{
			if (UsesHierarchicalNavigation && !_entries[_selectedIndex].IsAdjustable && CanGoBack)
			{
				GoBack();
			}
			else
			{
				AdjustSelection(forward: false);
			}
		}
		else if (NavigationTriggered(keyboard, Keys.Right, gameTime))
		{
			AccessibleMenuEntry entry = _entries[_selectedIndex];
			if (entry.IsAdjustable)
			{
				AdjustSelection(forward: true);
			}
			else if (RightArrowOpensSubmenu && entry.IsSubmenu)
			{
				ActivateSelection();
			}
		}
		else if (Pressed(keyboard, Keys.Enter))
		{
			ActivateSelection();
			Main.chatRelease = false;
		}
		else if (FirstLetterNavigator.TryGetPressedLetter(keyboard, _previousKeyboard, out _, out char letter))
		{
			NavigateByFirstLetter(letter);
		}
		else if (CanGoBack && Pressed(keyboard, Keys.Escape))
		{
			GoBack();
		}

		_previousKeyboard = keyboard;
	}

	protected void RebuildEntries(bool announceSelection = false)
	{
		_entries.Clear();
		BuildEntries(_entries);
		_selectedIndex = Math.Clamp(_selectedIndex, 0, Math.Max(0, _entries.Count - 1));
		RefreshLabels();
		if (announceSelection && _entries.Count > 0)
		{
			AriadneMod.ScreenReader.Output(DescribeSelection());
		}
	}

	protected void Announce(string text)
	{
		AriadneMod.ScreenReader.Output(text);
	}

	protected virtual bool HandleAdditionalInput(KeyboardState keyboard, GameTime gameTime) => false;

	protected void SetSelectionWithoutAnnouncement(int index)
	{
		_selectedIndex = Math.Clamp(index, 0, Math.Max(0, _entries.Count - 1));
		RefreshLabels();
	}

	protected virtual void GoBack()
	{
		SoundEngine.PlaySound(SoundID.MenuClose);
		Controller.Back();
	}

	protected virtual void OpenContextHelp()
	{
		List<AccessibleHelpTopic> topics =
		[
			new("Current menu", $"{Title} contains {_entries.Count} options."),
		];
		if (_entries.Count > 0)
		{
			topics.Add(new("Focused option", DescribeSelection()));
			topics.Add(new("Up and Down Arrow keys", "Move between options. Movement wraps from the first option to the last and from the last option to the first. Hold an arrow key past the initial pause to move repeatedly."));
			topics.Add(new("Home and End", "Move directly to the first or last option."));
			topics.Add(new("Letter keys", "Jump to options by name. Press the same letter repeatedly to cycle through matching options."));
			if (_entries.Count > VisibleEntryCount)
			{
				topics.Add(new("Page Up and Page Down", $"Move through up to {VisibleEntryCount} options at a time."));
			}
			if (_entries.Exists(entry => entry.IsAdjustable))
			{
				topics.Add(new("Left and Right Arrow keys", "Change the value or current action of an adjustable option. Values and actions wrap when appropriate. Hold an arrow key past the initial pause to change repeatedly."));
			}
			if (UsesHierarchicalNavigation)
			{
				if (RightArrowOpensSubmenu && _entries.Exists(entry => entry.IsSubmenu))
				{
					topics.Add(new("Right Arrow", "Open the focused submenu without activating buttons or toggles."));
				}
				if (CanGoBack)
				{
					topics.Add(new("Left Arrow", "Return to the previous menu when the focused option is not adjustable."));
				}
			}
			topics.Add(new("Enter", ActivationHelp));
		}
		if (CanGoBack)
		{
			topics.Add(new("Escape", "Return to the previous menu without activating an option."));
		}
		AddContextHelpTopics(topics);
		topics.Add(new(ContextHelpChord.Name, $"Open this contextual help screen. Press {ContextHelpChord.Name} or Escape while reading help to return."));

		SoundEngine.PlaySound(SoundID.MenuOpen);
		Controller.Navigate(new AccessibleContextHelpMenuState(Controller, Title, topics, KeepsInventoryOpen));
	}

	protected virtual void AddContextHelpTopics(List<AccessibleHelpTopic> topics)
	{
	}

	protected bool Pressed(KeyboardState keyboard, Keys key)
	{
		return keyboard.IsKeyDown(key) && _previousKeyboard.IsKeyUp(key);
	}

	private bool NavigationTriggered(KeyboardState keyboard, Keys key, GameTime gameTime)
	{
		if (keyboard.IsKeyUp(key))
		{
			return false;
		}

		if (_previousKeyboard.IsKeyUp(key))
		{
			_repeatingNavigationKey = key;
			_nextNavigationRepeat = gameTime.TotalGameTime + NavigationRepeatDelay;
			return true;
		}

		if (_repeatingNavigationKey != key || gameTime.TotalGameTime < _nextNavigationRepeat)
		{
			return false;
		}

		_nextNavigationRepeat = gameTime.TotalGameTime + NavigationRepeatInterval;
		return true;
	}

	private void MoveSelection(int offset)
	{
		_selectedIndex = (_selectedIndex + offset + _entries.Count) % _entries.Count;
		SelectionChanged();
	}

	private void SetSelection(int index)
	{
		if (_selectedIndex == index)
		{
			return;
		}
		_selectedIndex = index;
		SelectionChanged();
	}

	private void NavigateByFirstLetter(char letter)
	{
		int index = FirstLetterNavigator.FindNextIndex(_entries, _selectedIndex, letter, entry => entry.Label());
		if (index < 0)
		{
			AriadneMod.ScreenReader.Output($"No option starting with {char.ToUpperInvariant(letter)}.");
			return;
		}
		if (index == _selectedIndex)
		{
			SoundEngine.PlaySound(SoundID.MenuTick);
			AriadneMod.ScreenReader.Output(DescribeSelection());
			return;
		}
		SetSelection(index);
	}

	private void SelectionChanged()
	{
		RefreshLabels();
		SoundEngine.PlaySound(SoundID.MenuTick);
		AriadneMod.ScreenReader.Output(DescribeSelection());
	}

	private void ActivateSelection()
	{
		AccessibleMenuEntry entry = _entries[_selectedIndex];
		if (!entry.IsEnabled)
		{
			SoundEngine.PlaySound(SoundID.MenuClose);
			string description = entry.Description?.Invoke() ?? string.Empty;
			AriadneMod.ScreenReader.Output(
				$"{entry.Label()}, unavailable." +
				(string.IsNullOrWhiteSpace(description) ? string.Empty : $" {description}"));
			return;
		}

		if (entry.Activate is null)
		{
			if (entry.NextValue is not null)
			{
				AdjustSelection(forward: true);
			}
			return;
		}

		if (ActivationAdjustsValue(entry))
		{
			if (PlaysActivationTick(entry))
			{
				SoundEngine.PlaySound(SoundID.MenuTick);
			}
			entry.Activate();
			string announcement = DescribeAdjustment(entry);
			RebuildEntries();
			if (!string.IsNullOrWhiteSpace(announcement))
			{
				AriadneMod.ScreenReader.Output(announcement);
			}
			return;
		}

		entry.Activate();
		if (Controller.IsShowing(this))
		{
			SoundEngine.PlaySound(SoundID.MenuTick);
			RebuildEntries(announceSelection: true);
			return;
		}
		SoundEngine.PlaySound(SoundID.MenuOpen);
	}

	private void AdjustSelection(bool forward)
	{
		AccessibleMenuEntry entry = _entries[_selectedIndex];
		if (!entry.IsEnabled)
		{
			string description = entry.Description?.Invoke() ?? string.Empty;
			AriadneMod.ScreenReader.Output(
				$"{entry.Label()}, unavailable." +
				(string.IsNullOrWhiteSpace(description) ? string.Empty : $" {description}"));
			return;
		}

		Action? adjustment = forward ? entry.NextValue : entry.PreviousValue;
		adjustment ??= forward ? entry.PreviousValue : entry.NextValue;
		if (adjustment is null)
		{
			return;
		}

		SoundEngine.PlaySound(SoundID.MenuTick);
		adjustment();
		string announcement = DescribeAdjustment(entry);
		RebuildEntries();
		if (!string.IsNullOrWhiteSpace(announcement))
		{
			AriadneMod.ScreenReader.Output(announcement);
		}
	}

	private static string DescribeAdjustment(AccessibleMenuEntry entry)
	{
		string? explicitAnnouncement = entry.AdjustmentAnnouncement?.Invoke();
		if (!string.IsNullOrWhiteSpace(explicitAnnouncement))
		{
			return explicitAnnouncement;
		}

		string label = entry.Label().Trim();
		int separator = Math.Max(label.LastIndexOf(':'), label.LastIndexOf('：'));
		return separator >= 0 && separator + 1 < label.Length
			? label[(separator + 1)..].Trim()
			: label;
	}

	private void RefreshLabels()
	{
		_titleLabel?.SetText(Title);
		for (int slot = 0; slot < _entryLabels.Count; slot++)
		{
			int entryIndex = GetFirstVisibleIndex() + slot;
			UIText label = _entryLabels[slot];
			if (entryIndex >= _entries.Count)
			{
				label.SetText(string.Empty);
				continue;
			}

			AccessibleMenuEntry entry = _entries[entryIndex];
			bool selected = entryIndex == _selectedIndex;
			string unavailable = entry.IsEnabled ? string.Empty : " (unavailable)";
			label.SetText($"{(selected ? "> " : "  ")}{entry.Label()}{unavailable}");
			label.TextColor = !entry.IsEnabled
				? new Color(135, 140, 160)
				: selected ? Main.OurFavoriteColor : Color.White;
		}

		_descriptionLabel?.SetText(
			_entries.Count == 0
				? "No options are available."
				: _entries[_selectedIndex].Description?.Invoke() ?? string.Empty);
	}

	private int GetFirstVisibleIndex()
	{
		if (_entries.Count <= VisibleEntryCount)
		{
			return 0;
		}

		int centered = _selectedIndex - VisibleEntryCount / 2;
		return Math.Clamp(centered, 0, _entries.Count - VisibleEntryCount);
	}

	protected string DescribeSelection()
	{
		if (_entries.Count == 0)
		{
			return "No options are available.";
		}

		AccessibleMenuEntry entry = _entries[_selectedIndex];
		string label = entry.Label().Trim();
		bool suppressRole = !AnnouncesSubmenuRole && entry.Role.Equals("submenu", StringComparison.OrdinalIgnoreCase);
		List<string> semantics = [];
		if (!suppressRole && !string.IsNullOrWhiteSpace(entry.Role))
		{
			semantics.Add(entry.Role.Trim());
		}
		if (entry.IsAdjustable)
		{
			semantics.Add("adjustable");
		}
		if (!entry.IsEnabled)
		{
			semantics.Add("unavailable");
		}
		semantics.Add($"{_selectedIndex + 1} of {_entries.Count}");

		string separator = EndsWithSentencePunctuation(label) ? " " : ", ";
		string description = entry.Description?.Invoke()?.Trim() ?? string.Empty;
		if (DescriptionRepeatsLabel(label, description))
		{
			description = string.Empty;
		}
		string details = string.IsNullOrWhiteSpace(description) ? string.Empty : $" {description}";
		return $"{label}{separator}{string.Join(", ", semantics)}.{details}";
	}

	private static bool EndsWithSentencePunctuation(string text)
	{
		return text.Length > 0 && text[^1] is '.' or '!' or '?' or '…';
	}

	private static bool DescriptionRepeatsLabel(string label, string description)
	{
		if (description.Length == 0)
		{
			return false;
		}

		if (label.Equals(description, StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}

		int separator = label.IndexOf(':');
		return separator >= 0 &&
			label[(separator + 1)..].Trim().Equals(description, StringComparison.OrdinalIgnoreCase);
	}
}

internal sealed class AccessibleContextHelpMenuState : AccessibleMenuState
{
	private readonly string _sourceTitle;
	private readonly List<AccessibleHelpTopic> _topics;
	private readonly bool _keepsInventoryOpen;

	internal AccessibleContextHelpMenuState(
		AccessibleMenuController controller,
		string sourceTitle,
		IEnumerable<AccessibleHelpTopic> topics,
		bool keepsInventoryOpen = false)
		: base(controller)
	{
		_sourceTitle = sourceTitle;
		_topics = [.. topics];
		_keepsInventoryOpen = keepsInventoryOpen;
	}

	protected override string Title => $"Help: {_sourceTitle}";

	protected override bool KeepsInventoryOpen => _keepsInventoryOpen;

	protected override void BuildEntries(List<AccessibleMenuEntry> entries)
	{
		foreach (AccessibleHelpTopic topic in _topics)
		{
			AccessibleHelpTopic capturedTopic = topic;
			entries.Add(new(
				() => capturedTopic.Control,
				description: () => capturedTopic.Explanation,
				// The help title is spoken when this screen opens. Omitting a role here
				// keeps it from being repeated as the user moves between topics.
				role: string.Empty));
		}
	}

	protected override void OpenContextHelp()
	{
		GoBack();
	}
}
