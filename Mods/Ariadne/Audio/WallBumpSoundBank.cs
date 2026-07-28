#nullable enable

using System;
using Microsoft.Xna.Framework.Audio;
using Terraria;
using Terraria.Audio;

namespace Ariadne.Audio;

/// <summary>What the blocked movement ran into.</summary>
internal enum MovementBumpSurface
{
	Terrain,
	Door,
}

/// <summary>
/// The thuds for blocked movement. Terrain holds one design instead of the
/// footstep bank's four, below their pitch and with the tone damped into a dull
/// knock, which keeps a bump distinguishable from a step even when the two arrive
/// in the same stride. Length carries most of that contrast: a bump rings for
/// roughly three times a step so the two do not rely on pitch alone, which lets
/// the fundamental stay high enough for small speakers to reproduce it.
///
/// A door answers a fifth higher and dies away faster, with the partial pushed up
/// and the noise pulled back so it reads as a knock on wood. Sounding the same as
/// stone made every closed door read as a dead end while exploring.
/// </summary>
internal sealed class WallBumpSoundBank : IDisposable
{
	private static readonly ImpactToneDesign TerrainDesign =
		new(146f, 130f, 0.050f, 0.20f, 0.32f, 65f, 0.45f, 0x4C1D_93E7u);
	private static readonly ImpactToneDesign DoorDesign =
		new(232f, 105f, 0.180f, 0.14f, 0.28f, 42f, 0.30f, 0x7E36_2A55u);

	private readonly SoundEffect _terrainTone;
	private readonly SoundEffect _doorTone;
	private bool _disposed;

	private WallBumpSoundBank()
	{
		_terrainTone = ImpactToneSynthesizer.Render(TerrainDesign);
		_doorTone = ImpactToneSynthesizer.Render(DoorDesign);
	}

	internal static WallBumpSoundBank? Create()
	{
		return Main.dedServ || !SoundEngine.IsAudioSupported ? null : new WallBumpSoundBank();
	}

	internal void Play(float volume, float pitch, MovementBumpSurface surface)
	{
		float scaledVolume = Math.Clamp(volume * Main.soundVolume, 0f, 1f);
		if (_disposed || scaledVolume <= 0f || SoundEngine.AreSoundsPaused)
		{
			return;
		}

		SoundEffect tone = surface == MovementBumpSurface.Door ? _doorTone : _terrainTone;
		tone.Play(scaledVolume, Math.Clamp(pitch, -1f, 1f), pan: 0f);
	}

	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		_terrainTone.Dispose();
		_doorTone.Dispose();
		_disposed = true;
	}
}
