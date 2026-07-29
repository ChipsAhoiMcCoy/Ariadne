#nullable enable

using System;

namespace Ariadne.Audio;

/// <summary>
/// The shape of one combat-target cue: how long it sounds, and the two pitches it
/// travels between. Direction is the whole distinction between the cues, so it is the
/// only thing a design changes.
/// </summary>
internal readonly record struct CombatTargetCueDesign(
	float DurationSeconds,
	float StartFrequency,
	float EndFrequency);

/// <summary>
/// A short tone that sweeps between two pitches. Its timbre and envelope are fixed, so
/// every combat-target cue is recognisably the same sound and only its contour says
/// which event it marks.
/// </summary>
internal sealed class CombatTargetCueVoice : ISpatialMonoSource
{
	private const float AttackSeconds = 0.004f;

	private readonly int _frameCount;
	private readonly float _startFrequency;
	private readonly float _endFrequency;
	private readonly float _gain;
	private float _phase;
	private int _frame;

	internal CombatTargetCueVoice(in CombatTargetCueDesign design, int frameCount, float gain)
	{
		_frameCount = Math.Max(1, frameCount);
		_startFrequency = design.StartFrequency;
		_endFrequency = design.EndFrequency;
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
			(_startFrequency + (_endFrequency - _startFrequency) * progress) * pitchRatio,
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

/// <summary>
/// The two cues combat targeting sounds, and the gain each needs to reach the shared
/// reference.
///
/// They are one pair rather than two sounds: taking a target rises between two
/// pitches, losing one falls back between the same two. Sharing the timbre and the
/// envelope keeps both recognisable as the targeting cue, while a rise against a fall
/// is about the most reliable distinction hearing offers, so neither has to be learned
/// against the other. The fall is given slightly longer because a descent needs room
/// to read as settling rather than as being cut off.
/// </summary>
internal sealed class CombatTargetCue
{
	private const float LowPitch = 620f;
	private const float HighPitch = 1_140f;

	/// <summary>
	/// How long a voice stays on the bus past the end of the cue itself, so a hard pan
	/// has time to let its far ear out. Derived from the longest delay the mixer can be
	/// configured for at this rate rather than fixed, because a fixed count covered a
	/// millisecond at 44.1 kHz and would fall short on a faster endpoint.
	///
	/// Declared above the cues themselves because static fields initialize in the order
	/// they are written, and a cue built before this one is set is a cue with no tail.
	/// </summary>
	private static readonly int DelayTailFrames =
		(int)MathF.Ceiling(SpatialAudioTransformCalculator.SampleRate / 1_000f) + 8;

	internal static readonly CombatTargetCue Acquired = new(new(0.16f, LowPitch, HighPitch));
	internal static readonly CombatTargetCue Lost = new(new(0.20f, HighPitch, LowPitch));

	private readonly CombatTargetCueDesign _design;

	private CombatTargetCue(in CombatTargetCueDesign design)
	{
		_design = design;
		FrameCount = Math.Max(
			1,
			(int)MathF.Round(SpatialAudioTransformCalculator.SampleRate * design.DurationSeconds));
		BusFrameCount = FrameCount + DelayTailFrames;
		Trim = Calibrate(design, FrameCount);
	}

	/// <summary>How many frames the cue itself sounds for.</summary>
	internal int FrameCount { get; }

	/// <summary>How many frames its voice should be given on the bus, tail included.</summary>
	internal int BusFrameCount { get; }

	/// <summary>The gain that puts this cue on the shared reference.</summary>
	internal float Trim { get; }

	internal CombatTargetCueVoice CreateVoice() => new(_design, FrameCount, Trim);

	private static float Calibrate(in CombatTargetCueDesign design, int frameCount)
	{
		float[] samples = new float[frameCount];
		CombatTargetCueVoice voice = new(design, frameCount, gain: 1f);
		for (int frame = 0; frame < frameCount; frame++)
		{
			samples[frame] = voice.ReadSample(pitchRatio: 1f);
		}

		return AuthoredAudioLevels.PeakLimitedTrim(
			samples,
			AuthoredAudioLevels.SpatialVoiceReferenceLoudness,
			AuthoredAudioLevels.NormalizedSpatialVoicePeak);
	}
}
