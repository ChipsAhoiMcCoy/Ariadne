#nullable enable

using System;
using Terraria;
using Terraria.ModLoader;

namespace Ariadne.Audio;

internal sealed class FootstepSoundBank : IDisposable
{
	private static readonly ImpactToneDesign[] Designs =
	[
		new(180f, 40.0f, 0.180f, 0.135f, 0.18f, 25.0f, 0.120f, 0x16A3_7421u),
		new(186f, 39.5f, 0.185f, 0.130f, 0.22f, 25.5f, 0.115f, 0xB529_7A4Du),
		new(192f, 39.0f, 0.190f, 0.125f, 0.26f, 26.0f, 0.110f, 0x68E3_1DA4u),
		new(198f, 38.5f, 0.195f, 0.120f, 0.30f, 26.5f, 0.105f, 0x9C71_53B2u),
	];

	private readonly AriadneAudioBus _bus;
	private readonly float[][] _tones;
	private int _lastToneIndex = -1;
	private bool _disposed;

	private FootstepSoundBank(AriadneAudioBus bus)
	{
		_bus = bus;
		_tones = new float[Designs.Length][];
		for (int i = 0; i < Designs.Length; i++)
		{
			_tones[i] = ImpactToneSynthesizer.Render(Designs[i]);
		}
	}

	internal static FootstepSoundBank? Create(Mod owner)
	{
		if (Main.dedServ)
		{
			return null;
		}

		AriadneAudioBus? bus = AudioBusSystem.Bus;
		if (bus is null)
		{
			owner.Logger.Warn("Footsteps are unavailable because the audio bus could not be created.");
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

		int nextToneIndex = Random.Shared.Next(_tones.Length - 1);
		if (nextToneIndex >= _lastToneIndex)
		{
			nextToneIndex++;
		}

		_lastToneIndex = nextToneIndex;
		_bus.Add(new MonoOneShotVoice(
			_tones[nextToneIndex],
			PitchRatio(pitch),
			scaledVolume));
	}

	/// <summary>
	/// Terraria's pitch parameter is octaves, the same as the one these cues used when
	/// they were played through a sound effect.
	/// </summary>
	internal static float PitchRatio(float pitch)
	{
		return MathF.Pow(2f, Math.Clamp(pitch, -1f, 1f));
	}

	public void Dispose()
	{
		_disposed = true;
	}
}
