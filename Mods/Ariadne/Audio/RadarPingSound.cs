#nullable enable

using System;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;
using Ariadne.Configs;
using Ariadne.Ingame;

namespace Ariadne.Audio;

/// <summary>
/// Plays radar contacts through the same screen-relative ILD, ITD, and vertical-pitch
/// transform used by Ariadne's continuous spatial audio, so a ping locates its contact
/// in the language the wall tones and the hostile bed already speak.
///
/// Unlike the combat-target cues, a ping is fired and forgotten. Several contacts can be
/// in the air at once during a sweep, and each has to keep sounding from where its own
/// contact is; stopping the previous one to make room would turn a sweep into a single
/// ping with a stutter in front of it.
/// </summary>
internal sealed class RadarPingSound : IDisposable
{
	private readonly AriadneAudioBus _bus;
	private bool _disposed;

	private RadarPingSound(AriadneAudioBus bus)
	{
		_bus = bus;
	}

	internal static RadarPingSound? Create(Mod owner)
	{
		if (Main.dedServ)
		{
			return null;
		}

		AriadneAudioBus? bus = AudioBusSystem.Bus;
		if (bus is null)
		{
			owner.Logger.Warn("Radar pings are unavailable because the audio bus could not be created.");
			return null;
		}

		return new(bus);
	}

	/// <summary>
	/// Sounds one contact. Proximity is passed in rather than derived here because the
	/// radar's own range decides it, and level is the only cue carrying distance.
	/// </summary>
	internal void Play(
		Vector2 worldPosition,
		RadarPing ping,
		float proximity,
		AriadneClientConfig config)
	{
		// Terraria's sound slider is applied once, by the bus, for the whole mix.
		float volume = Math.Clamp(config.RadarVolumePercent / 100f, 0f, 1f) *
			SpatialAudioDistanceGain.FromProximity(proximity, config.SpatialAudioDistanceAttenuationEnabled);
		if (_disposed || volume <= 0f || !GameplayAudioGate.CanListen())
		{
			return;
		}

		_bus.Add(new SpatialOneShotVoice(
			ping.CreateVoice(),
			ping.BusFrameCount,
			SpatialObserverContext.Current.NormalizeToField(worldPosition),
			config.ToSpatialAudioSettings(),
			volume));
	}

	/// <summary>
	/// Stops issuing pings. Voices already on the bus are left to retire themselves,
	/// because pulling one out mid-waveform is a click and the longest of them is under
	/// half a second from finishing anyway.
	/// </summary>
	public void Dispose()
	{
		_disposed = true;
	}
}
