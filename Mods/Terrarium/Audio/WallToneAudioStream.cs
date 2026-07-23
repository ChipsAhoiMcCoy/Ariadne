#nullable enable

using System;
using Microsoft.Xna.Framework.Audio;
using Terraria;
using Terraria.Audio;
using Terraria.ModLoader;
using Terrarium.Configs;
using Terrarium.Ingame.WallTones;

namespace Terrarium.Audio;

internal sealed class WallToneAudioStream : IDisposable
{
	private const int FramesPerBuffer = 512;
	private const int TargetQueuedBuffers = 6;
	private const float VoiceHeadroomGain = 0.28f;
	private const float MinimumFrequency = 320f;
	private const float MaximumFrequency = 2_400f;

	private readonly Mod _owner;
	private readonly WallToneVoice _leftVoice = new(0x93A4_52E1u);
	private readonly WallToneVoice _rightVoice = new(0xD17B_8305u);
	private readonly WallToneVoice _ceilingVoice = new(0x6C8E_9CF3u);
	private readonly SpatialAudioEmitter _leftEmitter = new();
	private readonly SpatialAudioEmitter _rightEmitter = new();
	private readonly SpatialAudioEmitter _ceilingEmitter = new();
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

	private WallToneAudioStream(Mod owner, DynamicSoundEffectInstance stream)
	{
		_owner = owner;
		_stream = stream;
	}

	internal static WallToneAudioStream? TryCreate(Mod owner)
	{
		if (Main.dedServ)
		{
			return null;
		}
		if (!SoundEngine.IsAudioSupported)
		{
			owner.Logger.Warn("Wall tones are unavailable because this client does not support audio.");
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
			owner.Logger.Warn($"Wall-tone streaming could not be initialized and will remain silent: {exception.GetBaseException().Message}");
			return null;
		}
	}

	internal void UpdateTargets(WallToneSnapshot snapshot, TerrariumClientConfig config)
	{
		if (_disposed || _stream is null)
		{
			return;
		}

		_itdEnabled = config.SpatialAudioItdEnabled;
		_maximumItdMilliseconds = config.SpatialAudioItdStrengthMilliseconds;
		float configuredGain = Math.Clamp(config.WallToneVolumePercent / 100f, 0f, 1f);
		float masterGain = configuredGain * Math.Clamp(Main.soundVolume, 0f, 1f) * VoiceHeadroomGain;
		SetVoiceTarget(_leftVoice, _leftEmitter, snapshot.Left, masterGain);
		SetVoiceTarget(_rightVoice, _rightEmitter, snapshot.Right, masterGain);
		SetVoiceTarget(_ceilingVoice, _ceilingEmitter, snapshot.Ceiling, masterGain);
		_isReset = false;
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

	internal void ResetForDiscontinuity()
	{
		if (_disposed || _stream is null)
		{
			return;
		}

		_isReset = false;
		StopAndReset();
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
			// Disposal must remain safe if the audio device has already disappeared.
		}
		_stream?.Dispose();
		_stream = null;
		_disposed = true;
		_isRunning = false;
		ResetSignalState();
	}

	private static void SetVoiceTarget(
		WallToneVoice voice,
		SpatialAudioEmitter emitter,
		WallToneRegionSnapshot snapshot,
		float masterGain)
	{
		float proximity = snapshot.Proximity;
		float distanceGain = SpatialAudioDistanceGain.FromProximity(proximity);
		float frequency = MinimumFrequency * MathF.Pow(MaximumFrequency / MinimumFrequency, proximity);
		voice.SetTarget(frequency, masterGain);
		emitter.SetTarget(new(
			snapshot.NormalizedPosition.X,
			snapshot.NormalizedPosition.Y,
			snapshot.HasHit ? distanceGain : 0f));
	}

