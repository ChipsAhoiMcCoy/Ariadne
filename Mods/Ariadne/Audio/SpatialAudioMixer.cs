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

/// <summary>
/// The listener's own settings, which belong to the player rather than to any one
/// cue. Passed as a unit so a new cue does not have to be threaded through every
/// emitter individually.
/// </summary>
internal readonly record struct SpatialAudioSettings(
	bool ItdEnabled,
	float MaximumItdMilliseconds,
	bool HeadShadowEnabled);

internal readonly record struct SpatialAudioTransform(
	float LeftGain,
	float RightGain,
	float LeftDelaySamples,
	float RightDelaySamples,
	float PitchRatio,
	float LeftShadow,
	float RightShadow);

internal static class SpatialFieldPosition
{
	/// <summary>
	/// Places a world position in the listener's field: zero is the body, and either
	/// extreme is that edge of the field. The body is the origin rather than the field
	/// rectangle's centre so that a sound standing on the player always reads centred,
	/// which is what lets the field be a fixed size instead of whatever the camera
	/// happens to show.
	/// </summary>
	internal static Vector2 Normalize(
		Vector2 worldPosition,
		Vector2 bodyCenter,
		Vector2 fieldSize)
	{
		if (fieldSize.X <= 0f || fieldSize.Y <= 0f)
		{
			return Vector2.Zero;
		}

		Vector2 offset = worldPosition - bodyCenter;
		return new(
			MathHelper.Clamp(offset.X / (fieldSize.X * 0.5f), -1f, 1f),
			MathHelper.Clamp(offset.Y / (fieldSize.Y * 0.5f), -1f, 1f));
	}
}

internal static class SpatialAudioDistanceGain
{
	/// <summary>
	/// Level is the one cue that carries distance, so it follows proximity directly. An
	/// earlier smoothstep spent its resolution in the middle of the field and left the
	/// outer third collapsing toward silence, so a source approaching from the edge of
	/// the screen read as appearing from nothing rather than as closing.
	/// </summary>
	internal static float FromProximity(float proximity)
	{
		return Math.Clamp(proximity, 0f, 1f);
	}
}

internal static class SpatialAudioTransformCalculator
{
	/// <summary>
	/// Taken from the output device so nothing downstream has to resample. Held as a
	/// field rather than read through <see cref="AudioFormat"/> at every use because
	/// the per-sample voices read it inside their inner loops.
	/// </summary>
	internal static readonly int SampleRate = AudioFormat.SampleRate;

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
		in SpatialAudioSettings settings)
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

		float delaySamples = settings.ItdEnabled
			? directionAmount * Math.Clamp(settings.MaximumItdMilliseconds, 0f, 1f) / 1_000f * SampleRate
			: 0f;
		float leftDelay = x > 0f ? delaySamples : 0f;
		float rightDelay = x < 0f ? delaySamples : 0f;
		float pitchRatio = MathF.Pow(2f, (-6f * y) / 12f);

		// How much of the head each ear is behind. Taken from the signed position so
		// the two shadows cross through zero together and a source passing the midline
		// does not step from one ear's filter to the other's.
		float leftShadow = settings.HeadShadowEnabled ? MathF.Max(0f, x) : 0f;
		float rightShadow = settings.HeadShadowEnabled ? MathF.Max(0f, -x) : 0f;
		return new(
			leftGain,
			rightGain,
			leftDelay,
			rightDelay,
			pitchRatio,
			leftShadow,
			rightShadow);
	}
}

/// <summary>
/// Owns smoothing and interaural-delay history for one spatial mono emitter.
/// Coordinate conversion remains independent of the source implementation.
/// </summary>
internal sealed class SpatialAudioEmitter
{
	/// <summary>
	/// The longest interaural delay the configuration allows, which is what the delay
	/// line has to hold. It is a duration rather than a sample count because the mixer
	/// now runs at the device's rate: a fixed sixty-four samples covered a millisecond
	/// at 44.1 kHz and would silently truncate the delay on a faster endpoint.
	/// </summary>
	private const float MaximumItdMilliseconds = 1f;

	/// <summary>
	/// Three taps of headroom past the longest delay, so the four-point interpolation
	/// below always has a neighbour on each side.
	/// </summary>
	private static readonly int DelayBufferLength = NextPowerOfTwo(
		(int)MathF.Ceiling(MaximumItdMilliseconds / 1_000f * SpatialAudioTransformCalculator.SampleRate) + 4);

