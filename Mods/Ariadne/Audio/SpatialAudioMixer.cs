#nullable enable

using System;
using Microsoft.Xna.Framework;
using Terraria;

namespace Ariadne.Audio;

/// <summary>
/// A mono source that can react to the pitch ratio supplied by the spatial transform.
/// Procedural and decoded PCM sources can share this contract.
/// </summary>
internal interface ISpatialMonoSource
{
	float ReadSample(float pitchRatio);

	void Reset();
}

/// <summary>
/// A mono source that supplies its own per-sample screen position.
/// This bypasses emitter smoothing so authored motion keeps its intended path
/// while still using the shared ILD and ITD transform.
/// </summary>
internal interface IMovingSpatialMonoSource : ISpatialMonoSource
{
	SpatialSourceParameters CurrentSpatialParameters { get; }
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

internal static class ViewportSpatialPosition
{
	/// <summary>
	/// Places a world position relative to the listener's own body, scaled so half a
	/// viewport away reaches either edge. Measuring from the viewport rectangle
	/// instead assumed the body sits at screen center, which stops being true once
	/// Terraria clamps the camera near a world boundary: a sound directly on the
	/// player would then pan to one side for as long as the player stayed there.
	/// </summary>
	internal static Vector2 Normalize(Vector2 worldPosition)
	{
		return Normalize(worldPosition, Main.LocalPlayer.Center, Main.Camera.ScaledSize);
	}

	internal static Vector2 Normalize(Vector2 worldPosition, Vector2 bodyCenter, Vector2 viewportSize)
	{
		if (viewportSize.X <= 0f || viewportSize.Y <= 0f)
		{
			return Vector2.Zero;
		}

		Vector2 halfViewport = viewportSize * 0.5f;
		return new(
			MathHelper.Clamp((worldPosition.X - bodyCenter.X) / halfViewport.X, -1f, 1f),
			MathHelper.Clamp((worldPosition.Y - bodyCenter.Y) / halfViewport.Y, -1f, 1f));
	}
}

internal static class SpatialAudioDistanceGain
{
	internal static float FromProximity(float proximity)
	{
		float clampedProximity = Math.Clamp(proximity, 0f, 1f);
		return clampedProximity * clampedProximity * (3f - 2f * clampedProximity);
	}
}

internal static class SpatialAudioTransformCalculator
{
	internal const int SampleRate = 44_100;
	/// <summary>
	/// The conservative default width. Cues that sit inside the world image, such as
	/// the cursor and mob tones, stay here so they read as part of the scene. Terrain
	/// voices pass a deeper value because their whole job is to say which side.
	/// </summary>
	internal const float DefaultFarEarAttenuationDecibels = 6f;

	/// <summary>
	/// What each channel receives from a centered voice under the equal-power pan
	/// below. Level calibration has to account for it, because a mono cue played
	/// without the spatializer reaches both channels whole.
	/// </summary>
	internal static readonly float CenteredChannelGain = 1f / MathF.Sqrt(2f);

