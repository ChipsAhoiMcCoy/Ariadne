#nullable enable

using System;

namespace Ariadne.Audio;

internal readonly record struct HostileMobToneTarget(
	bool IsActive,
	float NormalizedX,
	float NormalizedY,
	float Proximity);

/// <summary>
/// The four voices a hostile-mob bed is heard through, and the rule for handing one of
/// them from one mob to the next.
///
/// There are always more mobs than voices, so a slot is a seat whose occupant changes.
/// Emptying a seat by resetting it outright dropped a sounding voice to silence within
/// a single sample, which is a step in the middle of a waveform and is heard as a
/// click; with a crowd on screen the seats change hands several times a second and
/// those clicks arrive as crackle. A seat is therefore faded out before it is reused
/// and faded back in under its new occupant, and the reset itself happens while the
/// seat is silent, where it costs nothing. The same path covers a mob leaving the
/// field, which is the other way a seat empties.
/// </summary>
internal sealed class HostileMobToneVoiceBank
{
	internal const int SlotCount = 4;

	private const float CarrierFrequency = 320f;
	private const float MinimumModulationRate = 1.5f;
	private const float MaximumModulationRate = 12f;
	private const float EmitterGainAttackSeconds = 0.003f;

	/// <summary>
	/// How long a voice takes to leave its seat, and how long its replacement takes to
	/// arrive. Both are far under the fastest tick the bed produces, so a mob is still
	/// heard on the frame it matters; they only have to outlast a single sample, which
	/// is what separates a fade from a step. Arrival is the slower of the two because a
	/// tone appearing is more noticeable than one leaving.
	/// </summary>
	private const float RetireSeconds = 0.006f;
	private const float ArriveSeconds = 0.010f;

	private static readonly int CalibrationFrames = SpatialAudioTransformCalculator.SampleRate;
	private static readonly float RetireStep = StepFor(RetireSeconds);
	private static readonly float ArriveStep = StepFor(ArriveSeconds);

	/// <summary>
	/// The gain that puts one voice on the shared reference, measured once at the cadence
	/// the closest range ticks at.
	///
	/// Measuring it separately at every cadence looked like it was correcting for the rate
	/// carrying energy of its own, and instead made the rate pay for itself. Loudness is
	/// read over a window a third of a second wide, which a 12 Hz tick train fills and a
	/// 1.5 Hz one barely touches, so normalizing each cadence to the same window loudness
	/// handed a distant mob a six-decibel boost that cancelled almost all of its distance
	/// gain: across the first twenty tiles of an eighty-tile range, a tick moved by six
	/// tenths of a decibel. One measurement leaves every tick the same height, so a nearby
	/// mob is loud and sounds often while a far one is quiet and sounds rarely, and
	/// distance gain is the only thing setting level.
	/// </summary>
	internal static readonly float VoiceGain = CalibrateVoiceGain();

	private readonly ModulatedTriangleToneVoice[] _voices =
	[
		new(0.00f, 0.00f),
		new(0.19f, 0.25f),
		new(0.41f, 0.50f),
		new(0.67f, 0.75f),
	];
	private readonly SpatialAudioEmitter[] _emitters =
	[
		new(EmitterGainAttackSeconds),
		new(EmitterGainAttackSeconds),
		new(EmitterGainAttackSeconds),
		new(EmitterGainAttackSeconds),
	];
	private readonly Seat[] _seats = [new(), new(), new(), new()];
	private float[] _scratchLeft = [];
	private float[] _scratchRight = [];
	private SpatialAudioSettings _settings;

	internal void SetTargets(
		ReadOnlySpan<HostileMobToneTarget> targets,
		float masterGain,
		in SpatialAudioSettings settings)
	{
		_settings = settings;
		for (int slot = 0; slot < SlotCount; slot++)
		{
			HostileMobToneTarget target = slot < targets.Length
				? SanitizeTarget(targets[slot])
				: default;
			Seat seat = _seats[slot];
			if (seat.IsRetiring)
			{
				// The seat is still emptying. Hold what it should become until it has,
				// or the incoming mob would be levelled against the outgoing one's fade.
				seat.PendingTarget = target;
				seat.PendingMasterGain = masterGain;
				seat.HasPendingTarget = true;
				continue;
			}

			Apply(slot, target, masterGain, place: false);
		}
	}

	/// <summary>
	/// Empties a seat so it can be given to someone else. The voice keeps sounding
	/// while it fades, and the reset lands once it cannot be heard.
	/// </summary>
	internal void Retire(int slot)
	{
		if ((uint)slot >= SlotCount)
		{
			return;
		}

		_seats[slot].IsRetiring = true;
		_seats[slot].HasPendingTarget = false;
	}

