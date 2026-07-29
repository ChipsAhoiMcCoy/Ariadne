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
internal sealed class SpatialOneShotVoice : IAudioBusSource
{
	private readonly ISpatialMonoSource _source;
	private readonly SpatialAudioEmitter _emitter = new(gainAttackSeconds: 0.001f);
	private SpatialAudioSettings _settings;
	private float _volume;
	private int _framesRemaining;
	private bool _stopped;

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

	internal bool IsFinished => _stopped || _framesRemaining <= 0;

	/// <summary>
	/// Moves the cue and re-levels it. Both ride the emitter's own smoothing, so a
	/// slider moved mid-cue does not step.
	/// </summary>
	internal void Update(
		Vector2 normalizedPosition,
		in SpatialAudioSettings settings,
		float volume)
	{
		if (IsFinished)
		{
			return;
		}

		_settings = settings;
		_volume = Math.Clamp(volume, 0f, 1f);
		_emitter.SetTarget(new(normalizedPosition.X, normalizedPosition.Y, _volume));
	}

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

		// The whole block is rendered even when the cue ends inside it, because the
		// source answers with silence past its end and the interaural delay still has
		// a tail to flush. The frame budget below is what decides when to retire.
		_emitter.Render(_source, _settings, left, right);
		_framesRemaining -= left.Length;
		return _framesRemaining > 0;
	}
}
