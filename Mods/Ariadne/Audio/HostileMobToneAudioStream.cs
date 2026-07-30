#nullable enable

using System;
using Terraria;
using Terraria.ModLoader;
using Ariadne.Configs;

namespace Ariadne.Audio;

/// <summary>
/// Puts the hostile-enemy tone on the shared bus and translates the player's config into
/// what <see cref="HostileMobToneBed"/> needs. The voice, its level and the rule for
/// moving it from one enemy to the next all live in the bed, which owns no part of the
/// game and can therefore be measured off the engine.
/// </summary>
internal sealed class HostileMobToneAudioStream : IAudioBusSource, IDisposable
{
	private readonly AriadneAudioBus _bus;
	private readonly HostileMobToneBed _bed = new();
	private bool _isReset = true;
	private bool _disposed;

	private HostileMobToneAudioStream(AriadneAudioBus bus)
	{
		_bus = bus;
		bus.Add(this);
	}

	internal static HostileMobToneAudioStream? TryCreate(Mod owner)
	{
		if (Main.dedServ)
		{
			return null;
		}

		AriadneAudioBus? bus = AudioBusSystem.Bus;
		if (bus is null)
		{
			owner.Logger.Warn("Hostile-mob tones are unavailable because the audio bus could not be created.");
			return null;
		}

		return new(bus);
	}

	internal void UpdateTarget(in HostileMobToneTarget target, AriadneClientConfig config)
	{
		if (_disposed)
		{
			return;
		}

		// Terraria's sound slider is applied once, by the bus, for the whole mix.
		float masterGain = Math.Clamp(config.HostileMobToneVolumePercent / 100f, 0f, 1f);
		_bed.SetTarget(target, masterGain, config.ToSpatialAudioSettings());
		_isReset = false;
	}

	/// <summary>
	/// Frees the voice for a different enemy. It fades out first and is reset once it is
	/// silent, so changing enemy is not a step in the waveform.
	/// </summary>
	internal void Handoff()
	{
		if (_disposed)
		{
			return;
		}

		_bed.Handoff();
	}

	public bool Render(Span<float> left, Span<float> right)
	{
		if (_disposed)
		{
			return false;
		}

		_bed.Render(left, right);
		return true;
	}

	internal void StopAndReset()
	{
		if (_disposed || _isReset)
		{
			return;
		}

		_bed.Handoff();
		_isReset = true;
	}

	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		_bus.Remove(this);
		_disposed = true;
		// Nothing renders this again, so there is no fade left to hear.
		_bed.ResetNow();
		_isReset = true;
	}
}
