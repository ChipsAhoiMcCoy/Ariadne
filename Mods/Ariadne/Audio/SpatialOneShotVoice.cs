#nullable enable

using System;
using Microsoft.Xna.Framework;

namespace Ariadne.Audio;

/// <summary>
/// A cue that sounds once and then retires itself from the bus.
///
/// One-shots used to be rendered whole at the moment they fired, packed into a fresh
/// <see cref="Microsoft.Xna.Framework.Audio.SoundEffect"/> and played. That put the
/// entire render on the game thread at trigger time, allocated four objects per
/// press, and froze the cue's position into the samples, so a cue could not follow a
/// listener who moved while it was still sounding. Rendered a block at a time
/// alongside everything else, it costs no allocation, and its position is a target
/// like any other emitter's.
/// </summary>
internal sealed class SpatialOneShotVoice : IAudioBusSource, ISpatialMonoSource
{
	/// <summary>
	/// How long a cue is faded over when something stops it before it has finished.
	/// Being replaced while still sounding is the normal case rather than the odd
	/// one — a cursor moving to the next tile, a boss part opening again, a listener
	/// pressing the sound guide twice — and dropping a cue mid-waveform is a step
	/// the ear hears as a click. Five milliseconds is under a third of a frame, so
	/// the replacement is not audibly delayed by waiting for it.
	/// </summary>
	private const float StopFadeSeconds = 0.005f;

	private static readonly int StopFadeFrames = Math.Max(
		1,
		(int)MathF.Ceiling(StopFadeSeconds * SpatialAudioTransformCalculator.SampleRate));

	/// <summary>
	/// What the voice is given past the end of the fade so the interaural delay can
	/// empty. The far ear is still holding samples from before the fade began, and
	/// retiring the moment the source goes quiet would cut them.
	/// </summary>
	private static readonly int StopTailFrames =
		(int)MathF.Ceiling(SpatialAudioTransformCalculator.SampleRate / 1_000f) + 8;

	private readonly ISpatialMonoSource _source;
	private readonly SpatialAudioEmitter _emitter = new(gainAttackSeconds: 0.001f);
	private SpatialAudioSettings _settings;
	private float _volume;
	private int _framesRemaining;
	private int _fadeFramesRemaining;
	private bool _isStopping;

	internal SpatialOneShotVoice(
		ISpatialMonoSource source,
		int frameCount,
		Vector2 normalizedPosition,
		in SpatialAudioSettings settings,
		float volume)
	{
		_source = source;
		_settings = settings;
		_volume = Math.Clamp(volume, 0f, 1f);
		_framesRemaining = Math.Max(1, frameCount);
		_emitter.SetTargetImmediately(
			new(normalizedPosition.X, normalizedPosition.Y, _volume));
	}

	/// <summary>
	/// Whether the voice has stopped producing audio. A stopped cue is not finished
	/// until its fade has been rendered, so an owner may drop its own reference at
	/// once but must leave the voice on the bus to retire itself.
	/// </summary>
	internal bool IsFinished => _framesRemaining <= 0;

	/// <summary>
	/// Moves the cue and re-levels it. Both ride the emitter's own smoothing, so a
	/// slider moved mid-cue does not step. Ignored once the cue is stopping, where
	/// the only thing left to do is get quietly out of the way.
	/// </summary>
	internal void Update(
		Vector2 normalizedPosition,
		in SpatialAudioSettings settings,
		float volume)
	{
		if (IsFinished || _isStopping)
		{
			return;
		}

		_settings = settings;
		_volume = Math.Clamp(volume, 0f, 1f);
		_emitter.SetTarget(new(normalizedPosition.X, normalizedPosition.Y, _volume));
	}

	/// <summary>
	/// Ends the cue early, over the fade rather than at once. The voice goes on
	/// rendering until it is silent and then retires itself, so a caller must leave
	/// it on the bus; removing it here is the step the fade exists to avoid.
	/// </summary>
	internal void Stop()
	{
		if (_isStopping)
		{
			return;
		}

		_isStopping = true;
		_fadeFramesRemaining = StopFadeFrames;
		_framesRemaining = Math.Min(_framesRemaining, StopFadeFrames + StopTailFrames);
	}

	public bool Render(Span<float> left, Span<float> right)
	{
		if (IsFinished)
		{
			return false;
		}

		// The whole block is rendered even when the cue ends inside it, because the
		// source answers with silence past its end and the interaural delay still has
		// a tail to flush. The frame budget below is what decides when to retire.
		_emitter.Render(this, _settings, left, right);
		_framesRemaining -= left.Length;
		return _framesRemaining > 0;
	}

	/// <summary>
	/// The cue's own source, with the stop fade over it. Applied here rather than to
	/// the rendered block so the fade reaches the delay line as well, and the far ear
	/// fades with the near one instead of a millisecond behind it.
	/// </summary>
	float ISpatialMonoSource.ReadSample(float pitchRatio)
	{
		float sample = _source.ReadSample(pitchRatio);
		if (!_isStopping)
		{
			return sample;
		}
		if (_fadeFramesRemaining <= 0)
		{
			return 0f;
		}

		_fadeFramesRemaining--;
		return sample * (_fadeFramesRemaining / (float)StopFadeFrames);
	}

	void ISpatialMonoSource.Reset()
	{
		_source.Reset();
	}
}
