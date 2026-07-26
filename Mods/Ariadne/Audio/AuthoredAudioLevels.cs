#nullable enable

namespace Ariadne.Audio;

/// <summary>
/// Shared source-level target for Ariadne-authored one-shot sounds.
/// Playback-specific gain and user configuration are applied after normalization.
/// </summary>
internal static class AuthoredAudioLevels
{
	internal const float NormalizedOneShotPeak = 0.70f;
}
