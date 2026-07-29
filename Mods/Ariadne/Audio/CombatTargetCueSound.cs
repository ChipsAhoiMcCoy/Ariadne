#nullable enable

using System;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;
using Ariadne.Configs;
using Ariadne.Ingame;

namespace Ariadne.Audio;

/// <summary>
/// Plays a short authored lock-on cue through the same screen-relative ILD, ITD,
/// and vertical-pitch transform used by Ariadne's continuous spatial audio.
/// </summary>
internal sealed class CombatTargetCueSound : IDisposable
{
	private const float DurationSeconds = 0.16f;
	private const int DelayTailFrames = 64;

	/// <summary>
	/// The gain that puts the cue on the shared reference, measured once from the
	/// voice itself rather than trimmed by ear.
	/// </summary>
	private static readonly float CueTrim = CalibrateCueTrim();

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

	internal void Play(Vector2 worldPosition, AriadneClientConfig config)
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
		int cueFrames = CueFrameCount();
		_voice = new SpatialOneShotVoice(
			new TargetLockCueVoice(cueFrames, CueTrim),
			cueFrames + DelayTailFrames,
			SpatialObserverContext.Current.NormalizeToField(worldPosition),
			config.ToSpatialAudioSettings(),
			volume);
		_bus.Add(_voice);
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

	private static float CalibrateCueTrim()
	{
		int cueFrames = CueFrameCount();
		float[] samples = new float[cueFrames];
		TargetLockCueVoice voice = new(cueFrames, gain: 1f);
		for (int frame = 0; frame < cueFrames; frame++)
		{
			samples[frame] = voice.ReadSample(pitchRatio: 1f);
		}

		return AuthoredAudioLevels.PeakLimitedTrim(
			samples,
			AuthoredAudioLevels.SpatialVoiceReferenceLoudness,
			AuthoredAudioLevels.NormalizedSpatialVoicePeak);
	}

	private static int CueFrameCount()
	{
		return Math.Max(
			1,
			(int)MathF.Round(SpatialAudioTransformCalculator.SampleRate * DurationSeconds));
	}

	private void StopCurrent()
	{
		if (_voice is null)
		{
			return;
		}

		_voice.Stop();
		_bus.Remove(_voice);
		_voice = null;
	}

	private sealed class TargetLockCueVoice : ISpatialMonoSource
	{
		private const float AttackSeconds = 0.004f;
		private readonly int _frameCount;
		private readonly float _gain;
		private float _phase;
		private int _frame;

		internal TargetLockCueVoice(int frameCount, float gain)
		{
			_frameCount = Math.Max(1, frameCount);
			_gain = gain;
		}

		public float ReadSample(float pitchRatio)
		{
			if (_frame >= _frameCount)
			{
				return 0f;
			}

			float progress = _frame / (float)Math.Max(1, _frameCount - 1);
			float time = _frame / (float)SpatialAudioTransformCalculator.SampleRate;
			float attack = Math.Min(1f, time / AttackSeconds);
			float release = MathF.Pow(Math.Max(0f, 1f - progress), 1.8f);
			float frequency = Math.Clamp(
				(620f + 520f * progress) * pitchRatio,
				120f,
				6_000f);
			_phase += frequency / SpatialAudioTransformCalculator.SampleRate;
			_phase -= MathF.Floor(_phase);
			_frame++;

			float angle = MathF.Tau * _phase;
			float tone = MathF.Sin(angle) + 0.22f * MathF.Sin(angle * 2f);
			return tone / 1.22f * attack * release * _gain;
		}

		public void Reset()
		{
			_phase = 0f;
			_frame = 0;
		}
	}
}
