#nullable enable

using System;
using Microsoft.Xna.Framework.Audio;
using Terraria;
using Terraria.Audio;
using Terraria.ModLoader;
using Terrarium.Configs;

namespace Terrarium.Audio;

internal readonly record struct HostileMobFlightTarget(
	float NormalizedX,
	float NormalizedY,
	float ViewportEdgeFraction);

internal sealed class HostileMobToneAudioStream : IDisposable
{
	private const int FramesPerBuffer = 512;
	private const int TargetQueuedBuffers = 6;
	private const int MaximumEmitterCount = 4;
	private const float VoiceHeadroomGain = 0.85f;

	private readonly Mod _owner;
	private readonly FlyingVectorVoice[] _voices =
	[
		new(0xA11C_E551u),
		new(0xB42D_7193u),
		new(0xC73E_62A5u),
		new(0xD84F_93B7u),
	];
	private readonly SpatialAudioEmitter[] _emitters = [new(), new(), new(), new()];
	private readonly HostileMobFlightTarget[] _targets = new HostileMobFlightTarget[MaximumEmitterCount];
	private readonly float[] _leftMix = new float[FramesPerBuffer];
	private readonly float[] _rightMix = new float[FramesPerBuffer];
	private readonly byte[] _pcmBuffer = new byte[FramesPerBuffer * 2 * sizeof(short)];
	private DynamicSoundEffectInstance? _stream;
	private WallToneSpatializationMode _spatialization;
	private float _maximumItdMilliseconds = 0.65f;
	private float _masterGain;
	private float _playerNormalizedY;
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

