#nullable enable

using System;
using Terraria;
using Terraria.ModLoader;

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

	private readonly AriadneAudioBus _bus;
	private readonly float[] _terrainTone;
	private readonly float[] _doorTone;
	private bool _disposed;

	private WallBumpSoundBank(AriadneAudioBus bus)
	{
		_bus = bus;
		_terrainTone = ImpactToneSynthesizer.Render(TerrainDesign);
		_doorTone = ImpactToneSynthesizer.Render(DoorDesign);
	}

	internal static WallBumpSoundBank? Create(Mod owner)
	{
		if (Main.dedServ)
		{
			return null;
		}

		AriadneAudioBus? bus = AudioBusSystem.Bus;
		if (bus is null)
		{
			owner.Logger.Warn("Movement bumps are unavailable because the audio bus could not be created.");
			return null;
		}

		return new(bus);
	}

	internal void Play(float volume, float pitch, MovementBumpSurface surface)
	{
		// Terraria's sound slider and the listening gate are the bus's job now.
		float scaledVolume = Math.Clamp(volume, 0f, 1f);
		if (_disposed || scaledVolume <= 0f)
		{
			return;
		}

		float[] tone = surface == MovementBumpSurface.Door ? _doorTone : _terrainTone;
		_bus.Add(new MonoOneShotVoice(
			tone,
			FootstepSoundBank.PitchRatio(pitch),
			scaledVolume));
	}

	public void Dispose()
	{
		_disposed = true;
	}
}
