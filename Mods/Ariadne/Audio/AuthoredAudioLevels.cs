#nullable enable

using System;

namespace Ariadne.Audio;

/// <summary>
/// The single loudness every Ariadne cue is levelled against, so one slider
/// percentage means one loudness no matter which cue it belongs to.
///
/// Cues are matched by measured loudness rather than by peak. Matching peaks left
/// a footstep roughly ten decibels under a wall bump, because a 40 ms transient
/// and a 130 ms knock that reach the same peak do not reach the same loudness.
/// </summary>
internal static class AuthoredAudioLevels
{
	/// <summary>
	/// The highest peak a normalized one-shot may reach. Loudness matching decides
	/// the gain; this only stops a very short cue from being pushed into clipping,
	/// at the cost of that cue falling short of the reference.
	/// </summary>
	internal const float NormalizedOneShotPeak = 0.70f;

	/// <summary>
	/// The same ceiling for a voice that is encoded after the spatializer's pan. The
	/// pan never passes a voice on whole, so this sits higher and stops a loud short
	/// clip such as a tile hit from being held under the reference for no reason.
	/// </summary>
	internal const float NormalizedSpatialVoicePeak = 0.95f;

	/// <summary>
	/// The loudness, in LUFS, every cue reaches at a slider of 100%. It sits a few
	/// decibels under Terraria's own gameplay sounds, which measure about -21 to
	/// -25 LUFS, so a cue reads over the game without covering it.
	/// </summary>
	internal const float ReferenceLoudness = -25f;

	/// <summary>
	/// The reference for a voice that reaches the listener through the spatializer.
	/// Its equal-power pan puts only part of a centered voice into each channel, so
	/// such a voice is normalized this much louder to arrive at the reference.
	/// </summary>
	internal static readonly float SpatialVoiceReferenceLoudness =
		ReferenceLoudness - 20f * MathF.Log10(SpatialAudioTransformCalculator.CenteredChannelGain);

	/// <summary>
	/// The gain that brings a rendered buffer to the given reference loudness.
	/// Silence is left alone.
	/// </summary>
	internal static float LoudnessTrim(ReadOnlySpan<float> samples, float referenceLoudness)
	{
		float measured = AudioLoudness.Measure(samples);
		return float.IsFinite(measured)
			? MathF.Pow(10f, (referenceLoudness - measured) / 20f)
			: 1f;
	}

	/// <summary>
	/// The gain that brings a buffer to the reference loudness without carrying its
	/// peak past the given ceiling. A cue that cannot reach the reference under that
	/// ceiling stays quieter rather than clipping.
	/// </summary>
	internal static float PeakLimitedTrim(
		ReadOnlySpan<float> samples,
		float referenceLoudness,
		float peakCeiling)
	{
		float trim = LoudnessTrim(samples, referenceLoudness);
		float peak = AudioLoudness.Peak(samples);
		return peak * trim > peakCeiling ? peakCeiling / peak : trim;
	}

	/// <summary>Scales a one-shot buffer to the reference loudness in place.</summary>
	internal static void NormalizeOneShot(
		Span<float> samples,
		float referenceLoudness = ReferenceLoudness,
		float peakCeiling = NormalizedOneShotPeak)
	{
		float trim = PeakLimitedTrim(samples, referenceLoudness, peakCeiling);
		for (int index = 0; index < samples.Length; index++)
		{
			samples[index] = Math.Clamp(samples[index] * trim, -1f, 1f);
		}
	}

	internal static byte[] EncodeMono(ReadOnlySpan<float> samples)
	{
		byte[] pcm = new byte[samples.Length * sizeof(short)];
		for (int index = 0; index < samples.Length; index++)
		{
			short encoded = (short)MathF.Round(
				Math.Clamp(samples[index], -1f, 1f) * short.MaxValue);
			pcm[index * 2] = (byte)encoded;
			pcm[index * 2 + 1] = (byte)(encoded >> 8);
		}
		return pcm;
	}
}
