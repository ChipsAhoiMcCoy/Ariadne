#nullable enable

using System;
using Microsoft.Xna.Framework.Audio;
using Terraria;
using Terraria.Audio;

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

	private readonly SoundEffect[] _tones;
	private int _lastToneIndex = -1;
	private bool _disposed;

	private FootstepSoundBank()
	{
		_tones = new SoundEffect[Designs.Length];
		for (int i = 0; i < Designs.Length; i++)
		{
			_tones[i] = ImpactToneSynthesizer.Render(Designs[i]);
		}
	}

	internal static FootstepSoundBank? Create()
	{
		return Main.dedServ || !SoundEngine.IsAudioSupported ? null : new FootstepSoundBank();
	}

	internal void Play(float volume, float pitch)
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
		_tones[nextToneIndex].Play(scaledVolume, Math.Clamp(pitch, -1f, 1f), pan: 0f);
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
}
