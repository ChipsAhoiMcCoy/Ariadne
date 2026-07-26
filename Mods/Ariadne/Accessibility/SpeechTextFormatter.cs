#nullable enable

using System;
using System.Text.RegularExpressions;

namespace Ariadne.Accessibility;

/// <summary>
/// Reduces Terraria and mod supplied display text to a single spoken line.
/// Color and chat tags carry no meaning through speech, and the line breaks
/// used for on-screen wrapping are not sentence boundaries.
/// </summary>
internal static class SpeechTextFormatter
{
	private static readonly Regex ColorTag = new(@"\[c/[0-9A-Fa-f]{6}:(.*?)\]", RegexOptions.Compiled);
	private static readonly Regex ChatTag = new(@"\[[^\]]+:[^\]]*\]", RegexOptions.Compiled);

	internal static string Flatten(string? value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return string.Empty;
		}

		string flattened = ColorTag.Replace(value, "$1");
		flattened = ChatTag.Replace(flattened, string.Empty)
			.Replace('\r', ' ')
			.Replace('\n', ' ')
			.Trim();
		while (flattened.Contains("  ", StringComparison.Ordinal))
		{
			flattened = flattened.Replace("  ", " ", StringComparison.Ordinal);
		}
		return flattened;
	}
}
