#nullable enable

using System;
using Terraria;
using Terraria.ModLoader;

namespace Ariadne.Audio;

/// <summary>
/// Synthesizes the two-part "lub-dub" cardiac cycle that Terraria has no native
/// sound for. One authored buffer is resampled at playback, so raising the pitch
/// for a worse wound also tightens the gap between the two thumps the way a
/// quickening pulse does.
/// </summary>
internal sealed class HeartbeatSound : IDisposable
{
	private const float BeatSeconds = 0.46f;
	private const float ReleaseSeconds = 0.004f;
	private const float AttackSeconds = 0.006f;
	private const float PitchSweepSeconds = 0.030f;
	private const float PitchDrop = 0.9f;
	private const float ThudDecaySeconds = 0.018f;

	private static readonly ThumpDesign FirstSound = new(
		StartSeconds: 0.000f,
		DurationSeconds: 0.200f,
		Frequency: 78f,
		DecaySeconds: 0.052f,
		Amplitude: 1.00f);

	private static readonly ThumpDesign SecondSound = new(
		StartSeconds: 0.235f,
		DurationSeconds: 0.165f,
		Frequency: 94f,
		DecaySeconds: 0.040f,
		Amplitude: 0.74f);

	private readonly AriadneAudioBus _bus;
	private readonly float[] _beat;
	private bool _disposed;

	private HeartbeatSound(AriadneAudioBus bus)
	{
		_bus = bus;
		_beat = CreateBeat();
	}

	internal static HeartbeatSound? Create(Mod owner)
	{
		if (Main.dedServ)
		{
			return null;
		}

		AriadneAudioBus? bus = AudioBusSystem.Bus;
		if (bus is null)
		{
			owner.Logger.Warn("The low-health heartbeat is unavailable because the audio bus could not be created.");
			return null;
		}

		return new(bus);
	}

	internal void Play(float volume, float pitch)
	{
		// Terraria's sound slider and the listening gate are the bus's job now.
		float scaledVolume = Math.Clamp(volume, 0f, 1f);
		if (_disposed || scaledVolume <= 0f)
		{
			return;
		}

		_bus.Add(new MonoOneShotVoice(
			_beat,
			FootstepSoundBank.PitchRatio(pitch),
			scaledVolume));
	}

	public void Dispose()
	{
		_disposed = true;
	}

	private static float[] CreateBeat()
	{
		int sampleRate = SpatialAudioTransformCalculator.SampleRate;
		int sampleCount = (int)MathF.Round(sampleRate * BeatSeconds);
		int releaseSamples = Math.Max(2, (int)MathF.Round(sampleRate * ReleaseSeconds));
		float[] samples = new float[sampleCount];
		uint noiseState = 0x5D2E_C41Bu;
		float filteredNoise = 0f;

		for (int i = 0; i < sampleCount; i++)
		{
			float time = i / (float)sampleRate;

			noiseState ^= noiseState << 13;
			noiseState ^= noiseState >> 17;
			noiseState ^= noiseState << 5;
			float noise = (noiseState & 0xFFFFu) / 32_767.5f - 1f;
			// Heavy smoothing keeps the noise component a chest thud rather than a hiss.
			filteredNoise = filteredNoise * 0.88f + noise * 0.12f;

			float release = i < sampleCount - releaseSamples
				? 1f
				: (sampleCount - 1f - i) / (releaseSamples - 1f);
			samples[i] =
				(Thump(FirstSound, time, filteredNoise) + Thump(SecondSound, time, filteredNoise)) *
				release;
		}

		AuthoredAudioLevels.NormalizeOneShot(samples);
		return samples;
	}

	private static float Thump(ThumpDesign design, float time, float noise)
	{
		float local = time - design.StartSeconds;
		if (local < 0f || local >= design.DurationSeconds)
		{
			return 0f;
		}

		float settledPitch = 1f - MathF.Exp(-local / PitchSweepSeconds);
		float sweptTime = local + PitchDrop * PitchSweepSeconds * settledPitch;
		float angle = MathF.Tau * design.Frequency * sweptTime;
		// Upper partials keep a sub-100 Hz pulse audible on speakers with no low end.
		float body =
			MathF.Sin(angle) +
			0.34f * MathF.Sin(angle * 2f) +
			0.12f * MathF.Sin(angle * 3f);
		float thud = 0.18f * noise * MathF.Exp(-local / ThudDecaySeconds);
		float attack = Math.Min(1f, local / AttackSeconds);
		float decay = MathF.Exp(-local / design.DecaySeconds);
		return design.Amplitude * attack * decay * (body + thud);
	}

	private readonly record struct ThumpDesign(
		float StartSeconds,
		float DurationSeconds,
		float Frequency,
		float DecaySeconds,
		float Amplitude);
}
