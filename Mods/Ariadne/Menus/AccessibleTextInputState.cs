#nullable enable

using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent.UI.Elements;
using Terraria.GameInput;
using Terraria.ID;
using Terraria.UI;

namespace Ariadne.Menus;

internal sealed class AccessibleTextInputState : UIState
{
	private readonly AccessibleMenuController _controller;
	private readonly string _prompt;
	private readonly Action<string> _submit;
	private readonly int _maximumLength;
	private readonly bool _hideContents;
	private readonly Action? _cancel;
	private KeyboardState _previousKeyboard;
	private UIText? _valueLabel;
	private string _value;

	internal AccessibleTextInputState(
		AccessibleMenuController controller,
		string prompt,
		string initialValue,
		int maximumLength,
		Action<string> submit,
		bool hideContents = false,
		Action? cancel = null)
	{
		_controller = controller;
		_prompt = prompt;
		_value = initialValue;
		_maximumLength = maximumLength;
		_submit = submit;
		_hideContents = hideContents;
		_cancel = cancel;
	}

	public override void OnInitialize()
	{
		UIPanel panel = new()
		{
			HAlign = 0.5f,
			VAlign = 0.5f,
			BackgroundColor = new Color(24, 31, 58) * 0.96f,
			BorderColor = new Color(93, 125, 213),
		};
		panel.Width.Set(700f, 0f);
		panel.Height.Set(250f, 0f);
		Append(panel);

		UIText prompt = new(_prompt, 0.8f, large: true) { HAlign = 0.5f };
		prompt.Top.Set(22f, 0f);
		panel.Append(prompt);

		_valueLabel = new UIText(string.Empty, 1f) { HAlign = 0.5f, TextOriginX = 0.5f };
		_valueLabel.Top.Set(105f, 0f);
		_valueLabel.Width.Set(-40f, 1f);
		panel.Append(_valueLabel);

		UIText help = new("Type text. Enter: accept    Escape: cancel    F1: help", 0.75f)
		{
			HAlign = 0.5f,
			TextColor = new Color(180, 192, 222),
		};
		help.Top.Set(190f, 0f);
		panel.Append(help);
		RefreshLabel();
	}

	public override void OnActivate()
	{
		_previousKeyboard = Keyboard.GetState();
		Main.clrInput();
		Main.GetInputText(_value);
		PlayerInput.WritingText = true;
		RefreshLabel();
		string contents = _hideContents ? $"{_value.Length} characters" : _value;
		AriadneMod.ScreenReader.Output($"{_prompt}. Edit field. {contents}. Press Enter to accept, Escape to cancel, or F1 for contextual help.");
	}

	public override void OnDeactivate()
	{
		PlayerInput.WritingText = false;
		base.OnDeactivate();
	}

	public override void Update(GameTime gameTime)
	{
		base.Update(gameTime);
		KeyboardState keyboard = Keyboard.GetState();
		if (Pressed(keyboard, Keys.F1))
		{
			SoundEngine.PlaySound(SoundID.MenuOpen);
			_controller.Navigate(new AccessibleContextHelpMenuState(
				_controller,
				_prompt,
				[
					new("Typing", "Type to add text at the end of this field. Terraria's normal text-entry sounds play for edits."),
					new("Backspace", "Delete the last character. Hold Backspace to continue deleting."),
					new("Control C, Control X, and Control V", "Copy, cut, or paste the field contents using the clipboard."),
					new("Enter", "Accept the current text and return when the value is valid."),
					new("Escape", "Cancel editing and return without applying this edit."),
					new("F1", "Open this help screen. Press F1 or Escape in help to return to the edit field."),
				]));
			_previousKeyboard = keyboard;
			return;
		}

		PlayerInput.WritingText = true;
		Main.instance.HandleIME();
		string oldValue = _value;
		_value = Main.GetInputText(_value);
		if (_value.Length > _maximumLength)
		{
			_value = _value[.._maximumLength];
		}

		if (_value != oldValue)
		{
			SoundEngine.PlaySound(SoundID.MenuTick);
			RefreshLabel();
			AnnounceEdit(oldValue, _value);
		}

		if (Pressed(keyboard, Keys.Escape))
		{
			SoundEngine.PlaySound(SoundID.MenuClose);
			if (_cancel is null)
			{
				_controller.Back();
			}
			else
			{
				_cancel();
			}
		}
		else if (Pressed(keyboard, Keys.Enter))
		{
			SoundEngine.PlaySound(SoundID.MenuClose);
			_submit(_value.Trim());
		}

		_previousKeyboard = keyboard;
	}

	private bool Pressed(KeyboardState keyboard, Keys key)
	{
		return keyboard.IsKeyDown(key) && _previousKeyboard.IsKeyUp(key);
	}

	private void RefreshLabel()
	{
		_valueLabel?.SetText(_hideContents ? new string('*', _value.Length) : _value);
	}

	private void AnnounceEdit(string oldValue, string newValue)
	{
		if (_hideContents)
		{
			AriadneMod.ScreenReader.Output($"{newValue.Length} characters.", interrupt: false);
			return;
		}

		if (newValue.Length == oldValue.Length + 1 && newValue.StartsWith(oldValue, StringComparison.Ordinal))
		{
			AriadneMod.ScreenReader.Output(newValue[^1].ToString(), interrupt: false);
		}
		else if (oldValue.Length == newValue.Length + 1 && oldValue.StartsWith(newValue, StringComparison.Ordinal))
		{
			AriadneMod.ScreenReader.Output($"Deleted {oldValue[^1]}", interrupt: false);
		}
		else
		{
			AriadneMod.ScreenReader.Output(newValue, interrupt: false);
		}
	}
}
