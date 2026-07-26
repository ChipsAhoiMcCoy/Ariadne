#nullable enable

using System;
using Microsoft.Xna.Framework.Audio;
using Terraria;
using Terraria.Audio;

namespace Ariadne.Audio;

/// <summary>
/// A single unvarying thud for blocked movement. Holding one design instead of
/// the footstep bank's four, below their pitch and with the tone damped into a
/// dull knock, keeps a bump distinguishable from a step even when the two arrive
/// in the same stride. Length carries most of that contrast: a bump rings for
/// roughly three times a step so the two do not rely on pitch alone, which lets
/// the fundamental stay high enough for small speakers to reproduce it.
/// </summary>
internal sealed class WallBumpSoundBank : IDisposable
{
	private static readonly ImpactToneDesign Design =
		new(146f, 130f, 0.050f, 0.20f, 0.32f, 65f, 0.45f, 0x4C1D_93E7u);

	private readonly SoundEffect _tone;
	private bool _disposed;

	private WallBumpSoundBank()
	{
		_tone = ImpactToneSynthesizer.Render(Design);
	}

	internal static WallBumpSoundBank? Create()
	{
		return Main.dedServ || !SoundEngine.IsAudioSupported ? null : new WallBumpSoundBank();
	}

	internal void Play(float volume, float pitch)
	{
		float scaledVolume = Math.Clamp(volume * Main.soundVolume, 0f, 1f);
		if (_disposed || scaledVolume <= 0f || SoundEngine.AreSoundsPaused)
		{
			return;
		}

		_tone.Play(scaledVolume, Math.Clamp(pitch, -1f, 1f), pan: 0f);
	}

	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		_tone.Dispose();
		_disposed = true;
	}
}