	private static readonly int DelayIndexMask = DelayBufferLength - 1;

	/// <summary>
	/// How often the pan, delay and pitch laws are re-evaluated. Every sample cost two
	/// transcendentals and a square root per emitter, which across a terrain bed, four
	/// mob voices and a beacon came to roughly eight hundred thousand of each a second
	/// on the game thread. The listener's position cannot move meaningfully inside a
	/// third of a millisecond, so the law is solved on that grid and the results are
	/// carried across it by straight lines, which is inaudible and sixteen times less
	/// work.
	/// </summary>
	private const int ControlBlockSamples = 16;

	/// <summary>
	/// Where the far ear's corner sits with the head fully between it and the source,
	/// and where it sits with no head in the way. The open figure is above anything the
	/// mod synthesizes, so an unshadowed ear is left alone.
	/// </summary>
	private const float ShadowedEarCutoffHertz = 2_200f;
	private const float OpenEarCutoffHertz = 20_000f;

	/// <summary>
	/// The time constant a silenced emitter falls away on. Exposed because it is an
	/// exponential rather than a ramp: a caller that silences a cue and then resets
	/// its signal state has to wait out several of these, or the reset lands on audio
	/// that has not finished getting quiet and is heard as a step.
	/// </summary>
	internal const float GainReleaseSeconds = 0.160f;

	private static readonly float PositionSmoothing = SmoothingCoefficient(0.025f);
	private static readonly float GainReleaseSmoothing = SmoothingCoefficient(GainReleaseSeconds);

