#nullable enable

using System;

namespace Ariadne.Audio;

/// <summary>
/// Reads an authored or decoded buffer back at a playback ratio, which is how every
/// pre-rendered cue in the mod is pitched. The read is interpolated rather than
/// nearest-neighbour because the ratio is almost never a whole number.
/// </summary>
internal sealed class PcmPlaybackVoice : ISpatialMonoSource
{
	/// <summary>
	/// A cue cut short at a buffer limit is faded rather than stopped, so the
	/// truncation is not itself a click.
	/// </summary>
	private const float TruncationFadeSeconds = 0.006f;

	private readonly float[] _samples;
	private readonly int _frameLimit;
	private readonly float _nativePitchRatio;
	private readonly bool _isTruncated;
	private double _position;

	internal PcmPlaybackVoice(float[] samples, int frameLimit, float nativePitchRatio)
	{
		_samples = samples;
		_frameLimit = Math.Clamp(frameLimit, 1, samples.Length);
		_nativePitchRatio = nativePitchRatio;
		_isTruncated = _frameLimit < samples.Length;
	}

	/// <summary>
	/// How many output frames this voice will produce at the given ratio, which is
	/// what a one-shot needs to know to retire itself.
	/// </summary>
	internal static int FrameCountFor(int sourceFrames, float playbackRatio)
	{
		return Math.Max(1, (int)MathF.Ceiling(sourceFrames / MathF.Max(0.05f, playbackRatio)));
	}

	public float ReadSample(float pitchRatio)
	{
		if (_position >= _frameLimit)
		{
			return 0f;
		}

		int lower = (int)_position;
		float fraction = (float)(_position - lower);
		float sample = AudioInterpolation.Hermite(
			SampleAt(lower - 1),
			SampleAt(lower),
			SampleAt(lower + 1),
			SampleAt(lower + 2),
			fraction);
		if (_isTruncated)
		{
			float fadeFrames = TruncationFadeSeconds * SpatialAudioTransformCalculator.SampleRate;
			sample *= Math.Clamp((_frameLimit - (float)_position) / fadeFrames, 0f, 1f);
		}

		_position += Math.Max(0.05f, pitchRatio * _nativePitchRatio);
		return sample;
	}

	public void Reset()
	{
		_position = 0d;
	}

	private float SampleAt(int frame)
	{
		return _samples[Math.Clamp(frame, 0, _frameLimit - 1)];
	}
}

/// <summary>
/// A cue that belongs to the listener rather than to a place: footsteps, blocked
/// movement, the heartbeat. It reaches both channels whole, which is why these are
/// levelled against the plain reference loudness while anything through the
/// spatializer is levelled against the louder one that its pan gives back.
///
/// Playing them on the bus rather than on their own voices does not move them in the
/// field; it puts them under the same limiter as everything else, so a step landing
/// on top of a full terrain bed is a sum the mod can see.
/// </summary>
internal sealed class MonoOneShotVoice : IAudioBusSource
{
	private readonly PcmPlaybackVoice _voice;
	private readonly float _volume;
	private int _framesRemaining;
	private bool _stopped;

	internal MonoOneShotVoice(float[] samples, float playbackRatio, float volume)
	{
		_voice = new(samples, samples.Length, playbackRatio);
		_volume = Math.Clamp(volume, 0f, 1f);
		_framesRemaining = PcmPlaybackVoice.FrameCountFor(samples.Length, playbackRatio);
	}

	internal bool IsFinished => _stopped || _framesRemaining <= 0;

	internal void Stop()
	{
		_stopped = true;
	}

	public bool Render(Span<float> left, Span<float> right)
	{
		if (IsFinished)
		{
			return false;
		}

		for (int index = 0; index < left.Length; index++)
		{
			float sample = _voice.ReadSample(1f) * _volume;
			left[index] += sample;
			right[index] += sample;
		}

		_framesRemaining -= left.Length;
		return _framesRemaining > 0;
	}
}
