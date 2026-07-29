#nullable enable

using System;

namespace Ariadne.Audio;

/// <summary>
/// The voice a hostile mob sounds through: a triangle carrier ticking at a rate that
/// rises with proximity. Its level and cadence are set by
/// <see cref="HostileMobToneAudioStream"/>; the voice itself only knows how to sound.
/// </summary>
internal sealed class ModulatedTriangleToneVoice : ISpatialMonoSource
{
	private const float MinimumModulationGain = 0.04f;
	private const float TickAttackSeconds = 0.002f;
	private const float TickDecaySeconds = 0.030f;
	private static readonly float FrequencySmoothing = SmoothingCoefficient(0.035f);
	private static readonly float ModulationSmoothing = SmoothingCoefficient(0.060f);
	private static readonly float GainAttackSmoothing = SmoothingCoefficient(0.003f);
	private static readonly float GainReleaseSmoothing = SmoothingCoefficient(0.020f);

	private readonly float _initialCarrierPhase;
	private readonly float _initialModulationPhase;
	private float _targetFrequency = 320f;
	private float _targetModulationRate = 1.5f;
	private float _targetGain;
	private float _currentFrequency = 320f;
	private float _currentModulationRate = 1.5f;
	private float _currentGain;
	private float _carrierPhase;
	private float _modulationPhase;

	internal ModulatedTriangleToneVoice(float carrierPhase, float modulationPhase)
	{
		_initialCarrierPhase = WrapPhase(carrierPhase);
		_initialModulationPhase = WrapPhase(modulationPhase);
		_carrierPhase = _initialCarrierPhase;
		_modulationPhase = _initialModulationPhase;
	}

	internal void SetTarget(float frequency, float modulationRate, float gain)
	{
		_targetFrequency = Math.Clamp(frequency, 120f, 6_000f);
		_targetModulationRate = Math.Clamp(modulationRate, 0.25f, 30f);
		_targetGain = Math.Clamp(gain, 0f, 1f);
	}

	public float ReadSample(float pitchRatio)
	{
		_currentFrequency += (_targetFrequency - _currentFrequency) * FrequencySmoothing;
		_currentModulationRate += (_targetModulationRate - _currentModulationRate) * ModulationSmoothing;
		float gainSmoothing = _targetGain > _currentGain
			? GainAttackSmoothing
			: GainReleaseSmoothing;
		_currentGain += (_targetGain - _currentGain) * gainSmoothing;

		float carrierFrequency = Math.Clamp(_currentFrequency * pitchRatio, 120f, 6_000f);
		float carrierIncrement = carrierFrequency / SpatialAudioTransformCalculator.SampleRate;
		float triangle = 4f * MathF.Abs(_carrierPhase - 0.5f) - 1f;

		float secondsIntoTick = _modulationPhase / _currentModulationRate;
		float tickAmount = secondsIntoTick < TickAttackSeconds
			? secondsIntoTick / TickAttackSeconds
			: MathF.Exp(-(secondsIntoTick - TickAttackSeconds) / TickDecaySeconds);
		float modulationGain = MinimumModulationGain + tickAmount * (1f - MinimumModulationGain);
		_carrierPhase = WrapPhase(_carrierPhase + carrierIncrement);
		_modulationPhase = WrapPhase(
			_modulationPhase + _currentModulationRate / SpatialAudioTransformCalculator.SampleRate);
		return triangle * modulationGain * _currentGain;
	}

	public void Reset()
	{
		_targetFrequency = 320f;
		_targetModulationRate = 1.5f;
		_targetGain = 0f;
		_currentFrequency = 320f;
		_currentModulationRate = 1.5f;
		_currentGain = 0f;
		_carrierPhase = _initialCarrierPhase;
		_modulationPhase = _initialModulationPhase;
	}

	private static float WrapPhase(float phase)
	{
		return phase - MathF.Floor(phase);
	}

	private static float SmoothingCoefficient(float timeConstantSeconds)
	{
		return 1f - MathF.Exp(-1f / (SpatialAudioTransformCalculator.SampleRate * timeConstantSeconds));
	}
}
