#nullable enable

using System;

namespace Ariadne.Audio;

/// <summary>
/// The pulse that marks where the body is while the camera is away from it. Where
/// that pulse sits in the field is <see cref="FreecamBodyBeaconAudioStream"/>'s
/// decision; this only knows the cadence and the envelope.
/// </summary>
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
