#nullable enable

using System;

namespace Ariadne.Audio;

/// <summary>
/// The one enemy the bed is currently following, where it is, and whether it is the
/// enemy the player holds.
/// </summary>
internal readonly record struct HostileMobToneTarget(
	bool IsActive,
	bool IsLocked,
	float NormalizedX,
	float NormalizedY,
	float Proximity);

/// <summary>
/// The single voice hostile enemies are heard through, and the rule for moving it from
/// one enemy to the next.
///
/// One voice rather than four seats. Four of them meant the bed was constantly handing
/// seats between mobs as a crowd shifted, and every handoff was a fade out and a fade in
/// on top of the tick envelopes each seat was already restarting. A listener also cannot
/// count four tick trains at once, so what those seats mainly produced was density.
///
/// Moving the voice to a different enemy is still a fade rather than a jump, because
/// level and pan would otherwise step within a single sample and a step in the middle of
/// a waveform is heard as a click. The voice is faded out, placed at its new enemy while
/// it is silent, and faded back in; the reset that discards the old enemy's phase and
/// delay history happens in that silence, where it costs nothing.
/// </summary>
internal sealed class HostileMobToneBed
{
	private const float EmitterGainAttackSeconds = 0.003f;

	/// <summary>
	/// How long the voice takes to leave one enemy and to arrive at the next. Both only
	/// have to outlast a single sample, which is what separates a fade from a step, and
	/// both are short enough that a handoff reads as the tone stepping across to another
	/// enemy rather than as the bed dropping out. Arrival is the slower of the two
	/// because a tone appearing is more noticeable than one leaving.
	/// </summary>
	private const float RetireSeconds = 0.008f;
	private const float ArriveSeconds = 0.012f;

	private static readonly int CalibrationFrames = SpatialAudioTransformCalculator.SampleRate;
	private static readonly float RetireStep = StepFor(RetireSeconds);
	private static readonly float ArriveStep = StepFor(ArriveSeconds);

	/// <summary>
	/// The gain that puts the voice on the shared reference, measured once at the pitch
	/// an unheld enemy sounds at. Measuring it again for the held pitch would make the
	/// fifth a change in level as well as in pitch, and level here means distance.
	/// </summary>
	internal static readonly float VoiceGain = CalibrateVoiceGain();

	private readonly HostileMobToneVoice _voice = new();
	private readonly SpatialAudioEmitter _emitter = new(EmitterGainAttackSeconds);
	private float[] _scratchLeft = [];
	private float[] _scratchRight = [];
	private SpatialAudioSettings _settings;
	private float _level = 1f;
	private bool _isRetiring;
	private bool _hasPendingTarget;
	private HostileMobToneTarget _pendingTarget;
	private float _pendingMasterGain;
	private bool _distanceAttenuationEnabled = true;

	internal void SetTarget(
		in HostileMobToneTarget target,
		float masterGain,
		in SpatialAudioSettings settings,
		bool distanceAttenuationEnabled)
	{
		_settings = settings;
		_distanceAttenuationEnabled = distanceAttenuationEnabled;
		HostileMobToneTarget sanitized = Sanitize(target);
		if (_isRetiring)
		{
			// The voice is still leaving its last enemy. Hold what it should become until
			// it has, or the incoming enemy would be levelled against the outgoing fade.
			_pendingTarget = sanitized;
			_pendingMasterGain = masterGain;
			_hasPendingTarget = true;
			return;
		}

		Apply(sanitized, masterGain, place: false);
	}

	/// <summary>
	/// Takes the voice off whichever enemy it is on. It keeps sounding while it fades,
	/// and whatever it is given next is placed once it cannot be heard.
	/// </summary>
	internal void Handoff()
	{
		_isRetiring = true;
		_hasPendingTarget = false;
	}

	/// <summary>
	/// Silences everything at once, without a fade. Only for tearing the bed down, where
	/// nothing downstream will hear the step.
	/// </summary>
	internal void ResetNow()
	{
		_voice.Reset();
		_emitter.Reset();
		_level = 1f;
		_isRetiring = false;
		_hasPendingTarget = false;
		_pendingTarget = default;
		_pendingMasterGain = 0f;
	}

