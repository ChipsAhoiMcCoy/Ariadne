#nullable enable

using System;
using Microsoft.Xna.Framework;

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
	/// How little of the viewport a side may claim before its scale stops shrinking.
	/// The body can sit on, or briefly past, a viewport edge while the camera catches
	/// up, which would otherwise divide by zero or invert that side entirely.
	/// </summary>
	private const float MinimumReachFraction = 0.25f;

	/// <summary>
	/// Places a world position on the screen the listener is actually looking at: zero
	/// is the body, and either extreme is that edge of the visible viewport. The body
	/// stays the origin because Terraria clamps the camera near a world boundary, and
	/// measuring from the rectangle's centre would pan a sound standing on the player
	/// to one side for as long as the player stayed there. Each side is scaled by its
	/// own distance to its own edge so that clamping cannot leave one half of the
	/// screen saturated before its edge while the other half never reaches the extreme.
	/// </summary>
	internal static Vector2 Normalize(
		Vector2 worldPosition,
		Vector2 bodyCenter,
		Vector2 viewportPosition,
		Vector2 viewportSize)
	{
		if (viewportSize.X <= 0f || viewportSize.Y <= 0f)
		{
			return Vector2.Zero;
		}

		return new(
			NormalizeAxis(worldPosition.X, bodyCenter.X, viewportPosition.X, viewportSize.X),
			NormalizeAxis(worldPosition.Y, bodyCenter.Y, viewportPosition.Y, viewportSize.Y));
	}

	private static float NormalizeAxis(
		float world,
		float body,
		float viewportStart,
		float viewportExtent)
	{
		float offset = world - body;
		float reach = offset >= 0f
			? viewportStart + viewportExtent - body
			: body - viewportStart;
		return MathHelper.Clamp(
			offset / MathF.Max(reach, viewportExtent * MinimumReachFraction),
			-1f,
			1f);
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
	/// One stereo width for every cue. A per-cue width let two sounds at the same
	/// screen position land in different places, which is the one thing a shared
	/// coordinate system exists to prevent.
	/// </summary>
	private const float FarEarAttenuationDecibels = 24f;

	/// <summary>
	/// What each channel receives from a centered voice under the equal-power pan
	/// below. Level calibration has to account for it, because a mono cue played
	/// without the spatializer reaches both channels whole.
	/// </summary>
	internal static readonly float CenteredChannelGain = 1f / MathF.Sqrt(2f);

	/// <summary>
	/// The field is linear in screen position: a source halfway to the edge images
	/// halfway over. An earlier cube-root curve spent most of the field on the few
	/// tiles nearest the body, so a source crossing the body swung ear to ear within a
	/// few tiles while the outer reaches of the screen barely moved at all. Resolution
	/// near the midline is left to the interaural delay below, which is the cue that
	/// resolves it: a source five tiles off centre is already tens of microseconds
	/// wide, well above what the ear can hear, at a level difference of only 2 dB.
	/// </summary>
	internal static SpatialAudioTransform Calculate(
		float normalizedX,
		float normalizedY,
		bool itdEnabled,
		float maximumItdMilliseconds)
	{
		float x = Math.Clamp(normalizedX, -1f, 1f);
		float y = Math.Clamp(normalizedY, -1f, 1f);
		float directionAmount = MathF.Abs(x);
		float farEarGain = MathF.Pow(10f, -FarEarAttenuationDecibels * directionAmount / 20f);
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
		Span<float> right)
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
