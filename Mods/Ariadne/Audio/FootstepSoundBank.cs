#nullable enable

using System;
using Microsoft.Xna.Framework.Audio;
using Terraria;
using Terraria.Audio;

namespace Ariadne.Audio;

internal sealed class FootstepSoundBank : IDisposable
{
	private const int SampleRate = 44_100;
	private const float ImpactDecaySeconds = 0.0012f;
	private const float PitchSweepSeconds = 0.008f;

	private static readonly ToneDesign[] Designs =
	[
		new(180f, 40.0f, 0.180f, 0.135f, 0.18f, 25.0f, 0.120f, 0x16A3_7421u),
		new(186f, 39.5f, 0.185f, 0.130f, 0.22f, 25.5f, 0.115f, 0xB529_7A4Du),
		new(192f, 39.0f, 0.190f, 0.125f, 0.26f, 26.0f, 0.110f, 0x68E3_1DA4u),
		new(198f, 38.5f, 0.195f, 0.120f, 0.30f, 26.5f, 0.105f, 0x9C71_53B2u),
	];

	private readonly SoundEffect[] _tones;
	private int _lastToneIndex = -1;
	private bool _disposed;

	private FootstepSoundBank()
	{
		_tones = new SoundEffect[Designs.Length];
		for (int i = 0; i < Designs.Length; i++)
		{
			_tones[i] = CreateTone(Designs[i]);
		}
	}

	internal static FootstepSoundBank? Create()
	{
		return Main.dedServ || !SoundEngine.IsAudioSupported ? null : new FootstepSoundBank();
	}

	internal void Play(float volume)
	{
		float scaledVolume = Math.Clamp(volume * Main.soundVolume, 0f, 1f);
		if (_disposed || scaledVolume <= 0f || SoundEngine.AreSoundsPaused)
		{
			return;
		}

		int nextToneIndex = Random.Shared.Next(_tones.Length - 1);
		if (nextToneIndex >= _lastToneIndex)
		{
			nextToneIndex++;
		}

		_lastToneIndex = nextToneIndex;
		_tones[nextToneIndex].Play(scaledVolume, pitch: 0f, pan: 0f);
	}

	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		foreach (SoundEffect tone in _tones)
		{
			tone.Dispose();
		}

		_disposed = true;
	}

	private static SoundEffect CreateTone(ToneDesign design)
	{
		int sampleCount = (int)MathF.Round(SampleRate * design.DurationMilliseconds / 1_000f);
		int attackSamples = Math.Max(1, (int)MathF.Round(SampleRate * design.AttackMilliseconds / 1_000f));
		int releaseSamples = Math.Max(2, (int)MathF.Round(SampleRate * 0.9f / 1_000f));
		byte[] pcm = new byte[sampleCount * sizeof(short)];
		uint noiseState = design.NoiseSeed;
		float peak = 0.72f + design.Brightness + design.Texture + 0.10f;
		float phaseOffset = (design.NoiseSeed & 0xFFu) / 255f * MathF.Tau;
		float filteredNoise = 0f;
		float attackSeconds = design.AttackMilliseconds / 1_000f;
		float bodyDecaySeconds = design.BodyDecayMilliseconds / 1_000f;

		for (int i = 0; i < sampleCount; i++)
		{
			float time = i / (float)SampleRate;
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
			float sample = Math.Clamp(
				(body * bodyEnvelope + impact) / peak * AuthoredAudioLevels.NormalizedOneShotPeak * release,
				-1f,
				1f);
			short encoded = (short)MathF.Round(sample * short.MaxValue);
			pcm[i * 2] = (byte)encoded;
			pcm[i * 2 + 1] = (byte)(encoded >> 8);
		}

		return new SoundEffect(pcm, SampleRate, AudioChannels.Mono);
	}

	private readonly record struct ToneDesign(
		float Frequency,
		float DurationMilliseconds,
		float Brightness,
		float Texture,
		float AttackMilliseconds,
		float BodyDecayMilliseconds,
		float PitchDrop,
		uint NoiseSeed);
}
