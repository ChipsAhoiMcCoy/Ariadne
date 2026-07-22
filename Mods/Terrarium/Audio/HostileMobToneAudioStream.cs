#nullable enable

using System;
using Microsoft.Xna.Framework.Audio;
using Terraria;
using Terraria.Audio;
using Terraria.ModLoader;
using Terrarium.Configs;

namespace Terrarium.Audio;

internal sealed class HostileMobToneAudioStream : IDisposable
{
	private const string PulseAssetPath = "Assets/Audio/HostileMobPulse.wav";
	private const int FramesPerBuffer = 512;
	private const int TargetQueuedBuffers = 6;
	private const int MaximumEmitterCount = 4;
	private const float VoiceHeadroomGain = 0.85f;

	private readonly Mod _owner;
	private readonly DecodedPcmPulseSource[] _voices = new DecodedPcmPulseSource[MaximumEmitterCount];
	private readonly SpatialAudioEmitter[] _emitters = new SpatialAudioEmitter[MaximumEmitterCount];
	private readonly bool[] _snapOnNextTarget = new bool[MaximumEmitterCount];
	private readonly float[] _leftMix = new float[FramesPerBuffer];
	private readonly float[] _rightMix = new float[FramesPerBuffer];
	private readonly byte[] _pcmBuffer = new byte[FramesPerBuffer * 2 * sizeof(short)];
	private DynamicSoundEffectInstance? _stream;
	private WallToneSpatializationMode _spatialization;
	private float _maximumItdMilliseconds = 0.65f;
	private bool _isRunning;
	private bool _isReset = true;
	private bool _failureLogged;
	private bool _disposed;

	private HostileMobToneAudioStream(Mod owner, DynamicSoundEffectInstance stream, float[] pulseSamples)
	{
		_owner = owner;
		_stream = stream;
		for (int index = 0; index < MaximumEmitterCount; index++)
		{
			_voices[index] = new(pulseSamples);
			_emitters[index] = new();
		}
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

			byte[] waveBytes = owner.GetFileBytes(PulseAssetPath);
			float[] samples = PcmWaveDecoder.DecodeMono16(
				waveBytes,
				SpatialAudioTransformCalculator.SampleRate);
			DynamicSoundEffectInstance stream = new(
				SpatialAudioTransformCalculator.SampleRate,
				AudioChannels.Stereo);
			return new(owner, stream, samples);
		}
		catch (Exception exception)
		{
			owner.Logger.Warn($"Hostile-mob tones could not be initialized and will remain silent: {exception.GetBaseException().Message}");
			return null;
		}
	}

	internal void UpdateTargets(
		ReadOnlySpan<SpatialSourceParameters> targets,
		int activeTargetCount,
		TerrariumClientConfig config)
	{
		if (_disposed || _stream is null)
		{
			return;
		}

		_spatialization = config.HostileMobToneSpatialization;
		_maximumItdMilliseconds = config.HostileMobToneItdMilliseconds;
		float configuredGain = Math.Clamp(config.HostileMobToneVolumePercent / 100f, 0f, 1f);
		float masterGain = configuredGain * Math.Clamp(Main.soundVolume, 0f, 1f) * VoiceHeadroomGain;
		int clampedCount = Math.Clamp(activeTargetCount, 0, Math.Min(MaximumEmitterCount, targets.Length));
		for (int index = 0; index < MaximumEmitterCount; index++)
		{
			SpatialSourceParameters target = index < clampedCount
				? targets[index] with { DistanceGain = targets[index].DistanceGain * masterGain }
				: default;
			if (_snapOnNextTarget[index])
			{
				_emitters[index].SetTargetImmediately(target);
				_snapOnNextTarget[index] = false;
			}
			else
			{
				_emitters[index].SetTarget(target);
			}
		}
		_isReset = false;
	}

	internal void StartPulse(int emitterIndex)
	{
		if (_disposed || _stream is null || (uint)emitterIndex >= MaximumEmitterCount)
		{
			return;
		}
		_voices[emitterIndex].Start();
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
		_snapOnNextTarget[emitterIndex] = true;
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
			// Disposal remains safe when the audio device has disappeared.
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
			_emitters[index].Render(
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
			_snapOnNextTarget[index] = false;
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

	private static short Encode(float sample)
	{
		return (short)MathF.Round(Math.Clamp(sample, -1f, 1f) * short.MaxValue);
	}
}

internal sealed class DecodedPcmPulseSource : ISpatialMonoSource
{
	private readonly float[] _samples;
	private float _position;
	private bool _isPlaying;

	internal DecodedPcmPulseSource(float[] samples)
	{
		_samples = samples;
	}

	internal void Start()
	{
		_position = 0f;
		_isPlaying = true;
	}

	public float ReadSample(float pitchRatio)
	{
		if (!_isPlaying)
		{
			return 0f;
		}

		int currentIndex = (int)_position;
		if ((uint)currentIndex >= _samples.Length)
		{
			_isPlaying = false;
			return 0f;
		}

		int nextIndex = Math.Min(currentIndex + 1, _samples.Length - 1);
		float fraction = _position - currentIndex;
		float sample = _samples[currentIndex] + (_samples[nextIndex] - _samples[currentIndex]) * fraction;
		_position += float.IsFinite(pitchRatio) ? Math.Clamp(pitchRatio, 0.5f, 2f) : 1f;
		return sample;
	}

	public void Reset()
	{
		_position = 0f;
		_isPlaying = false;
	}
}