	internal void Render(Span<float> left, Span<float> right)
	{
		// The reset waits for a block boundary at which the voice is already silent, so
		// the state it discards cannot be heard on either side of it.
		if (_isRetiring && _level <= 0f)
		{
			CompleteHandoff();
		}

		if (!_isRetiring && _level >= 1f)
		{
			// Settled on one enemy, which is nearly always. Nothing has to be scaled, so
			// the voice is written straight into the mix.
			_emitter.Render(_voice, _settings, left, right);
			return;
		}

		EnsureScratch(left.Length);
		Span<float> voiceLeft = _scratchLeft.AsSpan(0, left.Length);
		Span<float> voiceRight = _scratchRight.AsSpan(0, left.Length);
		voiceLeft.Clear();
		voiceRight.Clear();
		_emitter.Render(_voice, _settings, voiceLeft, voiceRight);

		float level = _level;
		float target = _isRetiring ? 0f : 1f;
		for (int frame = 0; frame < left.Length; frame++)
		{
			level = level < target
				? MathF.Min(target, level + ArriveStep)
				: MathF.Max(target, level - RetireStep);
			left[frame] += voiceLeft[frame] * level;
			right[frame] += voiceRight[frame] * level;
		}
		_level = level;
	}

	private void CompleteHandoff()
	{
		_voice.Reset();
		_emitter.Reset();
		_isRetiring = false;
		if (!_hasPendingTarget)
		{
			return;
		}

		// Placed rather than moved to: the voice is silent, so its new enemy can be put
		// where it actually is instead of sliding there from where the last one was.
		Apply(_pendingTarget, _pendingMasterGain, place: true);
		_hasPendingTarget = false;
	}

	private void Apply(in HostileMobToneTarget target, float masterGain, bool place)
	{
		_voice.SetTarget(
			target.IsLocked
				? HostileMobToneVoice.BaseFrequency * HostileMobToneVoice.LockedFrequencyRatio
				: HostileMobToneVoice.BaseFrequency,
			target.IsActive ? masterGain * VoiceGain : 0f);

		SpatialSourceParameters parameters = new(
			target.NormalizedX,
			target.NormalizedY,
			target.IsActive ? SpatialAudioDistanceGain.FromProximity(target.Proximity, _distanceAttenuationEnabled) : 0f);
		if (place)
		{
			_emitter.SetTargetImmediately(parameters);
		}
		else
		{
			_emitter.SetTarget(parameters);
		}
	}

	private void EnsureScratch(int frameCount)
	{
		if (_scratchLeft.Length >= frameCount)
		{
			return;
		}

		_scratchLeft = new float[frameCount];
		_scratchRight = new float[frameCount];
	}

	private static float StepFor(float seconds)
	{
		return 1f / MathF.Max(1f, seconds * SpatialAudioTransformCalculator.SampleRate);
	}

	private static float CalibrateVoiceGain()
	{
		HostileMobToneVoice voice = new();
		voice.SetTarget(HostileMobToneVoice.BaseFrequency, gain: 1f);
		float[] samples = new float[CalibrationFrames];
		for (int index = 0; index < samples.Length; index++)
		{
			samples[index] = voice.ReadSample(pitchRatio: 1f);
		}

		return AuthoredAudioLevels.LoudnessTrim(
			samples,
			AuthoredAudioLevels.SpatialVoiceReferenceLoudness);
	}

	private static HostileMobToneTarget Sanitize(in HostileMobToneTarget target)
	{
		return new(
			target.IsActive,
			target.IsLocked,
			float.IsFinite(target.NormalizedX) ? Math.Clamp(target.NormalizedX, -1f, 1f) : 0f,
			float.IsFinite(target.NormalizedY) ? Math.Clamp(target.NormalizedY, -1f, 1f) : 0f,
			float.IsFinite(target.Proximity) ? Math.Clamp(target.Proximity, 0f, 1f) : 0f);
	}
}
