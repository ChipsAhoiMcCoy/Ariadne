#nullable enable

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.Audio;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;
using Ariadne.Accessibility;

namespace Ariadne.Ingame.Chat;

/// <summary>
/// Speaks chat messages as they arrive whether or not the chat window is open,
/// and replaces the window's Up and Down line scrolling with a reading cursor
/// that steps through received messages one at a time.
/// </summary>
[Autoload(Side = ModSide.Client)]
internal sealed class AccessibleChatSystem : ModSystem
{
	/// <summary>Matches the chat monitor's own visible line budget.</summary>
	private const int VisibleLines = 10;

	/// <summary>Reading cursor value meaning the edit field rather than a message.</summary>
	private const long EditFieldFocus = -1;

	private readonly List<ChatSpeechParts> _pendingAnnouncements = [];

	private bool _wasInWorld;
	private bool _wasChatOpen;
	private string _previousChatText = string.Empty;
	private KeyboardState _previousKeyboard;
	private long _focusedSequence = EditFieldFocus;
	private long _lastAnnouncedSequence = -1;

	public override void Load() => ChatMessageLog.Install();

	public override void OnWorldLoad() => ResetLog();

	public override void OnWorldUnload() => ResetLog();

	public override void PostUpdateEverything()
	{
		if (Main.dedServ)
		{
			return;
		}

		if (Main.gameMenu)
		{
			if (_wasInWorld)
			{
				_wasInWorld = false;
				ResetLog();
			}

			return;
		}

		_wasInWorld = true;
		KeyboardState keyboard = Keyboard.GetState();
		MonitorChatWindow(keyboard);
		AnnounceNewMessages();
		_previousKeyboard = keyboard;
	}

	private void MonitorChatWindow(KeyboardState keyboard)
	{
		if (Main.drawingPlayerChat && !_wasChatOpen)
		{
			_wasChatOpen = true;
			_previousChatText = Main.chatText;
			_focusedSequence = EditFieldFocus;
			Speak("Opened");
		}
		else if (!Main.drawingPlayerChat && _wasChatOpen)
		{
			// Terraria clears this flag at the top of every GetInputText pass and only
			// leaves it set on the frame the edit field received Enter, so it separates
			// sending a message from cancelling out of the window with Escape.
			bool submitted = Main.inputTextEnter;
			ResetChatWindowState();
			if (!submitted)
			{
				// Escape only closes the window; text entry stops suppressing keyboard
				// bindings on the next frame, so a still-held Escape reaches the
				// Inventory binding and opens the inventory behind the closed chat.
				AccessibleInputSuppression.BlockKeyUntilReleased(Keys.Escape);
				// Sending a message plays MenuClose on the way out, but the Escape path
				// only clears the flag, leaving the dismissal with no audible cue.
				SoundEngine.PlaySound(SoundID.MenuClose);
				Speak("Closed");
			}
			return;
		}

		if (!Main.drawingPlayerChat)
		{
			return;
		}

		if (ContextHelpChord.Pressed(keyboard, _previousKeyboard))
		{
			Speak("Help");
		}
		else if (Pressed(keyboard, Keys.Up))
		{
			FocusOlderMessage();
		}
		else if (Pressed(keyboard, Keys.Down))
		{
			FocusNewerMessage();
		}

		// Terraria offsets the visible chat every frame an arrow key is held.
		// Reapplying the reading cursor's own offset afterwards keeps the window
		// following the spoken message instead of scrolling on its own.
		SyncVisibleScroll();
		AnnounceEditedText();
	}

	private void FocusOlderMessage()
	{
		if (!ChatMessageLog.TryGetRange(out long oldest, out long newest))
		{
			Speak("NoMessages");
			return;
		}

		long target = _focusedSequence == EditFieldFocus ? newest : _focusedSequence - 1;
		if (target < oldest)
		{
			_focusedSequence = oldest;
			AnnounceFocusedMessage("OldestMessage");
			return;
		}

		_focusedSequence = target;
		AnnounceFocusedMessage();
	}

