#nullable enable

using System;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;
using Ariadne.Configs;
using Ariadne.Ingame;

namespace Ariadne.Audio;

/// <summary>
/// The four short, centered movement-navigation cues. They share the impact renderer
/// used by footsteps and bumps, but their longer pitch sweeps make vertical direction
/// and ledge safety read as a separate family.
/// </summary>
internal sealed class NavigationCueSoundBank : IDisposable
{
	private readonly AriadneAudioBus _bus;
	private readonly float[][] _tones;
	private bool _disposed;

	private NavigationCueSoundBank(AriadneAudioBus bus)
	{
		_bus = bus;
		_tones = new float[NavigationCueDesigns.Count][];
		for (int index = 0; index < _tones.Length; index++)
		{
			_tones[index] = NavigationCueDesigns.Render((NavigationCueKind)index);
		}
	}

	internal static NavigationCueSoundBank? Create(Mod owner)
	{
		if (Main.dedServ)
		{
			return null;
		}

		AriadneAudioBus? bus = AudioBusSystem.Bus;
		if (bus is null)
		{
			owner.Logger.Warn("Movement navigation cues are unavailable because the audio bus could not be created.");
			return null;
		}

		return new(bus);
	}

	internal void Play(NavigationCueKind kind, float volume)
	{
		float gain = Math.Clamp(volume, 0f, 1f);
		if (_disposed || gain <= 0f)
		{
			return;
		}

		_bus.Add(new MonoOneShotVoice(_tones[(int)kind], playbackRatio: 1f, volume: gain));
	}

	internal void PlaySpatial(
		NavigationCueKind kind,
		Vector2 worldPosition,
		float volume,
		AriadneClientConfig config)
	{
		float gain = Math.Clamp(volume, 0f, 1f);
		if (_disposed || gain <= 0f)
		{
			return;
		}

		float[] samples = _tones[(int)kind];
		_bus.Add(new SpatialOneShotVoice(
			new PcmPlaybackVoice(samples, samples.Length, nativePitchRatio: 1f),
			samples.Length + 64,
			SpatialObserverContext.Current.NormalizeToField(worldPosition),
			config.ToSpatialAudioSettings(),
			gain));
	}

	public void Dispose()
	{
		_disposed = true;
	}
}