	internal void RetireAll()
	{
		for (int slot = 0; slot < SlotCount; slot++)
		{
			Retire(slot);
		}
	}

	/// <summary>
	/// Silences everything at once, without a fade. Only for tearing the bank down,
	/// where nothing downstream will hear the step.
	/// </summary>
	internal void ResetNow()
	{
		for (int slot = 0; slot < SlotCount; slot++)
		{
			_voices[slot].Reset();
			_emitters[slot].Reset();
			_seats[slot].Reset();
		}
	}

	internal void Render(Span<float> left, Span<float> right)
	{
		EnsureScratch(left.Length);
		Span<float> slotLeft = _scratchLeft.AsSpan(0, left.Length);
		Span<float> slotRight = _scratchRight.AsSpan(0, left.Length);

		for (int slot = 0; slot < SlotCount; slot++)
		{
			Seat seat = _seats[slot];

			// The reset waits for a block boundary at which the seat is already silent,
			// so the state it discards cannot be heard on either side of it.
			if (seat.IsRetiring && seat.Level <= 0f)
			{
				CompleteRetirement(slot, seat);
			}

			slotLeft.Clear();
			slotRight.Clear();
			_emitters[slot].Render(_voices[slot], _settings, slotLeft, slotRight);

			float level = seat.Level;
			float target = seat.IsRetiring ? 0f : 1f;
			for (int frame = 0; frame < left.Length; frame++)
			{
				level = level < target
					? MathF.Min(target, level + ArriveStep)
					: MathF.Max(target, level - RetireStep);
				left[frame] += slotLeft[frame] * level;
				right[frame] += slotRight[frame] * level;
			}
			seat.Level = level;
		}
	}

	private void CompleteRetirement(int slot, Seat seat)
	{
		_voices[slot].Reset();
		_emitters[slot].Reset();
		seat.IsRetiring = false;
		if (!seat.HasPendingTarget)
		{
			return;
		}

		// Placed rather than moved to: the seat is silent, so its new occupant can be
		// put where it actually is instead of sliding there from where the last one was.
		Apply(slot, seat.PendingTarget, seat.PendingMasterGain, place: true);
		seat.HasPendingTarget = false;
	}

	private void Apply(int slot, in HostileMobToneTarget target, float masterGain, bool place)
	{
		float proximity = target.IsActive ? target.Proximity : 0f;
		_voices[slot].SetTarget(
			CarrierFrequency,
			ModulationRateForProximity(proximity),
			target.IsActive ? masterGain * VoiceGain : 0f);

		SpatialSourceParameters parameters = new(
			target.NormalizedX,
			target.NormalizedY,
			target.IsActive ? SpatialAudioDistanceGain.FromProximity(proximity) : 0f);
		if (place)
		{
			_emitters[slot].SetTargetImmediately(parameters);
		}
		else
		{
			_emitters[slot].SetTarget(parameters);
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
		ModulatedTriangleToneVoice voice = new(0f, 0f);
		voice.SetTarget(CarrierFrequency, MaximumModulationRate, gain: 1f);
		float[] samples = new float[CalibrationFrames];
		for (int index = 0; index < samples.Length; index++)
		{
			samples[index] = voice.ReadSample(pitchRatio: 1f);
		}

		return AuthoredAudioLevels.LoudnessTrim(
			samples,
			AuthoredAudioLevels.SpatialVoiceReferenceLoudness);
	}

	private static float ModulationRateForProximity(float proximity)
	{
		return MinimumModulationRate * MathF.Pow(
			MaximumModulationRate / MinimumModulationRate,
			Math.Clamp(proximity, 0f, 1f));
	}

	private static HostileMobToneTarget SanitizeTarget(in HostileMobToneTarget target)
	{
		return new(
			target.IsActive,
			float.IsFinite(target.NormalizedX) ? Math.Clamp(target.NormalizedX, -1f, 1f) : 0f,
			float.IsFinite(target.NormalizedY) ? Math.Clamp(target.NormalizedY, -1f, 1f) : 0f,
			float.IsFinite(target.Proximity) ? Math.Clamp(target.Proximity, 0f, 1f) : 0f);
	}

	private sealed class Seat
	{
		internal float Level = 1f;
		internal bool IsRetiring;
		internal bool HasPendingTarget;
		internal HostileMobToneTarget PendingTarget;
		internal float PendingMasterGain;

		internal void Reset()
		{
			Level = 1f;
			IsRetiring = false;
			HasPendingTarget = false;
			PendingTarget = default;
			PendingMasterGain = 0f;
		}
	}
}
