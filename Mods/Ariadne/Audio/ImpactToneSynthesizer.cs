#nullable enable

using System;

namespace Ariadne.Audio;

internal readonly record struct ImpactToneDesign(
	float Frequency,
	float DurationMilliseconds,
	float Brightness,
	float Texture,
	float AttackMilliseconds,
	float BodyDecayMilliseconds,
	float PitchDrop,
	uint NoiseSeed);

/// <summary>
/// Renders the short percussive tones Ariadne uses for movement feedback.
/// Footsteps and blocked-movement bumps share one voice so a bump is heard as
/// the same family of sound, and only the design separates them.
///
/// A design describes shape, not level: the finished buffer is normalized to the
/// shared reference loudness, so lengthening or brightening a design cannot move
/// it out of balance with the other cues.
/// </summary>
internal static class ImpactToneSynthesizer
{
	private const float ImpactDecaySeconds = 0.0012f;
	private const float PitchSweepSeconds = 0.008f;

	internal static float[] Render(in ImpactToneDesign design)
	{
		int sampleRate = SpatialAudioTransformCalculator.SampleRate;
		int sampleCount = (int)MathF.Round(sampleRate * design.DurationMilliseconds / 1_000f);
		int attackSamples = Math.Max(1, (int)MathF.Round(sampleRate * design.AttackMilliseconds / 1_000f));
		int releaseSamples = Math.Max(2, (int)MathF.Round(sampleRate * 0.9f / 1_000f));
		float[] samples = new float[sampleCount];
		uint noiseState = design.NoiseSeed;
		float phaseOffset = (design.NoiseSeed & 0xFFu) / 255f * MathF.Tau;
		float filteredNoise = 0f;
		float attackSeconds = design.AttackMilliseconds / 1_000f;
		float bodyDecaySeconds = design.BodyDecayMilliseconds / 1_000f;

		for (int i = 0; i < sampleCount; i++)
		{
			float time = i / (float)sampleRate;
			float settledPitch = 1f - MathF.Exp(-time / PitchSweepSeconds);
			float sweptTime = time + design.PitchDrop * PitchSweepSeconds * settledPitch;
			float angle = MathF.Tau * design.Frequency * sweptTime;
			float attack = Math.Min(1f, (i + 1f) / attackSamples);
			float timeAfterAttack = Math.Max(0f, time - attackSeconds);
			float bodyEnvelope = attack * MathF.Exp(-3f * timeAfterAttack / bodyDecaySeconds);
			float impactEnvelope = attack * MathF.Exp(-time / ImpactDecaySeconds);
			float release = i < sampleCount - releaseSamples
				? 1f
				: (sampleCount - 1f - i) / (releaseSamples - 1f);

			noiseState ^= noiseState << 13;
			noiseState ^= noiseState >> 17;
			noiseState ^= noiseState << 5;
			float noise = (noiseState & 0xFFFFu) / 32_767.5f - 1f;
			filteredNoise = filteredNoise * 0.35f + noise * 0.65f;

			float body =
				0.72f * MathF.Sin(angle) +
				design.Brightness * MathF.Sin(angle * 3.05f + phaseOffset);
			float impact = impactEnvelope *
				(design.Texture * filteredNoise + 0.10f * MathF.Sin(angle * 4.7f + phaseOffset));
			samples[i] = (body * bodyEnvelope + impact) * release;
		}

		AuthoredAudioLevels.NormalizeOneShot(samples);
		return samples;
	}
}