	private readonly float[] _delayBuffer = new float[DelayBufferLength];
	private readonly float _gainAttackSmoothing;
	private SpatialSourceParameters _target;
	private SpatialAudioTransform _appliedTransform;
	private float _appliedLeftGain;
	private float _appliedRightGain;
	private float _leftShadowState;
	private float _rightShadowState;
	private float _currentX;
	private float _currentY;
	private float _currentDistanceGain;
	private int _writeIndex;
	private bool _hasAppliedTransform;

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
		in SpatialAudioSettings settings,
		Span<float> left,
		Span<float> right)
	{
		int position = 0;
		while (position < left.Length)
		{
			int blockLength = Math.Min(ControlBlockSamples, left.Length - position);
			AdvanceControlBlock(blockLength);
			SpatialAudioTransform transform = SpatialAudioTransformCalculator.Calculate(
				_currentX,
				_currentY,
				settings);

			// Distance rides in the channel gains rather than as a third factor, so one
			// straight line per ear carries both the pan and the level.
			float leftGain = transform.LeftGain * _currentDistanceGain;
			float rightGain = transform.RightGain * _currentDistanceGain;
			if (!_hasAppliedTransform)
			{
				_appliedTransform = transform;
				_appliedLeftGain = leftGain;
				_appliedRightGain = rightGain;
				_hasAppliedTransform = true;
			}

			float inverseLength = 1f / blockLength;
			float leftGainStep = (leftGain - _appliedLeftGain) * inverseLength;
			float rightGainStep = (rightGain - _appliedRightGain) * inverseLength;
			float leftDelayStep =
				(transform.LeftDelaySamples - _appliedTransform.LeftDelaySamples) * inverseLength;
			float rightDelayStep =
				(transform.RightDelaySamples - _appliedTransform.RightDelaySamples) * inverseLength;
			float pitchStep =
				(transform.PitchRatio - _appliedTransform.PitchRatio) * inverseLength;

			// The shadow filters hold their coefficient for the block. A one-pole moved
			// on this grid cannot step far enough to be heard as a change in its own
			// right, and interpolating it would cost more than the filter does.
			float leftShadowCoefficient = ShadowCoefficient(transform.LeftShadow);
			float rightShadowCoefficient = ShadowCoefficient(transform.RightShadow);

			for (int offset = 0; offset < blockLength; offset++)
			{
				float monoSample = source.ReadSample(_appliedTransform.PitchRatio + pitchStep * offset);
				_delayBuffer[_writeIndex] = monoSample;
				float leftSample = ReadDelayed(_appliedTransform.LeftDelaySamples + leftDelayStep * offset);
				float rightSample = ReadDelayed(_appliedTransform.RightDelaySamples + rightDelayStep * offset);
				_leftShadowState += (leftSample - _leftShadowState) * leftShadowCoefficient;
				_rightShadowState += (rightSample - _rightShadowState) * rightShadowCoefficient;
				left[position + offset] += _leftShadowState * (_appliedLeftGain + leftGainStep * offset);
				right[position + offset] += _rightShadowState * (_appliedRightGain + rightGainStep * offset);
				_writeIndex = (_writeIndex + 1) & DelayIndexMask;
			}

			_appliedTransform = transform;
			_appliedLeftGain = leftGain;
			_appliedRightGain = rightGain;
			position += blockLength;
		}
	}

	internal void Reset()
	{
		_target = default;
		_appliedTransform = default;
		_appliedLeftGain = 0f;
		_appliedRightGain = 0f;
		_leftShadowState = 0f;
		_rightShadowState = 0f;
		_hasAppliedTransform = false;
		_currentX = 0f;
		_currentY = 0f;
		_currentDistanceGain = 0f;
		_writeIndex = 0;
		Array.Clear(_delayBuffer);
	}

	/// <summary>
	/// Carries the smoothed position and level forward by a whole control block. The
	/// per-sample coefficients are raised to the block length so the time constants
	/// stay exactly what they were when this ran once per sample.
	/// </summary>
	private void AdvanceControlBlock(int blockLength)
	{
		float positionSmoothing = BlockSmoothing(PositionSmoothing, blockLength);
		_currentX += (_target.NormalizedX - _currentX) * positionSmoothing;
		_currentY += (_target.NormalizedY - _currentY) * positionSmoothing;
		float gainSmoothing = _target.DistanceGain > _currentDistanceGain
			? _gainAttackSmoothing
			: GainReleaseSmoothing;
		_currentDistanceGain +=
			(_target.DistanceGain - _currentDistanceGain) * BlockSmoothing(gainSmoothing, blockLength);
	}

	private static float BlockSmoothing(float perSampleSmoothing, int blockLength)
	{
		return blockLength == ControlBlockSamples
			? 1f - MathF.Pow(1f - perSampleSmoothing, ControlBlockSamples)
			: 1f - MathF.Pow(1f - perSampleSmoothing, blockLength);
	}

	/// <summary>
	/// The far ear's one-pole coefficient. A head does not attenuate every frequency
	/// alike: it casts an acoustic shadow that takes the treble and leaves the bass,
	/// which is the cue the flat attenuation in the pan law cannot express. At no
	/// shadow the corner sits above anything the mod synthesizes, so a centred source
	/// passes through untouched.
	/// </summary>
	private static float ShadowCoefficient(float shadowAmount)
	{
		if (shadowAmount <= 0f)
		{
			return 1f;
		}

		float cutoff = OpenEarCutoffHertz *
			MathF.Pow(ShadowedEarCutoffHertz / OpenEarCutoffHertz, Math.Clamp(shadowAmount, 0f, 1f));
		float nyquist = SpatialAudioTransformCalculator.SampleRate * 0.5f;
		if (cutoff >= nyquist)
		{
			return 1f;
		}

		return 1f - MathF.Exp(-MathF.Tau * cutoff / SpatialAudioTransformCalculator.SampleRate);
	}

	/// <summary>
	/// Reads the delay line with four-point Hermite interpolation. Two-tap linear
	/// interpolation attenuates by an amount that depends on where the fraction falls,
	/// so a source sweeping across the field had its timbre swept with it: the delay
	/// doubled as a low-pass whose corner moved with the pan.
	/// </summary>
	private float ReadDelayed(float delaySamples)
	{
		float delay = float.IsFinite(delaySamples)
			? Math.Clamp(delaySamples, 0f, DelayBufferLength - 3f)
			: 0f;
		int wholeSampleDelay = (int)MathF.Floor(delay);
		float fraction = delay - wholeSampleDelay;
		return AudioInterpolation.Hermite(
			TapAt(wholeSampleDelay - 1),
			TapAt(wholeSampleDelay),
			TapAt(wholeSampleDelay + 1),
			TapAt(wholeSampleDelay + 2),
			fraction);
	}

	/// <summary>
	/// One tap, clamped rather than wrapped past the write head, because a delay of
	/// zero has no newer neighbour to reach for.
	/// </summary>
	private float TapAt(int sampleDelay)
	{
		int clamped = Math.Clamp(sampleDelay, 0, DelayBufferLength - 1);
		return _delayBuffer[(_writeIndex - clamped) & DelayIndexMask];
	}

	private static int NextPowerOfTwo(int value)
	{
		int result = 4;
		while (result < value)
		{
			result <<= 1;
		}
		return result;
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