	private void GenerateBuffer()
	{
		Array.Clear(_leftMix);
		Array.Clear(_rightMix);
		_leftEmitter.Render(_leftVoice, _itdEnabled, _maximumItdMilliseconds, _leftMix, _rightMix);
		_rightEmitter.Render(_rightVoice, _itdEnabled, _maximumItdMilliseconds, _leftMix, _rightMix);
		_ceilingEmitter.Render(_ceilingVoice, _itdEnabled, _maximumItdMilliseconds, _leftMix, _rightMix);

		for (int frame = 0; frame < FramesPerBuffer; frame++)
		{
			short left = Encode(SoftLimit(_leftMix[frame]));
			short right = Encode(SoftLimit(_rightMix[frame]));
			int byteIndex = frame * 4;
			_pcmBuffer[byteIndex] = (byte)left;
			_pcmBuffer[byteIndex + 1] = (byte)(left >> 8);
			_pcmBuffer[byteIndex + 2] = (byte)right;
			_pcmBuffer[byteIndex + 3] = (byte)(right >> 8);
		}
	}

	private void ResetSignalState()
	{
		_leftVoice.Reset();
		_rightVoice.Reset();
		_ceilingVoice.Reset();
		_leftEmitter.Reset();
		_rightEmitter.Reset();
		_ceilingEmitter.Reset();
		Array.Clear(_leftMix);
		Array.Clear(_rightMix);
		Array.Clear(_pcmBuffer);
		_isReset = true;
	}

	private void DisableAfterFailure(Exception exception)
	{
		if (!_failureLogged)
		{
			_owner.Logger.Warn($"Wall-tone streaming failed and has been disabled for this session: {exception.GetBaseException().Message}");
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

	private static float SoftLimit(float sample)
	{
		return MathF.Tanh(sample);
	}

	private static short Encode(float sample)
	{
		return (short)MathF.Round(Math.Clamp(sample, -1f, 1f) * short.MaxValue);
	}
}

internal sealed class WallToneVoice : ISpatialMonoSource
{
	private const float DefaultFilterQ = 1.4f;
	private static readonly float FrequencySmoothing = SmoothingCoefficient(0.035f);
	private static readonly float GainSmoothing = SmoothingCoefficient(0.020f);

	private readonly uint _initialNoiseState;
	private uint _noiseState;
	private float _targetFrequency = 320f;
	private float _targetGain;
	private float _currentFrequency = 320f;
	private float _currentGain;
	private float _integratorOne;
	private float _integratorTwo;

	internal WallToneVoice(uint seed)
	{
		_initialNoiseState = seed;
		_noiseState = _initialNoiseState;
	}

	internal void SetTarget(float frequency, float gain)
	{
		_targetFrequency = Math.Clamp(frequency, 120f, 6_000f);
		_targetGain = Math.Clamp(gain, 0f, 1f);
	}

	public float ReadSample(float pitchRatio)
	{
		_currentFrequency += (_targetFrequency - _currentFrequency) * FrequencySmoothing;
		_currentGain += (_targetGain - _currentGain) * GainSmoothing;
		float centerFrequency = Math.Clamp(_currentFrequency * pitchRatio, 120f, 6_000f);
		float bandPassedNoise = FilterBandPass(NextWhiteNoise(ref _noiseState), centerFrequency, DefaultFilterQ);
		return bandPassedNoise * _currentGain;
	}

	public void Reset()
	{
		_noiseState = _initialNoiseState;
		_targetFrequency = 320f;
		_targetGain = 0f;
		_currentFrequency = 320f;
		_currentGain = 0f;
		_integratorOne = 0f;
		_integratorTwo = 0f;
	}

	private float FilterBandPass(float input, float centerFrequency, float filterQ)
	{
		float g = MathF.Tan(MathF.PI * centerFrequency / SpatialAudioTransformCalculator.SampleRate);
		float k = 1f / filterQ;
		float a1 = 1f / (1f + g * (g + k));
		float v3 = input - _integratorTwo;
		float v1 = a1 * (_integratorOne + g * v3);
		float v2 = _integratorTwo + g * v1;
		_integratorOne = 2f * v1 - _integratorOne;
		_integratorTwo = 2f * v2 - _integratorTwo;
		return v1;
	}

	private static float NextWhiteNoise(ref uint state)
	{
		state ^= state << 13;
		state ^= state >> 17;
		state ^= state << 5;
		return (state & 0x00FF_FFFFu) / 8_388_607.5f - 1f;
	}

	private static float SmoothingCoefficient(float timeConstantSeconds)
	{
		return 1f - MathF.Exp(-1f / (SpatialAudioTransformCalculator.SampleRate * timeConstantSeconds));
	}

}
