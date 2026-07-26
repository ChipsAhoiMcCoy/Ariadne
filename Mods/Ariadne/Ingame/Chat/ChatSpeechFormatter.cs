#nullable enable

using System.Text.RegularExpressions;
using Terraria.Localization;
using Ariadne.Accessibility;

namespace Ariadne.Ingame.Chat;

/// <summary>
/// A chat message reduced to a speaker and spoken body. Parsing is separated
/// from composition because messages are recorded on whichever thread the
/// network module happens to run on, while the localized wording is only read
/// on the main thread.
/// </summary>
internal readonly record struct ChatSpeechParts(string Speaker, string Body)
{
	internal bool IsEmpty => Speaker.Length == 0 && Body.Length == 0;
}

/// <summary>
/// Reduces a chat message to spoken text, keeping the speaker that Terraria
/// carries as a name tag. ChatHelper.DisplayMessage prefixes every authored
/// message with NameTagHandler.GenerateTag, which the shared formatter would
/// otherwise drop along with the decorative tags.
/// </summary>
internal static class ChatSpeechFormatter
{
	private static readonly Regex NameTag = new(@"\[n:((?:\\.|[^\]])*)\]", RegexOptions.Compiled);
	private static readonly Regex LeadingNameTag = new(@"^\s*\[n:((?:\\.|[^\]])*)\]\s*", RegexOptions.Compiled);

	/// <summary>Thread-safe. Uses only string and regex work.</summary>
	internal static ChatSpeechParts Parse(string? value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return new ChatSpeechParts(string.Empty, string.Empty);
		}

		Match speaker = LeadingNameTag.Match(value);
		string remainder = speaker.Success ? value[speaker.Length..] : value;
		// Names quoted mid-message still carry meaning, so substitute them before
		// the shared formatter removes every remaining tag.
		string body = SpeechTextFormatter.Flatten(
			NameTag.Replace(remainder, match => UnescapeName(match.Groups[1].Value)));
		return new ChatSpeechParts(
			speaker.Success ? UnescapeName(speaker.Groups[1].Value) : string.Empty,
			body);
	}

	/// <summary>Main thread only. Reads localized wording.</summary>
	internal static string Compose(ChatSpeechParts parts)
	{
		if (parts.Speaker.Length == 0)
		{
			return parts.Body;
		}

		return parts.Body.Length == 0
			? parts.Speaker
			: Language.GetTextValue("Mods.Ariadne.Chat.MessageFromPlayer", parts.Speaker, parts.Body);
	}

	private static string UnescapeName(string name)
	{
		return name.Replace("\\[", "[").Replace("\\]", "]");
	}
}
