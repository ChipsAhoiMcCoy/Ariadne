#nullable enable

using System;

namespace Ariadne.Audio;

/// <summary>
/// The shape of one radar ping: how many times the bell is struck. Nothing else varies
/// between the pings, because the count is the whole distinction.
/// </summary>
internal readonly record struct RadarPingDesign(int PipCount);

/// <summary>
/// A struck bell, repeated a fixed number of times.
///
/// The radar cannot say what it found with pitch, because pitch already carries height
/// and every ping arrives shifted somewhere within six semitones of the base. Nor can it
/// say so with frequency band: hostile tones sit at 220 Hz, wall tones cover 320 to
/// 2400 Hz, and the combat cues take 620/1140 and 1240/2280, so there is no clear band
/// left to claim. What is left, and what nothing else in the mod uses, is how many times
/// the sound repeats. Counting two taps against three is far more reliable than telling
/// two timbres apart, and it survives the pitch shift the height cue applies.
///
/// The timbre is deliberately inharmonic. Its partials sit at ratios no harmonic series
/// contains, which is what makes a bell sound struck rather than played, and it is the
/// one thing in Ariadne's mix built that way, so a ping is never mistaken for a tone that
/// happens to be sounding at the same moment.
/// </summary>
internal sealed class RadarPingVoice : ISpatialMonoSource
{
	/// <summary>
	/// The strike pitch. It sits above the hostile bed and below the top of the wall-tone
	/// band, where the ±6 semitones of the height cue keep it clear of both ends.
	/// </summary>
	private const float BaseFrequency = 990f;

	private const float AttackSeconds = 0.003f;

	/// <summary>
	/// How fast a strike falls away. Short enough that a pip has all but ended before the
	/// next one lands, which is what keeps the count countable rather than a roll: by the
	/// time the next strike arrives this one is down to a twentieth of its peak.
	/// </summary>
	private const float DecaySeconds = 0.020f;

	/// <summary>
	/// How often the bell is struck. Kept as tight as the decay allows, because several
	/// contacts sound one after another and the count is the only thing here that costs
	/// time. Four strikes fit inside a quarter of a second at this rate.
	/// </summary>
	internal const float PipPeriodSeconds = 0.060f;

	/// <summary>
	/// How long the last strike is given past its own start, so the final pip decays
	/// instead of being cut at the frame budget.
	/// </summary>
	internal const float PipTailSeconds = 0.080f;

	private static readonly float[] PartialRatios = [1f, 2.76f, 5.40f];
	private static readonly float[] PartialGains = [1f, 0.55f, 0.25f];
	private static readonly float PartialNormalization =
		1f / (PartialGains[0] + PartialGains[1] + PartialGains[2]);

	private readonly int _pipCount;
	private readonly int _frameCount;
	private readonly float _gain;
	private readonly float[] _phases = new float[PartialRatios.Length];
	private int _frame;
	private int _pip = -1;

	internal RadarPingVoice(in RadarPingDesign design, int frameCount, float gain)
	{
		_pipCount = Math.Max(1, design.PipCount);
		_frameCount = Math.Max(1, frameCount);
		_gain = gain;
	}

	public float ReadSample(float pitchRatio)
	{
		if (_frame >= _frameCount)
		{
			return 0f;
		}

		float time = _frame / (float)SpatialAudioTransformCalculator.SampleRate;

		// The index stops advancing at the last pip, so time past its start keeps feeding
		// the same decay rather than opening a strike that should not sound.
		int pip = Math.Min((int)(time / PipPeriodSeconds), _pipCount - 1);
		if (pip != _pip)
		{
			// A strike restarts the partials in phase with each other. That alignment is
			// what gives the attack its click, and it is how the ear separates one pip
			// from the tail of the one before it.
			_pip = pip;
			Array.Clear(_phases);
		}

		float pipTime = time - pip * PipPeriodSeconds;
		float attack = Math.Min(1f, pipTime / AttackSeconds);
		float decay = MathF.Exp(-pipTime / DecaySeconds);

		float tone = 0f;
		for (int index = 0; index < PartialRatios.Length; index++)
		{
			float frequency = Math.Clamp(
				BaseFrequency * PartialRatios[index] * pitchRatio,
				120f,
				6_000f);
			_phases[index] += frequency / SpatialAudioTransformCalculator.SampleRate;
			_phases[index] -= MathF.Floor(_phases[index]);
			tone += PartialGains[index] * MathF.Sin(MathF.Tau * _phases[index]);
		}

		_frame++;
		return tone * PartialNormalization * attack * decay * _gain;
	}

	public void Reset()
	{
		Array.Clear(_phases);
		_frame = 0;
		_pip = -1;
	}
}

/// <summary>
/// The pings the radar sounds, one per pip count, and the gain each needs to reach the
/// shared reference.
///
/// One pip is ore and other spelunkable ground, two is a container, three is a creature,
/// and four is whatever else the listener has armed. The order is not arbitrary: the
/// counts rise as the contact gets rarer in ordinary play, so the sounds heard most often
/// are also the shortest.
/// </summary>
internal sealed class RadarPing
{
	/// <summary>
	/// How long a voice stays on the bus past the end of the ping itself, so a hard pan
	/// has time to let its far ear out. Derived from the sample rate rather than fixed,
	/// and declared above the pings because static fields initialize in written order and
	/// a ping built before this is set is a ping with no tail.
	/// </summary>
	private static readonly int DelayTailFrames =
		(int)MathF.Ceiling(SpatialAudioTransformCalculator.SampleRate / 1_000f) + 8;

	private static readonly RadarPing[] ByPipCount =
	[
		new(new(1)),
		new(new(2)),
		new(new(3)),
		new(new(4)),
	];

	private readonly RadarPingDesign _design;

	private RadarPing(in RadarPingDesign design)
	{
		_design = design;
		FrameCount = Math.Max(
			1,
			(int)MathF.Round(
				SpatialAudioTransformCalculator.SampleRate *
				((design.PipCount - 1) * RadarPingVoice.PipPeriodSeconds +
					RadarPingVoice.PipTailSeconds)));
		BusFrameCount = FrameCount + DelayTailFrames;
		Trim = Calibrate(design, FrameCount);
	}

	/// <summary>The highest count the radar can sound, and so the size of its catalogue.</summary>
	internal static int MaximumPipCount => ByPipCount.Length;

	/// <summary>How many frames the ping itself sounds for.</summary>
	internal int FrameCount { get; }

	/// <summary>How many frames its voice should be given on the bus, tail included.</summary>
	internal int BusFrameCount { get; }

	/// <summary>The gain that puts this ping on the shared reference.</summary>
	internal float Trim { get; }

	/// <summary>How many times this ping strikes.</summary>
	internal int PipCount => _design.PipCount;

	internal static RadarPing ForPipCount(int pipCount)
	{
		return ByPipCount[Math.Clamp(pipCount, 1, ByPipCount.Length) - 1];
	}

	internal RadarPingVoice CreateVoice() => new(_design, FrameCount, Trim);

	/// <summary>
	/// Levelled per pip count rather than once for the family, because loudness is
	/// measured over the whole cue and a four-strike ping spends far more of its length
	/// sounding than a one-strike ping does. Sharing a single trim would have made the
	/// longer counts audibly louder for saying no more.
	/// </summary>
	private static float Calibrate(in RadarPingDesign design, int frameCount)
	{
		float[] samples = new float[frameCount];
		RadarPingVoice voice = new(design, frameCount, gain: 1f);
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
