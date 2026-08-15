#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework.Input;

namespace Ariadne.Accessibility;

/// <summary>Turns XNA binding identifiers into names intended to be spoken aloud.</summary>
internal static class SpokenKeyName
{
	private static readonly Dictionary<string, string> Names = new(StringComparer.OrdinalIgnoreCase)
	{
		[nameof(Keys.OemSemicolon)] = "semicolon",
		[nameof(Keys.OemQuotes)] = "apostrophe",
		[nameof(Keys.OemComma)] = "comma",
		[nameof(Keys.OemPeriod)] = "period",
		[nameof(Keys.OemQuestion)] = "slash",
		[nameof(Keys.OemPipe)] = "backslash",
		[nameof(Keys.OemOpenBrackets)] = "left bracket",
		[nameof(Keys.OemCloseBrackets)] = "right bracket",
		[nameof(Keys.OemMinus)] = "minus",
		[nameof(Keys.OemPlus)] = "equals",
		[nameof(Keys.OemTilde)] = "grave accent",
		[nameof(Keys.Back)] = "Backspace",
		[nameof(Keys.Enter)] = "Enter",
		[nameof(Keys.Escape)] = "Escape",
		[nameof(Keys.Space)] = "Space",
		[nameof(Keys.Tab)] = "Tab",
		[nameof(Keys.Delete)] = "Delete",
		[nameof(Keys.PageUp)] = "Page Up",
		[nameof(Keys.PageDown)] = "Page Down",
		[nameof(Keys.LeftAlt)] = "Left Alt",
		[nameof(Keys.RightAlt)] = "Right Alt",
		[nameof(Keys.LeftControl)] = "Left Control",
		[nameof(Keys.RightControl)] = "Right Control",
		[nameof(Keys.LeftShift)] = "Left Shift",
		[nameof(Keys.RightShift)] = "Right Shift",
	};

	internal static string Format(string binding)
	{
		if (Names.TryGetValue(binding, out string? spoken))
		{
			return spoken;
		}

		return string.Concat(binding.Select((character, index) =>
			index > 0 && char.IsUpper(character) && char.IsLower(binding[index - 1])
				? $" {character}"
				: character.ToString()));
	}

	internal static string Format(Keys key) => Format(key.ToString());

	internal static string Join(IEnumerable<string> bindings, string separator = ", ") =>
		string.Join(separator, bindings.Select(Format));
}
