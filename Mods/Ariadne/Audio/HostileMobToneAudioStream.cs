#nullable enable

using System;
using Microsoft.Xna.Framework.Audio;
using Terraria;
using Terraria.Audio;
using Terraria.ModLoader;
using Ariadne.Configs;

namespace Ariadne.Audio;

internal readonly record struct HostileMobToneTarget(
	bool IsActive,
	float NormalizedX,
	float NormalizedY,
	float ViewportEdgeFraction);

internal sealed class HostileMobToneAudioStream : IDisposable
{
	private const int FramesPerBuffer = 512;
	private const int TargetQueuedBuffers = 6;
	private const int MaximumEmitterCount = 4;
	private const float CarrierFrequency = 320f;
	private const float MinimumModulationRate = 1.5f;
	private const float MaximumModulationRate = 12f;
	private const float EmitterGainAttackSeconds = 0.003f;
	private const int CalibrationFrames = SpatialAudioTransformCalculator.SampleRate;

	/// <summary>
	/// The gain that puts one voice on the shared reference at its closest range,
	/// measured rather than trimmed by ear because the tone spends most of its cycle
	/// between ticks and a raw gain says little about how loud it lands.
	/// </summary>
	private static readonly float VoiceGain = CalibrateVoiceGain();

	private readonly Mod _owner;
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
	private readonly float[] _leftMix = new float[FramesPerBuffer];
	private readonly float[] _rightMix = new float[FramesPerBuffer];
	private readonly byte[] _pcmBuffer = new byte[FramesPerBuffer * 2 * sizeof(short)];
	private DynamicSoundEffectInstance? _stream;
	private bool _itdEnabled = true;
	private float _maximumItdMilliseconds = 0.65f;
	private bool _isRunning;
	private bool _isReset = true;
	private bool _failureLogged;
	private bool _disposed;

	private HostileMobToneAudioStream(Mod owner, DynamicSoundEffectInstance stream)
	{
		_owner = owner;
		_stream = stream;
	}

	internal static HostileMobToneAudioStream? TryCreate(Mod owner)
	{
		if (Main.dedServ)
		{
			return null;
		}
		if (!SoundEngine.IsAudioSupported)
		{
			owner.Logger.Warn("Hostile-mob tones are unavailable because this client does not support audio.");
			return null;
		}

		try
		{
			DynamicSoundEffectInstance stream = new(
				SpatialAudioTransformCalculator.SampleRate,
				AudioChannels.Stereo);
			return new(owner, stream);
		}
		catch (Exception exception)
		{
			owner.Logger.Warn($"Hostile-mob tone streaming could not be initialized and will remain silent: {exception.GetBaseException().Message}");
			return null;
		}
	}

	internal void UpdateTargets(
		ReadOnlySpan<HostileMobToneTarget> targets,
		AriadneClientConfig config)
	{
		if (_disposed || _stream is null)
		{
			return;
		}

		_itdEnabled = config.SpatialAudioItdEnabled;
		_maximumItdMilliseconds = config.SpatialAudioItdStrengthMilliseconds;
		float configuredGain = Math.Clamp(config.HostileMobToneVolumePercent / 100f, 0f, 1f);
		float masterGain = configuredGain * Math.Clamp(Main.soundVolume, 0f, 1f) * VoiceGain;
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

	internal void Pump()
	{
		if (_disposed || _stream is null)
		{
			return;
		}

		try
		{
			while (_stream.PendingBufferCount < TargetQueuedBuffers)
			{
				GenerateBuffer();
				_stream.SubmitBuffer(_pcmBuffer);
			}
			if (!_isRunning || _stream.State != SoundState.Playing)
			{
				_stream.Play();
				_isRunning = true;
			}
		}
		catch (Exception exception)
		{
			DisableAfterFailure(exception);
		}
	}

	internal void StopAndReset()
	{
		if (_disposed || _stream is null || _isReset)
		{
			return;
		}

		try
		{
			if (_isRunning || _stream.State != SoundState.Stopped)
			{
				_stream.Stop(true);
			}
		}
		catch (Exception exception)
		{
			DisableAfterFailure(exception);
			return;
		}

		_isRunning = false;
		ResetSignalState();
	}

	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		try
		{
			_stream?.Stop(true);
		}
		catch
		{
			// Disposal remains safe when the audio device has already disappeared.
		}
		_stream?.Dispose();
		_stream = null;
		_disposed = true;
		_isRunning = false;
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

	private static void SetVoiceTarget(
		ModulatedTriangleToneVoice voice,
		SpatialAudioEmitter emitter,
		in HostileMobToneTarget target,
		float masterGain)
	{
		float proximity = target.IsActive ? 1f - target.ViewportEdgeFraction : 0f;
		float distanceGain = SpatialAudioDistanceGain.FromProximity(proximity);
		float modulationRate = MinimumModulationRate *
			MathF.Pow(MaximumModulationRate / MinimumModulationRate, proximity);
		voice.SetTarget(CarrierFrequency, modulationRate, target.IsActive ? masterGain : 0f);
		emitter.SetTarget(new(
			target.NormalizedX,
			target.NormalizedY,
			target.IsActive ? distanceGain : 0f));
	}

	private void GenerateBuffer()
	{
		Array.Clear(_leftMix);
		Array.Clear(_rightMix);
		for (int index = 0; index < MaximumEmitterCount; index++)
		{
			_emitters[index].Render(
				_voices[index],
				_itdEnabled,
				_maximumItdMilliseconds,
				_leftMix,
				_rightMix);
		}

		for (int frame = 0; frame < FramesPerBuffer; frame++)
		{
			short left = Encode(MathF.Tanh(_leftMix[frame]));
			short right = Encode(MathF.Tanh(_rightMix[frame]));
			int byteIndex = frame * 4;
			_pcmBuffer[byteIndex] = (byte)left;
			_pcmBuffer[byteIndex + 1] = (byte)(left >> 8);
			_pcmBuffer[byteIndex + 2] = (byte)right;
			_pcmBuffer[byteIndex + 3] = (byte)(right >> 8);
		}
	}

	private void ResetSignalState()
	{
		for (int index = 0; index < MaximumEmitterCount; index++)
		{
			_voices[index].Reset();
			_emitters[index].Reset();
		}
		Array.Clear(_leftMix);
		Array.Clear(_rightMix);
		Array.Clear(_pcmBuffer);
		_isReset = true;
	}

	private void DisableAfterFailure(Exception exception)
	{
		if (!_failureLogged)
		{
			_owner.Logger.Warn($"Hostile-mob tone streaming failed and has been disabled for this session: {exception.GetBaseException().Message}");
			_failureLogged = true;
		}

		try
		{
			_stream?.Dispose();
		}
		catch
		{
			// The stream is already unusable.
		}
		_stream = null;
		_isRunning = false;
		ResetSignalState();
	}

	private static HostileMobToneTarget SanitizeTarget(in HostileMobToneTarget target)
	{
		return new(
			target.IsActive,
			float.IsFinite(target.NormalizedX) ? Math.Clamp(target.NormalizedX, -1f, 1f) : 0f,
			float.IsFinite(target.NormalizedY) ? Math.Clamp(target.NormalizedY, -1f, 1f) : 0f,
			float.IsFinite(target.ViewportEdgeFraction)
				? Math.Clamp(target.ViewportEdgeFraction, 0f, 1f)
				: 1f);
	}

	private static short Encode(float sample)
	{
		return (short)MathF.Round(Math.Clamp(sample, -1f, 1f) * short.MaxValue);
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
