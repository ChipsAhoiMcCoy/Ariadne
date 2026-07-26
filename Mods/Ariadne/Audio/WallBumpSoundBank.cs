#nullable enable

using System;
using Microsoft.Xna.Framework.Audio;
using Terraria;
using Terraria.Audio;

namespace Ariadne.Audio;

/// <summary>
/// A single unvarying thud for blocked movement. Holding one design instead of
/// the footstep bank's four, well below their pitch and with the tone damped
/// into a dull knock, keeps a bump distinguishable from a step even when the
/// two arrive in the same stride.
/// </summary>
internal sealed class WallBumpSoundBank : IDisposable
{
	private static readonly ImpactToneDesign Design =
		new(104f, 78f, 0.045f, 0.26f, 0.32f, 40f, 0.55f, 0x4C1D_93E7u);

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

	internal void Play(float volume, float pan, float pitch)
	{
		float scaledVolume = Math.Clamp(volume * Main.soundVolume, 0f, 1f);
		if (_disposed || scaledVolume <= 0f || SoundEngine.AreSoundsPaused)
		{
			return;
		}

		_tone.Play(
			scaledVolume,
			Math.Clamp(pitch, -1f, 1f),
			Math.Clamp(pan, -1f, 1f));
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
