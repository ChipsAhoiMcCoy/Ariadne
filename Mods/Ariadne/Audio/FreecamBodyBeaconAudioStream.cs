#nullable enable

using System;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;
using Ariadne.Configs;

namespace Ariadne.Audio;

internal sealed class FreecamBodyBeaconAudioStream : IAudioBusSource, IDisposable
{
	private static readonly int CalibrationFrames = SpatialAudioTransformCalculator.SampleRate;

	/// <summary>
	/// The gain that puts the beacon on the shared reference. It pulses for a sixth
	/// of its cycle, so its measured loudness sits well under its peak.
	/// </summary>
	private static readonly float VoiceGain = CalibrateVoiceGain();

	private readonly AriadneAudioBus _bus;
	private readonly BodyBeaconVoice _voice = new();
	private readonly SpatialAudioEmitter _emitter = new(gainAttackSeconds: 0.003f);
	private SpatialAudioSettings _settings;
	private bool _isReset = true;
	private bool _disposed;

	private FreecamBodyBeaconAudioStream(AriadneAudioBus bus)
	{
		_bus = bus;
		bus.Add(this);
	}

	internal static FreecamBodyBeaconAudioStream? TryCreate(Mod owner)
	{
		if (Main.dedServ)
		{
			return null;
		}

		AriadneAudioBus? bus = AudioBusSystem.Bus;
		if (bus is null)
		{
			owner.Logger.Warn("The freecam body beacon is unavailable because the audio bus could not be created.");
			return null;
		}

		return new(bus);
	}

	internal void UpdateTarget(Vector2 normalizedPosition, AriadneClientConfig config)
	{
		if (_disposed)
		{
			return;
		}

		_settings = config.ToSpatialAudioSettings();
		// Terraria's sound slider is applied once, by the bus, for the whole mix.
		float masterGain =
			Math.Clamp(config.FreecamBeaconVolumePercent / 100f, 0f, 1f) * VoiceGain;
		_voice.SetGain(masterGain);
		SpatialSourceParameters target = new(
			normalizedPosition.X,
			normalizedPosition.Y,
			DistanceGain: 1f);
		if (_isReset)
		{
			_emitter.SetTargetImmediately(target);
		}
		else
		{
			_emitter.SetTarget(target);
		}
		_isReset = false;
	}

	public bool Render(Span<float> left, Span<float> right)
	{
		if (_disposed)
		{
			return false;
		}

		_emitter.Render(_voice, _settings, left, right);
		return true;
	}

	internal void StopAndReset()
	{
		if (_disposed || _isReset)
		{
			return;
		}
		ResetSignalState();
	}

	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		_bus.Remove(this);
		_disposed = true;
		ResetSignalState();
	}

	private static float CalibrateVoiceGain()
	{
		BodyBeaconVoice voice = new();
		voice.SetGain(1f);
		float[] samples = new float[CalibrationFrames];
		for (int index = 0; index < samples.Length; index++)
		{
			samples[index] = voice.ReadSample(pitchRatio: 1f);
		}

		return AuthoredAudioLevels.LoudnessTrim(
			samples,
			AuthoredAudioLevels.SpatialVoiceReferenceLoudness);
	}

	private void ResetSignalState()
	{
		_voice.Reset();
		_emitter.Reset();
		_isReset = true;
	}
}

internal sealed class BodyBeaconVoice : ISpatialMonoSource
{
	private const float Frequency = 660f;
	private const float PulseDurationSeconds = 0.120f;
	private const float PulseIntervalSeconds = 0.750f;
	private const float EdgeSeconds = 0.005f;
	private static readonly int PulseDurationSamples =
		(int)(PulseDurationSeconds * SpatialAudioTransformCalculator.SampleRate);
	private static readonly int PulseIntervalSamples =
		(int)(PulseIntervalSeconds * SpatialAudioTransformCalculator.SampleRate);
	private static readonly int EdgeSamples =
		(int)(EdgeSeconds * SpatialAudioTransformCalculator.SampleRate);

	private float _gain;
	private float _phase;
	private int _sampleInInterval;

	internal void SetGain(float gain)
	{
		_gain = Math.Clamp(gain, 0f, 1f);
	}

	public float ReadSample(float pitchRatio)
	{
		float sample = 0f;
		if (_sampleInInterval < PulseDurationSamples)
		{
			float envelope = 1f;
			if (_sampleInInterval < EdgeSamples)
			{
				envelope = _sampleInInterval / (float)EdgeSamples;
			}
			else if (_sampleInInterval >= PulseDurationSamples - EdgeSamples)
			{
				envelope =
					(PulseDurationSamples - _sampleInInterval - 1) / (float)EdgeSamples;
			}

			sample = MathF.Sin(_phase * MathF.Tau) * Math.Clamp(envelope, 0f, 1f) * _gain;
			float frequency = Math.Clamp(Frequency * pitchRatio, 120f, 6_000f);
			_phase = WrapPhase(
				_phase + frequency / SpatialAudioTransformCalculator.SampleRate);
		}

		_sampleInInterval++;
		if (_sampleInInterval >= PulseIntervalSamples)
		{
			_sampleInInterval = 0;
			_phase = 0f;
		}
		return sample;
	}

	public void Reset()
	{
		_gain = 0f;
		_phase = 0f;
		_sampleInInterval = 0;
	}

	private static float WrapPhase(float phase)
	{
		return phase - MathF.Floor(phase);
	}
}
