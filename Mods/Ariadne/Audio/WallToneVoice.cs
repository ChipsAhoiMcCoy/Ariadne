#nullable enable

using System;

namespace Ariadne.Audio;

/// <summary>
/// The band a terrain voice sweeps between its farthest and nearest surface, and
/// the filter resonance that gives it its character.
/// </summary>
internal readonly record struct WallToneVoiceDesign(
	float MinimumFrequency,
	float MaximumFrequency,
	float FilterQ);

/// <summary>
/// One surface of the terrain bed: white noise through a state-variable band-pass
/// whose centre rides proximity. Which surface it is, and how loud, is
/// <see cref="WallToneAudioStream"/>'s decision.
/// </summary>
internal sealed class WallToneVoice : ISpatialMonoSource
{
	private static readonly float FrequencySmoothing = SmoothingCoefficient(0.035f);
	private static readonly float GainSmoothing = SmoothingCoefficient(0.020f);

	private readonly uint _initialNoiseState;
	private readonly WallToneVoiceDesign _design;
	private uint _noiseState;
	private float _targetFrequency;
	private float _targetGain;
	private float _currentFrequency;
	private float _currentGain;
	private float _integratorOne;
	private float _integratorTwo;

	internal WallToneVoice(uint seed, WallToneVoiceDesign design)
	{
		_initialNoiseState = seed;
		_noiseState = _initialNoiseState;
		_design = design;
		_targetFrequency = design.MinimumFrequency;
		_currentFrequency = design.MinimumFrequency;
	}

	internal float FrequencyForProximity(float proximity)
	{
		return _design.MinimumFrequency * MathF.Pow(
			_design.MaximumFrequency / _design.MinimumFrequency,
			Math.Clamp(proximity, 0f, 1f));
	}

	internal void SetTarget(float frequency, float gain)
	{
		_targetFrequency = Math.Clamp(frequency, 120f, 6_000f);
		_targetGain = Math.Clamp(gain, 0f, 1f);
	}

	public float ReadSample(float pitchRatio)
	{
		_currentFrequency += (_targetFrequency - _currentFrequency) * FrequencySmoothing;
		_currentGain += (_targetGain - _currentGain) * GainSmoothing;
		float centerFrequency = Math.Clamp(_currentFrequency * pitchRatio, 120f, 6_000f);
		float bandPassedNoise = FilterBandPass(NextWhiteNoise(ref _noiseState), centerFrequency, _design.FilterQ);
		return bandPassedNoise * _currentGain;
	}

	public void Reset()
	{
		_noiseState = _initialNoiseState;
		_targetFrequency = _design.MinimumFrequency;
		_targetGain = 0f;
		_currentFrequency = _design.MinimumFrequency;
		_currentGain = 0f;
		_integratorOne = 0f;
		_integratorTwo = 0f;
	}

	private float FilterBandPass(float input, float centerFrequency, float filterQ)
	{
		float g = MathF.Tan(MathF.PI * centerFrequency / SpatialAudioTransformCalculator.SampleRate);
		float k = 1f / filterQ;
		float a1 = 1f / (1f + g * (g + k));
		float v3 = input - _integratorTwo;
		float v1 = a1 * (_integratorOne + g * v3);
		float v2 = _integratorTwo + g * v1;
		_integratorOne = 2f * v1 - _integratorOne;
		_integratorTwo = 2f * v2 - _integratorTwo;
		return v1;
	}

	private static float NextWhiteNoise(ref uint state)
	{
		state ^= state << 13;
		state ^= state >> 17;
		state ^= state << 5;
		return (state & 0x00FF_FFFFu) / 8_388_607.5f - 1f;
	}

	private static float SmoothingCoefficient(float timeConstantSeconds)
	{
		return 1f - MathF.Exp(-1f / (SpatialAudioTransformCalculator.SampleRate * timeConstantSeconds));
	}
}
