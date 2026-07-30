#nullable enable

using System;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;
using Ariadne.Configs;
using Ariadne.Ingame;

namespace Ariadne.Audio;

/// <summary>
/// Plays the authored cues that mark a combat target being taken and lost, through the
/// same screen-relative ILD, ITD, and vertical-pitch transform used by Ariadne's
/// continuous spatial audio.
///
/// A loss sounds from where the target last was rather than from the middle of the
/// field, so the cue says which direction the thing that vanished had been in.
/// </summary>
internal sealed class CombatTargetCueSound : IDisposable
{
	private readonly AriadneAudioBus _bus;
	private SpatialOneShotVoice? _voice;
	private Vector2 _worldPosition;
	private bool _disposed;

	private CombatTargetCueSound(AriadneAudioBus bus)
	{
		_bus = bus;
	}

	internal static CombatTargetCueSound? Create(Mod owner)
	{
		if (Main.dedServ)
		{
			return null;
		}

		AriadneAudioBus? bus = AudioBusSystem.Bus;
		if (bus is null)
		{
			owner.Logger.Warn("Combat-target cues are unavailable because the audio bus could not be created.");
			return null;
		}

		return new(bus);
	}

	/// <summary>Sounds a target being taken.</summary>
	internal void Play(Vector2 worldPosition, AriadneClientConfig config)
	{
		Play(CombatTargetCue.Acquired, worldPosition, config);
	}

	/// <summary>Sounds a target being lost, from where it was last known to be.</summary>
	internal void PlayLoss(Vector2 worldPosition, AriadneClientConfig config)
	{
		Play(CombatTargetCue.Lost, worldPosition, config);
	}

	/// <summary>
	/// Sounds the target already held opening or shutting. A boss part whose window is a
	/// fraction of a second cannot be reported in words, so this is what carries it.
	/// </summary>
	internal void PlayVulnerability(Vector2 worldPosition, bool canBeHit, AriadneClientConfig config)
	{
		Play(canBeHit ? CombatTargetCue.Opened : CombatTargetCue.Shielded, worldPosition, config);
	}

	internal void Update(AriadneClientConfig config)
	{
		if (_disposed || _voice is null)
		{
			return;
		}

		float volume = Math.Clamp(config.HostileMobToneVolumePercent / 100f, 0f, 1f);
		if (!config.HostileMobTonesEnabled ||
			volume <= 0f ||
			!GameplayAudioGate.CanListen())
		{
			StopCurrent();
			return;
		}

		if (_voice.IsFinished)
		{
			_voice = null;
			return;
		}

		// The cue follows the target rather than staying where it was fired, which a
		// baked render could not do.
		_voice.Update(
			SpatialObserverContext.Current.NormalizeToField(_worldPosition),
			config.ToSpatialAudioSettings(),
			volume);
	}

	internal void StopAndReset()
	{
		if (_disposed)
		{
			return;
		}
		StopCurrent();
	}

	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		StopCurrent();
		_disposed = true;
	}

	private void Play(CombatTargetCue cue, Vector2 worldPosition, AriadneClientConfig config)
	{
		// Terraria's sound slider is applied once, by the bus, for the whole mix.
		float volume = Math.Clamp(config.HostileMobToneVolumePercent / 100f, 0f, 1f);
		if (_disposed ||
			!config.HostileMobTonesEnabled ||
			volume <= 0f ||
			!GameplayAudioGate.CanListen())
		{
			StopCurrent();
			return;
		}

		StopCurrent();
		_worldPosition = worldPosition;
		_voice = new SpatialOneShotVoice(
			cue.CreateVoice(),
			cue.BusFrameCount,
			SpatialObserverContext.Current.NormalizeToField(worldPosition),
			config.ToSpatialAudioSettings(),
			volume);
		_bus.Add(_voice);
	}

	/// <summary>
	/// Hands the sounding cue its fade and forgets it. It is left on the bus to retire
	/// itself, because a boss part answers several times a second and a cue pulled out
	/// mid-waveform to make room for the next one is a click each time.
	/// </summary>
	private void StopCurrent()
	{
		if (_voice is null)
		{
			return;
		}

		_voice.Stop();
		_voice = null;
	}
}
