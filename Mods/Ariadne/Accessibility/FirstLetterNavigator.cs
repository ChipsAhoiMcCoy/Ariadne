#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Xna.Framework.Input;

namespace Ariadne.Accessibility;

internal static class FirstLetterNavigator
{
	internal static bool TryGetPressedLetter(
		KeyboardState keyboard,
		KeyboardState previousKeyboard,
		out Keys key,
		out char letter)
	{
		if (HasCommandModifier(keyboard))
		{
			key = default;
			letter = default;
			return false;
		}

		for (int value = (int)Keys.A; value <= (int)Keys.Z; value++)
		{
			Keys candidate = (Keys)value;
			if (keyboard.IsKeyDown(candidate) && previousKeyboard.IsKeyUp(candidate))
			{
				key = candidate;
				letter = (char)('A' + value - (int)Keys.A);
				return true;
			}
		}

		key = default;
		letter = default;
		return false;
	}

	internal static int FindNextIndex<T>(
		IReadOnlyList<T> entries,
		int currentIndex,
		char letter,
		Func<T, string?> getNavigationName)
	{
		CompareInfo comparer = CultureInfo.CurrentCulture.CompareInfo;
		string prefix = char.ToUpperInvariant(letter).ToString();
		List<FirstLetterMatch> matches = [];
		for (int index = 0; index < entries.Count; index++)
		{
			string? name = getNavigationName(entries[index]);
			if (string.IsNullOrWhiteSpace(name))
			{
				continue;
			}

			name = name.TrimStart();
			if (comparer.IsPrefix(name, prefix, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace))
			{
				matches.Add(new FirstLetterMatch(index, name));
			}
		}

		if (matches.Count == 0)
		{
			return -1;
		}

		matches.Sort((left, right) =>
		{
			int comparison = comparer.Compare(
				left.Name,
				right.Name,
				CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace);
			return comparison != 0 ? comparison : left.Index.CompareTo(right.Index);
		});

		int currentMatch = matches.FindIndex(match => match.Index == currentIndex);
		return currentMatch < 0
			? matches[0].Index
			: matches[(currentMatch + 1) % matches.Count].Index;
	}

	private static bool HasCommandModifier(KeyboardState keyboard)
	{
		return keyboard.IsKeyDown(Keys.LeftControl) ||
			keyboard.IsKeyDown(Keys.RightControl) ||
			keyboard.IsKeyDown(Keys.LeftAlt) ||
			keyboard.IsKeyDown(Keys.RightAlt) ||
			keyboard.IsKeyDown(Keys.LeftWindows) ||
			keyboard.IsKeyDown(Keys.RightWindows);
	}

	private readonly record struct FirstLetterMatch(int Index, string Name);
}