	private void FocusNewerMessage()
	{
		if (_focusedSequence == EditFieldFocus)
		{
			return;
		}

		if (!ChatMessageLog.TryGetRange(out long oldest, out long newest))
		{
			FocusEditField();
			return;
		}

		long target = Math.Max(_focusedSequence + 1, oldest);
		if (target > newest)
		{
			FocusEditField();
			return;
		}

		_focusedSequence = target;
		AnnounceFocusedMessage();
	}

	private void FocusEditField()
	{
		_focusedSequence = EditFieldFocus;
		string text = Main.chatText;
		AriadneMod.ScreenReader.Output(Language.GetTextValue(
			"Mods.Ariadne.Chat.EditField",
			text.Length == 0 ? Language.GetTextValue("Mods.Ariadne.Chat.EditFieldEmpty") : text));
	}

	private void AnnounceFocusedMessage(string? prefixKey = null)
	{
		if (!ChatMessageLog.TryGetParts(_focusedSequence, out ChatSpeechParts parts))
		{
			return;
		}

		string text = ChatSpeechFormatter.Compose(parts);
		AriadneMod.ScreenReader.Output(prefixKey is null
			? text
			: Language.GetTextValue($"Mods.Ariadne.Chat.{prefixKey}", text));
	}

	private void AnnounceEditedText()
	{
		string value = Main.chatText;
		if (value == _previousChatText)
		{
			return;
		}

		string oldValue = _previousChatText;
		_previousChatText = value;
		if (value.Length == oldValue.Length + 1 && value.StartsWith(oldValue, StringComparison.Ordinal))
		{
			AriadneMod.ScreenReader.Output(value[^1].ToString(), interrupt: false);
		}
		else if (oldValue.Length == value.Length + 1 && oldValue.StartsWith(value, StringComparison.Ordinal))
		{
			AriadneMod.ScreenReader.Output(
				Language.GetTextValue("Mods.Ariadne.Chat.CharacterDeleted", oldValue[^1]),
				interrupt: false);
		}
		else
		{
			AriadneMod.ScreenReader.Output(
				value.Length == 0 ? Language.GetTextValue("Mods.Ariadne.Chat.EditFieldEmpty") : value,
				interrupt: false);
		}
	}

	private void AnnounceNewMessages()
	{
		_pendingAnnouncements.Clear();
		_lastAnnouncedSequence = ChatMessageLog.CopyPartsAfter(_lastAnnouncedSequence, _pendingAnnouncements);
		foreach (ChatSpeechParts parts in _pendingAnnouncements)
		{
			// Arriving messages queue behind whatever is being read so they never
			// cut off a navigated message or a typed character.
			AriadneMod.ScreenReader.Output(ChatSpeechFormatter.Compose(parts), interrupt: false);
		}

		_pendingAnnouncements.Clear();
	}

	private void SyncVisibleScroll()
	{
		int lines = 0;
		if (_focusedSequence != EditFieldFocus && ChatMessageLog.TryGetRange(out _, out long newest))
		{
			lines = (int)Math.Clamp(newest - _focusedSequence - (VisibleLines - 1), 0, int.MaxValue);
		}

		Main.chatMonitor.ResetOffset();
		if (lines > 0)
		{
			Main.chatMonitor.Offset(lines);
		}
	}

	private bool Pressed(KeyboardState keyboard, Keys key)
	{
		return keyboard.IsKeyDown(key) && _previousKeyboard.IsKeyUp(key);
	}

	private static void Speak(string key)
	{
		AriadneMod.ScreenReader.Output(Language.GetTextValue($"Mods.Ariadne.Chat.{key}"));
	}

	private void ResetLog()
	{
		_wasInWorld = false;
		ChatMessageLog.Clear();
		_lastAnnouncedSequence = ChatMessageLog.LastRecordedSequence;
		ResetChatWindowState();
	}

	private void ResetChatWindowState()
	{
		_wasChatOpen = false;
		_previousChatText = string.Empty;
		_previousKeyboard = Keyboard.GetState();
		_focusedSequence = EditFieldFocus;
	}
}
