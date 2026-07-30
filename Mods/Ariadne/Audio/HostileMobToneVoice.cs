#nullable enable

using System;

namespace Ariadne.Audio;

/// <summary>
/// The voice a hostile enemy sounds through: one square tone that simply holds.
///
/// It used to be a triangle re-enveloped on every tick, at a rate that rose with
/// proximity. That envelope was the whole problem. Its rise and fall were applied to a
/// carrier whose phase had no relation to them, so each tick began and ended on a
/// discontinuity, and at the closest range a mob restarted that envelope twelve times a
/// second; with several mobs sounding at once the steps arrived faster than they could
/// be heard apart and read as crackle. Nothing here restarts: the phase runs
/// continuously for as long as the same enemy is being followed, and the only things
/// that move are the ones the listener is meant to read.
///
/// Distance is therefore carried by level alone, as it is everywhere else in the mod,
/// with horizontal position in the pan and the far-ear delay and height in the pitch.
/// </summary>
internal sealed class HostileMobToneVoice : ISpatialMonoSource
{
	/// <summary>
	/// The pitch an enemy sounds at when it is merely the nearest one.
	///
	/// Low enough that a square's odd harmonics stack into a body rather than a whistle,
	/// and far enough under the combat-target cues at 620 and 1140 Hz that a held tone
	/// and a lock cue are never mistaken for one another. Height moves it by up to six
	/// semitones either way, which keeps the whole range beneath those cues.
	/// </summary>
	internal const float BaseFrequency = 220f;

	/// <summary>
	/// What the pitch is multiplied by while the enemy is the one the player holds: a
	/// perfect fifth. A fifth is consonant with the tone it replaces, so the change is
	/// heard as the same voice moving rather than as a second sound arriving, and it is
	/// wide enough to be unmistakable at the moment a lock is taken or given back.
	/// </summary>
	internal const float LockedFrequencyRatio = 1.5f;

	private const float MinimumFrequency = 60f;
	private const float MaximumFrequency = 6_000f;

	/// <summary>
	/// How quickly the pitch follows a change. Slow enough to glide audibly when a lock
	/// is taken on the enemy already sounding, which is the one moment the pitch moves on
	/// its own; height is a slow change and rides the same smoothing without ever
	/// sounding like a slide.
	/// </summary>
	private static readonly float FrequencySmoothing = SmoothingCoefficient(0.035f);

	private static readonly float GainAttackSmoothing = SmoothingCoefficient(0.010f);
	private static readonly float GainReleaseSmoothing = SmoothingCoefficient(0.020f);

	private float _targetFrequency = BaseFrequency;
	private float _currentFrequency = BaseFrequency;
	private float _targetGain;
	private float _currentGain;
	private float _phase;

	internal void SetTarget(float frequency, float gain)
	{
		_targetFrequency = Math.Clamp(frequency, MinimumFrequency, MaximumFrequency);
		_targetGain = Math.Clamp(gain, 0f, 1f);
	}

	public float ReadSample(float pitchRatio)
	{
		_currentFrequency += (_targetFrequency - _currentFrequency) * FrequencySmoothing;
		float gainSmoothing = _targetGain > _currentGain
			? GainAttackSmoothing
			: GainReleaseSmoothing;
		_currentGain += (_targetGain - _currentGain) * gainSmoothing;

		float frequency = Math.Clamp(
			_currentFrequency * pitchRatio,
			MinimumFrequency,
			MaximumFrequency);
		float increment = frequency / SpatialAudioTransformCalculator.SampleRate;
		float square = _phase < 0.5f ? 1f : -1f;
		square += EdgeCorrection(_phase, increment);
		square -= EdgeCorrection(WrapPhase(_phase + 0.5f), increment);
		_phase = WrapPhase(_phase + increment);
		return square * _currentGain;
	}

	public void Reset()
	{
		_targetFrequency = BaseFrequency;
		_currentFrequency = BaseFrequency;
		_targetGain = 0f;
		_currentGain = 0f;
		_phase = 0f;
	}

	/// <summary>
	/// What to add near one of the square's two edges so the edge stays band-limited.
	///
	/// A square built by comparing a phase against a half turn steps between its two
	/// levels at whatever sample happens to straddle the edge, which places the step at
	/// the wrong time by up to a sample and scatters the error back down the spectrum as
	/// harmonics that do not belong to the note. Held still that is a faint metallic
	/// edge; under the height pitch shift the false harmonics move against the real ones
	/// and it becomes a warble, which is precisely the kind of artefact this voice exists
	/// to be free of. This is the polynomial band-limited step: two parabola halves that
	/// round the sample either side of the edge by exactly the amount the edge misses it
	/// by, which puts the transition back where it belongs in time.
	/// </summary>
	private static float EdgeCorrection(float phase, float increment)
	{
		if (phase < increment)
		{
			float offset = phase / increment;
			return offset + offset - offset * offset - 1f;
		}

		if (phase > 1f - increment)
		{
			float offset = (phase - 1f) / increment;
			return offset * offset + offset + offset + 1f;
		}

		return 0f;
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
