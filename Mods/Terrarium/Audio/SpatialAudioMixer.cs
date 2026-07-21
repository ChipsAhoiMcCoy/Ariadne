#nullable enable

using System;
using Terrarium.Configs;

namespace Terrarium.Audio;

/// <summary>
/// A mono source that can react to the pitch ratio supplied by the spatial transform.
/// Procedural and decoded PCM sources can share this contract.
/// </summary>
internal interface ISpatialMonoSource
{
	float ReadSample(float pitchRatio);

	void Reset();
}

internal readonly record struct SpatialSourceParameters(
	float NormalizedX,
	float NormalizedY,
	float DistanceGain);

internal readonly record struct SpatialAudioTransform(
	float LeftGain,
	float RightGain,
	float LeftDelaySamples,
	float RightDelaySamples,
	float PitchRatio);

internal static class SpatialAudioTransformCalculator
{
	internal const int SampleRate = 44_100;
	private const float MaximumFarEarAttenuationDecibels = 6f;

	internal static SpatialAudioTransform Calculate(
		float normalizedX,
		float normalizedY,
		WallToneSpatializationMode spatialization,
		float maximumItdMilliseconds)
	{
		float x = Math.Clamp(normalizedX, -1f, 1f);
		float y = Math.Clamp(normalizedY, -1f, 1f);
		float directionAmount = MathF.Abs(x);
		float farEarGain = MathF.Pow(10f, -MaximumFarEarAttenuationDecibels * directionAmount / 20f);
		float leftGain = x > 0f ? farEarGain : 1f;
		float rightGain = x < 0f ? farEarGain : 1f;
		float powerNormalizer = 1f / MathF.Sqrt(leftGain * leftGain + rightGain * rightGain);
		leftGain *= powerNormalizer;
		rightGain *= powerNormalizer;

		float delaySamples = spatialization == WallToneSpatializationMode.Binaural
			? directionAmount * Math.Clamp(maximumItdMilliseconds, 0f, 1f) / 1_000f * SampleRate
			: 0f;
		float leftDelay = x > 0f ? delaySamples : 0f;
		float rightDelay = x < 0f ? delaySamples : 0f;
		float pitchRatio = MathF.Pow(2f, (-6f * y) / 12f);
		return new(leftGain, rightGain, leftDelay, rightDelay, pitchRatio);
	}
}

/// <summary>
/// Owns smoothing and interaural-delay history for one spatial mono emitter.
/// Coordinate conversion remains independent of the source implementation.
/// </summary>
internal sealed class SpatialAudioEmitter
{
	private const int DelayBufferLength = 64;
	private static readonly float PositionSmoothing = SmoothingCoefficient(0.025f);
	private static readonly float GainAttackSmoothing = SmoothingCoefficient(0.100f);
	private static readonly float GainReleaseSmoothing = SmoothingCoefficient(0.160f);

	private readonly float[] _delayBuffer = new float[DelayBufferLength];
	private SpatialSourceParameters _target;
	private float _currentX;
	private float _currentY;
	private float _currentDistanceGain;
	private int _writeIndex;

	internal void SetTarget(in SpatialSourceParameters target)
	{
		float distanceGain = Math.Clamp(target.DistanceGain, 0f, 1f);
		_target = new(
			distanceGain > 0f ? Math.Clamp(target.NormalizedX, -1f, 1f) : _target.NormalizedX,
			distanceGain > 0f ? Math.Clamp(target.NormalizedY, -1f, 1f) : _target.NormalizedY,
			distanceGain);
	}

	internal void Render(
		ISpatialMonoSource source,
		WallToneSpatializationMode spatialization,
		float maximumItdMilliseconds,
		Span<float> left,
		Span<float> right)
	{
		for (int index = 0; index < left.Length; index++)
		{
			_currentX += (_target.NormalizedX - _currentX) * PositionSmoothing;
			_currentY += (_target.NormalizedY - _currentY) * PositionSmoothing;
			float gainSmoothing = _target.DistanceGain > _currentDistanceGain
				? GainAttackSmoothing
				: GainReleaseSmoothing;
			_currentDistanceGain += (_target.DistanceGain - _currentDistanceGain) * gainSmoothing;
			SpatialAudioTransform transform = SpatialAudioTransformCalculator.Calculate(
				_currentX,
				_currentY,
				spatialization,
				maximumItdMilliseconds);
			float monoSample = source.ReadSample(transform.PitchRatio);
			_delayBuffer[_writeIndex] = monoSample;
			float leftSample = ReadDelayed(transform.LeftDelaySamples);
			float rightSample = ReadDelayed(transform.RightDelaySamples);
			left[index] += leftSample * transform.LeftGain * _currentDistanceGain;
			right[index] += rightSample * transform.RightGain * _currentDistanceGain;
			_writeIndex = (_writeIndex + 1) % DelayBufferLength;
		}
	}

	internal void Reset()
	{
		_target = default;
		_currentX = 0f;
		_currentY = 0f;
		_currentDistanceGain = 0f;
		_writeIndex = 0;
		Array.Clear(_delayBuffer);
	}

	private float ReadDelayed(float delaySamples)
	{
		float delay = float.IsFinite(delaySamples)
			? Math.Clamp(delaySamples, 0f, DelayBufferLength - 2f)
			: 0f;
		int wholeSampleDelay = (int)MathF.Floor(delay);
		float fraction = delay - wholeSampleDelay;
		int newerIndex = _writeIndex - wholeSampleDelay;
		if (newerIndex < 0)
		{
			newerIndex += DelayBufferLength;
		}
		int olderIndex = newerIndex == 0 ? DelayBufferLength - 1 : newerIndex - 1;
		return _delayBuffer[newerIndex] * (1f - fraction) + _delayBuffer[olderIndex] * fraction;
	}

	private static float SmoothingCoefficient(float timeConstantSeconds)
	{
		return 1f - MathF.Exp(-1f / (SpatialAudioTransformCalculator.SampleRate * timeConstantSeconds));
	}
}