		try
		{
			if (!SoundEngine.IsAudioSupported)
			{
				throw new InvalidOperationException("this client does not support audio");
			}

			DynamicSoundEffectInstance stream = new(
				SpatialAudioTransformCalculator.SampleRate,
				AudioChannels.Stereo);
			return new(owner, stream);
		}
		catch (Exception exception)
		{
			owner.Logger.Warn($"Hostile-mob vectors could not be initialized and will remain silent: {exception.GetBaseException().Message}");
			return null;
		}
	}

	internal static float FlightDurationSeconds(float viewportEdgeFraction)
	{
		return FlyingVectorVoice.CalculateFlightDurationSeconds(viewportEdgeFraction);
	}

	internal void UpdateTargets(
		ReadOnlySpan<HostileMobFlightTarget> targets,
		int activeTargetCount,
		float playerNormalizedY,
		TerrariumClientConfig config)
	{
		if (_disposed || _stream is null)
		{
			return;
		}

		_spatialization = config.HostileMobToneSpatialization;
		_maximumItdMilliseconds = config.HostileMobToneItdMilliseconds;
		float configuredGain = Math.Clamp(config.HostileMobToneVolumePercent / 100f, 0f, 1f);
		_masterGain = configuredGain * Math.Clamp(Main.soundVolume, 0f, 1f) * VoiceHeadroomGain;
		_playerNormalizedY = float.IsFinite(playerNormalizedY)
			? Math.Clamp(playerNormalizedY, -1f, 1f)
			: 0f;
		int clampedCount = Math.Clamp(activeTargetCount, 0, Math.Min(MaximumEmitterCount, targets.Length));
		for (int index = 0; index < MaximumEmitterCount; index++)
		{
			_targets[index] = index < clampedCount
				? SanitizeTarget(targets[index])
				: default;
		}
		_isReset = false;
	}

	internal void StartFlight(int emitterIndex)
	{
		if (_disposed || _stream is null || (uint)emitterIndex >= MaximumEmitterCount)
		{
			return;
		}

		_voices[emitterIndex].Start(
			_targets[emitterIndex],
			_playerNormalizedY,
			_masterGain);
		_emitters[emitterIndex].Reset();
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

	private void GenerateBuffer()
	{
		Array.Clear(_leftMix);
		Array.Clear(_rightMix);
		for (int index = 0; index < MaximumEmitterCount; index++)
		{
			_emitters[index].RenderMoving(
				_voices[index],
				_spatialization,
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
			_targets[index] = default;
		}
		_masterGain = 0f;
		_playerNormalizedY = 0f;
		Array.Clear(_leftMix);
		Array.Clear(_rightMix);
		Array.Clear(_pcmBuffer);
		_isReset = true;
	}

	private void DisableAfterFailure(Exception exception)
	{
		if (!_failureLogged)
		{
			_owner.Logger.Warn($"Hostile-mob vector streaming failed and has been disabled for this session: {exception.GetBaseException().Message}");
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

	private static HostileMobFlightTarget SanitizeTarget(in HostileMobFlightTarget target)
	{
		return new(
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

internal sealed class FlyingVectorVoice : IMovingSpatialMonoSource
{
	private const float MinimumFlightSeconds = 0.22f;
	private const float MaximumFlightSeconds = 0.64f;
	private const float ImpactSeconds = 0.09f;
	private const float FlightFilterQ = 2.2f;
	private const float ImpactFilterQ = 1.2f;
	private const float FlightSignalGain = 0.55f;
	private const float ImpactNoiseGain = 0.65f;
	private const float ImpactBodyGain = 0.32f;
	private const float MinimumEndpointGain = 0.28f;

	private readonly uint _initialNoiseState;
	private uint _noiseState;
	private HostileMobFlightTarget _target;
	private float _playerNormalizedY;
	private float _masterGain;
	private int _sampleIndex;
	private int _flightSampleCount;
	private int _totalSampleCount;
	private float _flightIntegratorOne;
	private float _flightIntegratorTwo;
	private float _impactIntegratorOne;
	private float _impactIntegratorTwo;
	private float _impactPhase;

	internal FlyingVectorVoice(uint seed)
	{
		_initialNoiseState = seed;
		_noiseState = seed;
	}

	public SpatialSourceParameters CurrentSpatialParameters
	{
		get
		{
			if (!IsPlaying)
			{
				return default;
			}

			float progress = _sampleIndex < _flightSampleCount
				? Math.Clamp(_sampleIndex / (float)Math.Max(1, _flightSampleCount - 1), 0f, 1f)
				: 1f;
			float horizontalProgress = MathF.Pow(progress, 0.65f);
			float edgeFraction = _target.ViewportEdgeFraction * progress;
			float currentProximityGain = SpatialAudioDistanceGain.FromProximity(1f - edgeFraction);
			float gentleDistanceGain = MinimumEndpointGain + currentProximityGain * (1f - MinimumEndpointGain);
			return new(
				_target.NormalizedX * horizontalProgress,
				Lerp(_playerNormalizedY, _target.NormalizedY, progress),
				_masterGain * gentleDistanceGain);
		}
	}

	private bool IsPlaying => _sampleIndex < _totalSampleCount;

	internal static float CalculateFlightDurationSeconds(float viewportEdgeFraction)
	{
		float edgeFraction = float.IsFinite(viewportEdgeFraction)
			? Math.Clamp(viewportEdgeFraction, 0f, 1f)
			: 1f;
		return Lerp(MinimumFlightSeconds, MaximumFlightSeconds, edgeFraction);
	}

	internal void Start(
		in HostileMobFlightTarget target,
		float playerNormalizedY,
		float masterGain)
	{
		_target = target;
		_playerNormalizedY = Math.Clamp(playerNormalizedY, -1f, 1f);
		_masterGain = Math.Clamp(masterGain, 0f, 1f);
		_sampleIndex = 0;
		_flightSampleCount = Math.Max(
			1,
			(int)MathF.Round(
				CalculateFlightDurationSeconds(target.ViewportEdgeFraction) *
				SpatialAudioTransformCalculator.SampleRate));
		_totalSampleCount = _flightSampleCount +
			(int)MathF.Round(ImpactSeconds * SpatialAudioTransformCalculator.SampleRate);
		_noiseState = _initialNoiseState;
		_flightIntegratorOne = 0f;
		_flightIntegratorTwo = 0f;
		_impactIntegratorOne = 0f;
		_impactIntegratorTwo = 0f;
		_impactPhase = 0f;
	}

	public float ReadSample(float pitchRatio)
	{
		if (!IsPlaying)
		{
			return 0f;
		}

		float sample = _sampleIndex < _flightSampleCount
			? ReadFlightSample()
			: ReadImpactSample(_sampleIndex - _flightSampleCount);
		_sampleIndex++;
		return sample;
	}

	public void Reset()
	{
		_target = default;
		_playerNormalizedY = 0f;
		_masterGain = 0f;
		_sampleIndex = 0;
		_flightSampleCount = 0;
		_totalSampleCount = 0;
		_noiseState = _initialNoiseState;
		_flightIntegratorOne = 0f;
		_flightIntegratorTwo = 0f;
		_impactIntegratorOne = 0f;
		_impactIntegratorTwo = 0f;
		_impactPhase = 0f;
	}

	private float ReadFlightSample()
	{
		float progress = Math.Clamp(
			_sampleIndex / (float)Math.Max(1, _flightSampleCount - 1),
			0f,
			1f);
		float durationSeconds = _flightSampleCount / (float)SpatialAudioTransformCalculator.SampleRate;
		float attackFraction = Math.Min(1f, 0.04f / durationSeconds);
		float attack = attackFraction > 0f
			? Math.Clamp(progress / attackFraction, 0f, 1f)
			: 1f;
		float release = progress <= 0.72f
			? 1f
			: MathF.Pow(Math.Clamp((1f - progress) / 0.28f, 0f, 1f), 2f);
		float centerFrequency = Lerp(900f, 2_800f, progress);
		float filteredNoise = FilterBandPass(
			NextWhiteNoise(ref _noiseState),
			centerFrequency,
			FlightFilterQ,
			ref _flightIntegratorOne,
			ref _flightIntegratorTwo);
		return filteredNoise * attack * release * FlightSignalGain;
	}

	private float ReadImpactSample(int impactSampleIndex)
	{
		float impactTime = impactSampleIndex / (float)SpatialAudioTransformCalculator.SampleRate;
		float targetHeight = (_target.NormalizedY + 1f) * 0.5f;
		float initialBrightness = Lerp(4_300f, 850f, targetHeight);
		float noiseProgress = Math.Clamp(impactTime / 0.055f, 0f, 1f);
		float noiseEnvelope = MathF.Pow(1f - noiseProgress, 2f);
		float brightness = Lerp(initialBrightness, initialBrightness * 0.55f, noiseProgress);
		float impactNoise = FilterBandPass(
			NextWhiteNoise(ref _noiseState),
			brightness,
			ImpactFilterQ,
			ref _impactIntegratorOne,
			ref _impactIntegratorTwo) *
			noiseEnvelope *
			ImpactNoiseGain;

		float bodyProgress = Math.Clamp(impactTime / 0.075f, 0f, 1f);
		float initialBodyFrequency = Lerp(150f, 520f, _target.ViewportEdgeFraction);
		float bodyFrequency = initialBodyFrequency * MathF.Pow(0.58f, bodyProgress);
		_impactPhase += MathF.Tau * bodyFrequency / SpatialAudioTransformCalculator.SampleRate;
		if (_impactPhase >= MathF.Tau)
		{
			_impactPhase -= MathF.Tau;
		}
		float bodyEnvelope = MathF.Pow(1f - bodyProgress, 2f);
		float impactBody = MathF.Sin(_impactPhase) * bodyEnvelope * ImpactBodyGain;
		return impactNoise + impactBody;
	}

	private static float FilterBandPass(
		float input,
		float centerFrequency,
		float filterQ,
		ref float integratorOne,
		ref float integratorTwo)
	{
		float clampedFrequency = Math.Clamp(centerFrequency, 120f, 6_000f);
		float g = MathF.Tan(MathF.PI * clampedFrequency / SpatialAudioTransformCalculator.SampleRate);
		float k = 1f / filterQ;
		float a1 = 1f / (1f + g * (g + k));
		float v3 = input - integratorTwo;
		float v1 = a1 * (integratorOne + g * v3);
		float v2 = integratorTwo + g * v1;
		integratorOne = 2f * v1 - integratorOne;
		integratorTwo = 2f * v2 - integratorTwo;
		return v1;
	}

	private static float NextWhiteNoise(ref uint state)
	{
		state ^= state << 13;
		state ^= state >> 17;
		state ^= state << 5;
		return (state & 0x00FF_FFFFu) / 8_388_607.5f - 1f;
	}

	private static float Lerp(float from, float to, float amount)
	{
		return from + (to - from) * amount;
	}
}
