#nullable enable

using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using Terraria;
using Terraria.Audio;
using Terraria.ModLoader;
using Ariadne.Configs;

namespace Ariadne.Audio;

/// <summary>
/// Plays a short authored lock-on cue through the same screen-relative ILD, ITD,
/// and vertical-pitch transform used by Ariadne's continuous spatial audio.
/// </summary>
internal sealed class CombatTargetCueSound : IDisposable
{
	private const float DurationSeconds = 0.16f;
	private const int DelayTailFrames = 64;

	private readonly Mod _owner;
	private SoundEffect? _soundEffect;
	private SoundEffectInstance? _instance;
	private bool _disabledAfterFailure;
	private bool _disposed;

	private CombatTargetCueSound(Mod owner)
	{
		_owner = owner;
	}

	internal static CombatTargetCueSound? Create(Mod owner)
	{
		if (Main.dedServ)
		{
			return null;
		}
		if (!SoundEngine.IsAudioSupported)
		{
			owner.Logger.Warn("Combat-target cues are unavailable because this client does not support audio.");
			return null;
		}

		return new(owner);
	}

	internal void Play(Vector2 worldPosition, AriadneClientConfig config)
	{
		float configuredVolume = Math.Clamp(config.HostileMobToneVolumePercent / 100f, 0f, 1f);
		float volume = configuredVolume * Math.Clamp(Main.soundVolume, 0f, 1f);
		if (_disposed ||
			_disabledAfterFailure ||
			!config.HostileMobTonesEnabled ||
			volume <= 0f ||
			!GameplayAudioGate.CanListen())
		{
			StopCurrent();
			return;
		}

		try
		{
			StopCurrent();
			Vector2 normalizedPosition = ViewportSpatialPosition.Normalize(worldPosition);
			byte[] pcm = CreatePcm(
				normalizedPosition,
				config.SpatialAudioItdEnabled,
				config.SpatialAudioItdStrengthMilliseconds);
			_soundEffect = new SoundEffect(
				pcm,
				SpatialAudioTransformCalculator.SampleRate,
				AudioChannels.Stereo);
			_instance = _soundEffect.CreateInstance();
			_instance.Volume = volume;
			_instance.Play();
		}
		catch (Exception exception)
		{
			DisableAfterFailure(exception);
		}
	}

	internal void Update(AriadneClientConfig config)
	{
		if (_disposed || _instance is null)
		{
			return;
		}

		try
		{
			float configuredVolume = Math.Clamp(config.HostileMobToneVolumePercent / 100f, 0f, 1f);
			float volume = configuredVolume * Math.Clamp(Main.soundVolume, 0f, 1f);
			if (!config.HostileMobTonesEnabled ||
				volume <= 0f ||
				!GameplayAudioGate.CanListen())
			{
				StopCurrent();
			}
			else if (_instance.State == SoundState.Stopped)
			{
				DisposeCurrent();
			}
			else
			{
				_instance.Volume = volume;
			}
		}
		catch (Exception exception)
		{
			DisableAfterFailure(exception);
		}
	}

	internal void StopAndReset()
	{
		if (_disposed)
		{
			return;
		}
		StopCurrent();
	}

	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		StopCurrent();
		_disposed = true;
	}

	private static byte[] CreatePcm(
		Vector2 normalizedPosition,
		bool itdEnabled,
		float maximumItdMilliseconds)
	{
		int cueFrames = Math.Max(
			1,
			(int)MathF.Round(SpatialAudioTransformCalculator.SampleRate * DurationSeconds));
		int totalFrames = cueFrames + DelayTailFrames;
		float[] left = new float[totalFrames];
		float[] right = new float[totalFrames];
		TargetLockCueVoice voice = new(cueFrames);
		SpatialAudioEmitter emitter = new(gainAttackSeconds: 0.001f);
		emitter.SetTargetImmediately(new(
			normalizedPosition.X,
			normalizedPosition.Y,
			DistanceGain: 1f));
		emitter.Render(
			voice,
			itdEnabled,
			maximumItdMilliseconds,
			left,
			right);

		byte[] pcm = new byte[totalFrames * 2 * sizeof(short)];
		for (int frame = 0; frame < totalFrames; frame++)
		{
			short leftSample = Encode(left[frame]);
			short rightSample = Encode(right[frame]);
			int byteIndex = frame * 4;
			pcm[byteIndex] = (byte)leftSample;
			pcm[byteIndex + 1] = (byte)(leftSample >> 8);
			pcm[byteIndex + 2] = (byte)rightSample;
			pcm[byteIndex + 3] = (byte)(rightSample >> 8);
		}
		return pcm;
	}

	private void StopCurrent()
	{
		try
		{
			_instance?.Stop(immediate: true);
		}
		catch
		{
			// Cleanup must remain safe if the audio device disappears.
		}
		DisposeCurrent();
	}

	private void DisposeCurrent()
	{
		_instance?.Dispose();
		_instance = null;
		_soundEffect?.Dispose();
		_soundEffect = null;
	}

	private void DisableAfterFailure(Exception exception)
	{
		if (!_disabledAfterFailure)
		{
			_owner.Logger.Warn(
				$"Combat-target cue playback failed and has been disabled for this session: " +
				$"{exception.GetBaseException().Message}");
		}

		_disabledAfterFailure = true;
		StopCurrent();
	}

	private static short Encode(float sample)
	{
		float normalized = Math.Clamp(
			sample * AuthoredAudioLevels.NormalizedOneShotPeak,
			-1f,
			1f);
		return (short)MathF.Round(normalized * short.MaxValue);
	}

	private sealed class TargetLockCueVoice : ISpatialMonoSource
	{
		private const float AttackSeconds = 0.004f;
		private readonly int _frameCount;
		private float _phase;
		private int _frame;

		internal TargetLockCueVoice(int frameCount)
		{
			_frameCount = Math.Max(1, frameCount);
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
				(620f + 520f * progress) * pitchRatio,
				120f,
				6_000f);
			_phase += frequency / SpatialAudioTransformCalculator.SampleRate;
			_phase -= MathF.Floor(_phase);
			_frame++;

			float angle = MathF.Tau * _phase;
			float tone = MathF.Sin(angle) + 0.22f * MathF.Sin(angle * 2f);
			return tone / 1.22f * attack * release;
		}

		public void Reset()
		{
			_phase = 0f;
			_frame = 0;
		}
	}
}