	internal static SpatialAudioTransform Calculate(
		float normalizedX,
		float normalizedY,
		bool itdEnabled,
		float maximumItdMilliseconds,
		float farEarAttenuationDecibels = DefaultFarEarAttenuationDecibels)
	{
		float x = Math.Clamp(normalizedX, -1f, 1f);
		float y = Math.Clamp(normalizedY, -1f, 1f);
		float directionAmount = MathF.Abs(x);
		float farEarGain = MathF.Pow(10f, -MathF.Max(0f, farEarAttenuationDecibels) * directionAmount / 20f);
		float leftGain = x > 0f ? farEarGain : 1f;
		float rightGain = x < 0f ? farEarGain : 1f;
		float powerNormalizer = 1f / MathF.Sqrt(leftGain * leftGain + rightGain * rightGain);
		leftGain *= powerNormalizer;
		rightGain *= powerNormalizer;

		float delaySamples = itdEnabled
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
	private static readonly float GainReleaseSmoothing = SmoothingCoefficient(0.160f);

	private readonly float[] _delayBuffer = new float[DelayBufferLength];
	private readonly float _gainAttackSmoothing;
	private SpatialSourceParameters _target;
	private float _currentX;
	private float _currentY;
	private float _currentDistanceGain;
	private int _writeIndex;

	internal SpatialAudioEmitter(float gainAttackSeconds = 0.100f)
	{
		_gainAttackSmoothing = SmoothingCoefficient(gainAttackSeconds);
	}

	internal void SetTarget(in SpatialSourceParameters target)
	{
		_target = SanitizeTarget(target, _target.NormalizedX, _target.NormalizedY);
	}

	internal void SetTargetImmediately(in SpatialSourceParameters target)
	{
		_target = SanitizeTarget(target, 0f, 0f);
		_currentX = _target.NormalizedX;
		_currentY = _target.NormalizedY;
		_currentDistanceGain = _target.DistanceGain;
		_writeIndex = 0;
		Array.Clear(_delayBuffer);
	}

	internal void Render(
		ISpatialMonoSource source,
		bool itdEnabled,
		float maximumItdMilliseconds,
		Span<float> left,
		Span<float> right,
		float farEarAttenuationDecibels = SpatialAudioTransformCalculator.DefaultFarEarAttenuationDecibels)
	{
		for (int index = 0; index < left.Length; index++)
		{
			_currentX += (_target.NormalizedX - _currentX) * PositionSmoothing;
			_currentY += (_target.NormalizedY - _currentY) * PositionSmoothing;
			float gainSmoothing = _target.DistanceGain > _currentDistanceGain
				? _gainAttackSmoothing
				: GainReleaseSmoothing;
			_currentDistanceGain += (_target.DistanceGain - _currentDistanceGain) * gainSmoothing;
			SpatialAudioTransform transform = SpatialAudioTransformCalculator.Calculate(
				_currentX,
				_currentY,
				itdEnabled,
				maximumItdMilliseconds,
				farEarAttenuationDecibels);
			float monoSample = source.ReadSample(transform.PitchRatio);
			_delayBuffer[_writeIndex] = monoSample;
			float leftSample = ReadDelayed(transform.LeftDelaySamples);
			float rightSample = ReadDelayed(transform.RightDelaySamples);
			left[index] += leftSample * transform.LeftGain * _currentDistanceGain;
			right[index] += rightSample * transform.RightGain * _currentDistanceGain;
			_writeIndex = (_writeIndex + 1) % DelayBufferLength;
		}
	}

	internal void RenderMoving(
		IMovingSpatialMonoSource source,
		bool itdEnabled,
		float maximumItdMilliseconds,
		Span<float> left,
		Span<float> right)
	{
		for (int index = 0; index < left.Length; index++)
		{
			SpatialSourceParameters parameters = source.CurrentSpatialParameters;
			float x = Math.Clamp(parameters.NormalizedX, -1f, 1f);
			float y = Math.Clamp(parameters.NormalizedY, -1f, 1f);
			float distanceGain = Math.Clamp(parameters.DistanceGain, 0f, 1f);
			SpatialAudioTransform transform = SpatialAudioTransformCalculator.Calculate(
				x,
				y,
				itdEnabled,
				maximumItdMilliseconds);
			float monoSample = source.ReadSample(transform.PitchRatio);
			_delayBuffer[_writeIndex] = monoSample;
			float leftSample = ReadDelayed(transform.LeftDelaySamples);
			float rightSample = ReadDelayed(transform.RightDelaySamples);
			left[index] += leftSample * transform.LeftGain * distanceGain;
			right[index] += rightSample * transform.RightGain * distanceGain;
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

	private static SpatialSourceParameters SanitizeTarget(
		in SpatialSourceParameters target,
		float silentX,
		float silentY)
	{
		float distanceGain = Math.Clamp(target.DistanceGain, 0f, 1f);
		return new(
			distanceGain > 0f ? Math.Clamp(target.NormalizedX, -1f, 1f) : silentX,
			distanceGain > 0f ? Math.Clamp(target.NormalizedY, -1f, 1f) : silentY,
			distanceGain);
	}

	private static float SmoothingCoefficient(float timeConstantSeconds)
	{
		return 1f - MathF.Exp(-1f / (SpatialAudioTransformCalculator.SampleRate * timeConstantSeconds));
	}
}
