#nullable enable

using System;
using Terraria;
using Terraria.ModLoader;
using Ariadne.Configs;

namespace Ariadne.Audio;

internal readonly record struct HostileMobToneTarget(
	bool IsActive,
	float NormalizedX,
	float NormalizedY,
	float Proximity);

internal sealed class HostileMobToneAudioStream : IAudioBusSource, IDisposable
{
	private const int MaximumEmitterCount = 4;
	private const float CarrierFrequency = 320f;
	private const float MinimumModulationRate = 1.5f;
	private const float MaximumModulationRate = 12f;
	private const float EmitterGainAttackSeconds = 0.003f;
	private static readonly int CalibrationFrames = SpatialAudioTransformCalculator.SampleRate;

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
	private static readonly float VoiceGain = CalibrateVoiceGain();

	private readonly AriadneAudioBus _bus;
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
	private SpatialAudioSettings _settings;
	private bool _isReset = true;
	private bool _disposed;

	private HostileMobToneAudioStream(AriadneAudioBus bus)
	{
		_bus = bus;
		bus.Add(this);
	}

	internal static HostileMobToneAudioStream? TryCreate(Mod owner)
	{
		if (Main.dedServ)
		{
			return null;
		}

		AriadneAudioBus? bus = AudioBusSystem.Bus;
		if (bus is null)
		{
			owner.Logger.Warn("Hostile-mob tones are unavailable because the audio bus could not be created.");
			return null;
		}

		return new(bus);
	}

	internal void UpdateTargets(
		ReadOnlySpan<HostileMobToneTarget> targets,
		AriadneClientConfig config)
	{
		if (_disposed)
		{
			return;
		}

		_settings = config.ToSpatialAudioSettings();
		// Terraria's sound slider is applied once, by the bus, for the whole mix.
		float masterGain = Math.Clamp(config.HostileMobToneVolumePercent / 100f, 0f, 1f);
		for (int index = 0; index < MaximumEmitterCount; index++)
		{
			HostileMobToneTarget target = index < targets.Length
				? SanitizeTarget(targets[index])
				: default;
			SetVoiceTarget(_voices[index], _emitters[index], target, masterGain);
		}
		_isReset = false;
	}

	internal void ResetEmitter(int emitterIndex)
	{
		if ((uint)emitterIndex >= MaximumEmitterCount)
		{
			return;
		}

		_voices[emitterIndex].Reset();
		_emitters[emitterIndex].Reset();
	}

	public bool Render(Span<float> left, Span<float> right)
	{
		if (_disposed)
		{
			return false;
		}

		for (int index = 0; index < MaximumEmitterCount; index++)
		{
			_emitters[index].Render(_voices[index], _settings, left, right);
		}
		return true;
	}

	internal void StopAndReset()
	{
		if (_disposed || _isReset)
		{
			return;
		}
		ResetSignalState();
	}

	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		_bus.Remove(this);
		_disposed = true;
		ResetSignalState();
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

	private static void SetVoiceTarget(
		ModulatedTriangleToneVoice voice,
		SpatialAudioEmitter emitter,
		in HostileMobToneTarget target,
		float masterGain)
	{
		float proximity = target.IsActive ? target.Proximity : 0f;
		float distanceGain = SpatialAudioDistanceGain.FromProximity(proximity);
		float modulationRate = ModulationRateForProximity(proximity);
		voice.SetTarget(
			CarrierFrequency,
			modulationRate,
			target.IsActive ? masterGain * VoiceGain : 0f);
		emitter.SetTarget(new(
			target.NormalizedX,
			target.NormalizedY,
			target.IsActive ? distanceGain : 0f));
	}

	private void ResetSignalState()
	{
		for (int index = 0; index < MaximumEmitterCount; index++)
		{
			_voices[index].Reset();
			_emitters[index].Reset();
		}
		_isReset = true;
	}

	private static HostileMobToneTarget SanitizeTarget(in HostileMobToneTarget target)
	{
		return new(
			target.IsActive,
			float.IsFinite(target.NormalizedX) ? Math.Clamp(target.NormalizedX, -1f, 1f) : 0f,
			float.IsFinite(target.NormalizedY) ? Math.Clamp(target.NormalizedY, -1f, 1f) : 0f,
			float.IsFinite(target.Proximity) ? Math.Clamp(target.Proximity, 0f, 1f) : 0f);
	}
}

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
