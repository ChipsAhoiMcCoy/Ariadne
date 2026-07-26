#nullable enable

using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using Terraria;
using Terraria.Audio;
using Terraria.ModLoader;
using Ariadne.Configs;

namespace Ariadne.Audio;

internal sealed class FreecamBodyBeaconAudioStream : IDisposable
{
	private const int FramesPerBuffer = 512;
	private const int TargetQueuedBuffers = 6;
	private const float VoiceHeadroomGain = 0.5f;

	private readonly Mod _owner;
	private readonly BodyBeaconVoice _voice = new();
	private readonly SpatialAudioEmitter _emitter = new(gainAttackSeconds: 0.003f);
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

	private FreecamBodyBeaconAudioStream(Mod owner, DynamicSoundEffectInstance stream)
	{
		_owner = owner;
		_stream = stream;
	}

	internal static FreecamBodyBeaconAudioStream? TryCreate(Mod owner)
	{
		if (Main.dedServ)
		{
			return null;
		}
		if (!SoundEngine.IsAudioSupported)
		{
			owner.Logger.Warn("The freecam body beacon is unavailable because this client does not support audio.");
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
			owner.Logger.Warn(
				$"Freecam body-beacon streaming could not be initialized and will remain silent: " +
				$"{exception.GetBaseException().Message}");
			return null;
		}
	}

	internal void UpdateTarget(Vector2 normalizedPosition, AriadneClientConfig config)
	{
		if (_disposed || _stream is null)
		{
			return;
		}

		_itdEnabled = config.SpatialAudioItdEnabled;
		_maximumItdMilliseconds = config.SpatialAudioItdStrengthMilliseconds;
		float configuredGain = Math.Clamp(config.FreecamBeaconVolumePercent / 100f, 0f, 1f);
		float masterGain =
			configuredGain * Math.Clamp(Main.soundVolume, 0f, 1f) * VoiceHeadroomGain;
		_voice.SetGain(masterGain);
		SpatialSourceParameters target = new(
			normalizedPosition.X,
			normalizedPosition.Y,
			DistanceGain: 1f);
		if (_isReset)
		{
			_emitter.SetTargetImmediately(target);
		}
		else
		{
			_emitter.SetTarget(target);
		}
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
			// Disposal remains safe if the audio device has disappeared.
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
		_emitter.Render(
			_voice,
			_itdEnabled,
			_maximumItdMilliseconds,
			_leftMix,
			_rightMix);

		for (int frame = 0; frame < FramesPerBuffer; frame++)
		{
			short left = Encode(_leftMix[frame]);
			short right = Encode(_rightMix[frame]);
			int byteIndex = frame * 4;
			_pcmBuffer[byteIndex] = (byte)left;
			_pcmBuffer[byteIndex + 1] = (byte)(left >> 8);
			_pcmBuffer[byteIndex + 2] = (byte)right;
			_pcmBuffer[byteIndex + 3] = (byte)(right >> 8);
		}
	}

	private void ResetSignalState()
	{
		_voice.Reset();
		_emitter.Reset();
		Array.Clear(_leftMix);
		Array.Clear(_rightMix);
		Array.Clear(_pcmBuffer);
		_isReset = true;
	}

	private void DisableAfterFailure(Exception exception)
	{
		if (!_failureLogged)
		{
			_owner.Logger.Warn(
				$"Freecam body-beacon streaming failed and has been disabled for this session: " +
				$"{exception.GetBaseException().Message}");
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

internal sealed class BodyBeaconVoice : ISpatialMonoSource
{
	private const float Frequency = 660f;
	private const float PulseDurationSeconds = 0.120f;
	private const float PulseIntervalSeconds = 0.750f;
	private const float EdgeSeconds = 0.005f;
	private const int PulseDurationSamples =
		(int)(PulseDurationSeconds * SpatialAudioTransformCalculator.SampleRate);
	private const int PulseIntervalSamples =
		(int)(PulseIntervalSeconds * SpatialAudioTransformCalculator.SampleRate);
	private const int EdgeSamples =
		(int)(EdgeSeconds * SpatialAudioTransformCalculator.SampleRate);

	private float _gain;
	private float _phase;
	private int _sampleInInterval;

	internal void SetGain(float gain)
	{
		_gain = Math.Clamp(gain, 0f, 1f);
	}

	public float ReadSample(float pitchRatio)
	{
		float sample = 0f;
		if (_sampleInInterval < PulseDurationSamples)
		{
			float envelope = 1f;
			if (_sampleInInterval < EdgeSamples)
			{
				envelope = _sampleInInterval / (float)EdgeSamples;
			}
			else if (_sampleInInterval >= PulseDurationSamples - EdgeSamples)
			{
				envelope =
					(PulseDurationSamples - _sampleInInterval - 1) / (float)EdgeSamples;
			}

			sample = MathF.Sin(_phase * MathF.Tau) * Math.Clamp(envelope, 0f, 1f) * _gain;
			float frequency = Math.Clamp(Frequency * pitchRatio, 120f, 6_000f);
			_phase = WrapPhase(
				_phase + frequency / SpatialAudioTransformCalculator.SampleRate);
		}

		_sampleInInterval++;
		if (_sampleInInterval >= PulseIntervalSamples)
		{
			_sampleInInterval = 0;
			_phase = 0f;
		}
		return sample;
	}

	public void Reset()
	{
		_gain = 0f;
		_phase = 0f;
		_sampleInInterval = 0;
	}

	private static float WrapPhase(float phase)
	{
		return phase - MathF.Floor(phase);
	}
}
