#nullable enable

using System.Collections.Generic;
using Microsoft.Xna.Framework.Input;
using Terraria.GameInput;

namespace Ariadne.Ingame;

internal static class AccessibleInputSuppression
{
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
