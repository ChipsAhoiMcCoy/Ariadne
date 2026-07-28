#nullable enable

using System.Collections.Generic;
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.GameInput;

namespace Ariadne.Ingame;

internal static class AccessibleInputSuppression
{
	private static bool _cursorModeSuppressed;

	/// <summary>
	/// True while the Smart Cursor trigger is being withheld from gameplay. Systems that
	/// predict the next cursor mode must treat the trigger as unpressed for as long as this
	/// holds, because trigger clearing only affects the systems that read after it.
	/// </summary>
	internal static bool IsCursorModeSuppressed => _cursorModeSuppressed;

	/// <summary>
	/// Withholds the Smart Cursor trigger while an Ariadne menu owns the keyboard, so the
	/// modifier a menu chord uses cannot flip the cursor mode underneath it.
	/// <c>Player.TryToToggleSmartCursor</c> acts on a press edge guarded by its own
	/// <c>releaseSmart</c> latch, and clearing the trigger re-arms that latch. Suppression
	/// therefore has to outlive the menu until the key is physically released, or the
	/// swallowed toggle simply fires on the frame the menu closes.
	/// </summary>
	internal static void UpdateCursorModeSuppression(bool menuOwnsKeyboard)
	{
		if (menuOwnsKeyboard)
		{
			_cursorModeSuppressed = true;
		}
		else if (_cursorModeSuppressed && !PlayerInput.Triggers.Current.SmartCursor)
		{
			_cursorModeSuppressed = false;
		}

		if (!_cursorModeSuppressed)
		{
			return;
		}

		PlayerInput.Triggers.Current.SmartCursor = false;
		PlayerInput.Triggers.JustPressed.SmartCursor = false;
		PlayerInput.Triggers.JustReleased.SmartCursor = false;
	}

	internal static void ResetCursorModeSuppression()
	{
		_cursorModeSuppressed = false;
	}

	/// <summary>
	/// Applies Terraria's own held-key latch so a key that just dismissed one screen does
	/// not go on to fire its normal binding. <c>PlayerInput.KeyboardInput</c> strips the
	/// blocked key from the pressed set every frame and clears the latch once the key is
	/// physically released. Vanilla uses this for sign-edit cancellation.
	/// </summary>
	internal static void BlockKeyUntilReleased(Keys key)
	{
		Main.blockKey = key.ToString();
	}

	/// <summary>
	/// Clears the triggers behind every key an Ariadne menu treats as its own. Menus read the
	/// keyboard directly, but <c>Main.CanPauseGame</c> never pauses for a custom
	/// <c>IngameFancyUI</c> state, so <c>Player.Update</c> keeps consuming triggers underneath
	/// them. Letters cover menu navigation and Alt chords; function keys cover the loadout
	/// swaps that would otherwise fire alongside a help request.
	/// </summary>
	internal static void ConsumeMenuNavigationAndLetterTriggers(KeyboardState keyboard)
	{
		TriggersSet current = PlayerInput.Triggers.Current;
		TriggersSet justPressed = PlayerInput.Triggers.JustPressed;
		current.MapStyle = false;
		justPressed.MapStyle = false;
		current.MenuUp = current.MenuDown = current.MenuLeft = current.MenuRight = false;
		justPressed.MenuUp = justPressed.MenuDown = justPressed.MenuLeft = justPressed.MenuRight = false;

		if (!PlayerInput.CurrentProfile.InputModes.TryGetValue(InputMode.Keyboard, out KeyConfiguration? bindings))
		{
			return;
		}

		ConsumeKeyRange(keyboard, bindings, current, justPressed, Keys.A, Keys.Z);
		ConsumeKeyRange(keyboard, bindings, current, justPressed, Keys.F1, Keys.F12);
	}

	private static void ConsumeKeyRange(
		KeyboardState keyboard,
		KeyConfiguration bindings,
		TriggersSet current,
		TriggersSet justPressed,
		Keys first,
		Keys last)
	{
		for (int value = (int)first; value <= (int)last; value++)
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

				if (current.KeyStatus.ContainsKey(triggerName))
				{
					current.KeyStatus[triggerName] = false;
				}
				if (justPressed.KeyStatus.ContainsKey(triggerName))
				{
					justPressed.KeyStatus[triggerName] = false;
				}
			}
		}
	}

	internal static void ConsumeMovementTriggers()
	{
		TriggersSet current = PlayerInput.Triggers.Current;
		TriggersSet justPressed = PlayerInput.Triggers.JustPressed;
		current.Up = current.Down = current.Left = current.Right = current.Jump = false;
		justPressed.Up = justPressed.Down = justPressed.Left = justPressed.Right = justPressed.Jump = false;
	}
}
