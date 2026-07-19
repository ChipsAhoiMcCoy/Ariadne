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

namespace Terrarium.Menus;

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

	protected abstract void BuildEntries(List<AccessibleMenuEntry> entries);

	protected virtual bool ActivationAdjustsValue(AccessibleMenuEntry entry) => false;

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

		UIText help = new(
			CanGoBack
				? "Up/Down: move    Left/Right: change    Enter: select    Escape: back    F1: help"
				: "Up/Down: move    Left/Right: change    Enter: select    F1: help",
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
		_previousKeyboard = Keyboard.GetState();
		_repeatingNavigationKey = null;
		RebuildEntries();

		string availability = TerrariumMod.ScreenReader.IsAvailable
			? string.Empty
			: " Speech output is unavailable; see the tModLoader client log.";
		TerrariumMod.ScreenReader.Output(
			$"{Title}. {DescribeSelection()} " +
			"Use Up and Down Arrow keys to move, Left and Right Arrow keys to change values, Enter to select" +
			(CanGoBack ? ", Escape to go back" : string.Empty) +
			", and F1 for contextual help." +
			availability);
	}

	public override void Update(GameTime gameTime)
	{
		base.Update(gameTime);

		KeyboardState keyboard = Keyboard.GetState();
		if (_repeatingNavigationKey is Keys repeatingKey && keyboard.IsKeyUp(repeatingKey))
		{
			_repeatingNavigationKey = null;
		}
		if (Pressed(keyboard, Keys.F1))
		{
			OpenContextHelp();
			_previousKeyboard = keyboard;
			return;
		}
		if (_entries.Count == 0)
		{
			if (CanGoBack && Pressed(keyboard, Keys.Escape))
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
			AdjustSelection(forward: false);
		}
		else if (NavigationTriggered(keyboard, Keys.Right, gameTime))
		{
			AdjustSelection(forward: true);
		}
		else if (Pressed(keyboard, Keys.Enter))
		{
			ActivateSelection();
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
			TerrariumMod.ScreenReader.Output(DescribeSelection());
		}
	}

	protected void Announce(string text)
	{
		TerrariumMod.ScreenReader.Output(text);
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
			if (_entries.Count > VisibleEntryCount)
			{
				topics.Add(new("Page Up and Page Down", $"Move through up to {VisibleEntryCount} options at a time."));
			}
			if (_entries.Exists(entry => entry.IsAdjustable))
			{
				topics.Add(new("Left and Right Arrow keys", "Change the value or current action of an adjustable option. Values and actions wrap when appropriate. Hold an arrow key past the initial pause to change repeatedly."));
			}
			topics.Add(new("Enter", "Activate the focused option or its currently selected action."));
		}
		if (CanGoBack)
		{
			topics.Add(new("Escape", "Return to the previous menu without activating an option."));
		}
		topics.Add(new("F1", "Open this contextual help screen. Press F1 or Escape while reading help to return."));

		SoundEngine.PlaySound(SoundID.MenuOpen);
		Controller.Navigate(new AccessibleContextHelpMenuState(Controller, Title, topics));
	}

	private bool Pressed(KeyboardState keyboard, Keys key)
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

	private void SelectionChanged()
	{
		RefreshLabels();
		SoundEngine.PlaySound(SoundID.MenuTick);
		TerrariumMod.ScreenReader.Output(DescribeSelection());
	}

	private void ActivateSelection()
	{
		AccessibleMenuEntry entry = _entries[_selectedIndex];
		if (!entry.IsEnabled)
		{
			SoundEngine.PlaySound(SoundID.MenuClose);
			string description = entry.Description?.Invoke() ?? string.Empty;
			TerrariumMod.ScreenReader.Output(
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
			SoundEngine.PlaySound(SoundID.MenuTick);
			entry.Activate();
			string announcement = DescribeAdjustment(entry);
			RebuildEntries();
			if (!string.IsNullOrWhiteSpace(announcement))
			{
				TerrariumMod.ScreenReader.Output(announcement);
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
			TerrariumMod.ScreenReader.Output(
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
			TerrariumMod.ScreenReader.Output(announcement);
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

	private string DescribeSelection()
	{
		if (_entries.Count == 0)
		{
			return "No options are available.";
		}

		AccessibleMenuEntry entry = _entries[_selectedIndex];
		string role = string.IsNullOrWhiteSpace(entry.Role) ? string.Empty : $", {entry.Role}";
		string state = entry.IsEnabled ? string.Empty : ", unavailable";
		string adjustable = entry.IsAdjustable ? ", adjustable" : string.Empty;
		string description = entry.Description?.Invoke() ?? string.Empty;
		if (!string.IsNullOrWhiteSpace(description))
		{
			description = $" {description}";
		}
		return $"{entry.Label()}{role}{adjustable}{state}, {_selectedIndex + 1} of {_entries.Count}.{description}";
	}
}

internal sealed class AccessibleContextHelpMenuState : AccessibleMenuState
{
	private readonly string _sourceTitle;
	private readonly List<AccessibleHelpTopic> _topics;

	internal AccessibleContextHelpMenuState(
		AccessibleMenuController controller,
		string sourceTitle,
		IEnumerable<AccessibleHelpTopic> topics)
		: base(controller)
	{
		_sourceTitle = sourceTitle;
		_topics = [.. topics];
	}

	protected override string Title => $"Help: {_sourceTitle}";

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
