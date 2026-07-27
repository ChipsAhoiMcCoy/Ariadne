#nullable enable

using Microsoft.Xna.Framework.Input;

namespace Ariadne.Accessibility;

/// <summary>
/// The Alt plus H chord that opens contextual help. Screens that own the keyboard read it
/// straight off <see cref="Keyboard"/> like the rest of their navigation, rather than through a
/// mod keybind, because they update after input has already been folded into the trigger sets.
/// </summary>
internal static class ContextHelpChord
{
	internal const string Name = "Alt plus H";

	internal const string ShortName = "Alt+H";

	internal static bool ModifierHeld(KeyboardState keyboard)
	{
		return keyboard.IsKeyDown(Keys.LeftAlt) || keyboard.IsKeyDown(Keys.RightAlt);
	}

	internal static bool Pressed(KeyboardState keyboard, KeyboardState previousKeyboard)
	{
		return ModifierHeld(keyboard) && keyboard.IsKeyDown(Keys.H) && previousKeyboard.IsKeyUp(Keys.H);
	}
}
