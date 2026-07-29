#nullable enable

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework.Audio;
using Terraria;
using Terraria.Audio;
using Terraria.ModLoader;

namespace Ariadne.Audio;

/// <summary>
/// Anything that writes into the shared mix. Sources are rendered additively and in
/// registration order, on the game thread, from <see cref="AriadneAudioBus.Pump"/>.
/// </summary>
internal interface IAudioBusSource
{
	/// <summary>
	/// Adds this source's contribution to the mix. Returning false retires the source,
	/// which is how a one-shot removes itself once it has finished sounding.
	/// </summary>
	bool Render(Span<float> left, Span<float> right);
}

/// <summary>
/// The single output every Ariadne cue reaches the device through.
///
/// Each continuous cue used to own a <see cref="DynamicSoundEffectInstance"/> of its
/// own, and every one-shot built a throwaway <see cref="SoundEffect"/> at the moment
/// it fired. Three independent voices cannot see each other, so each clipped on its
/// own with a hyperbolic tangent while the beacon clipped not at all, and nothing
/// could hold the sum of them under a ceiling. One bus makes the sum measurable, and
/// makes the master volume and the listening gate one decision instead of five.
/// </summary>
internal sealed class AriadneAudioBus : IDisposable
{
	/// <summary>
	/// Small enough that the queue below is a latency in the tens of milliseconds
	/// rather than the seventy the separate streams carried, and large enough that a
	/// block is worth the per-block work in the limiter.
	/// </summary>
	private const int FramesPerBuffer = 256;

	/// <summary>
	/// How much audio is kept queued ahead of the device. Buffers are submitted from
	/// the game thread, so the queue has to cover a whole frame plus jitter or the
	/// voice runs dry between pumps; FNA also only retires spent buffers in its own
	/// end-of-frame pass, so the count read here trails reality by up to one frame.
	/// Forty milliseconds covers a 60 Hz frame twice over.
	/// </summary>
	private const float TargetQueueMilliseconds = 40f;

	/// <summary>
	/// The ceiling underruns may push the queue to. Past this the latency costs more
	/// than the dropouts do, and the real answer is that the machine cannot keep up.
	/// </summary>
	private const float MaximumQueueMilliseconds = 120f;

	private const float QueueGrowthMilliseconds = 15f;

	/// <summary>
	/// How quickly the mix fades when the listening gate closes. A hard cut on a
	/// running terrain bed is a click; this is short enough to still read as immediate.
	/// </summary>
	private const float GateFadeSeconds = 0.008f;

	private readonly Mod _owner;
	private readonly List<IAudioBusSource> _sources = [];
	private readonly float[] _leftMix = new float[FramesPerBuffer];
	private readonly float[] _rightMix = new float[FramesPerBuffer];
	private readonly float[] _interleaved = new float[FramesPerBuffer * 2];
	private readonly MasterLimiter _limiter = new(FramesPerBuffer);
	private readonly float _gateFadeStep;
	private readonly bool _submitsFloat;
	private readonly byte[]? _pcmBuffer;
	private DynamicSoundEffectInstance? _stream;
	private int _targetQueuedBuffers;
	private int _maximumQueuedBuffers;
	private int _underrunCount;
	private int _loggedUnderrunCount;
	private uint _lastPumpUpdateCount;
	private bool _hasPumped;
	private float _gateGain;
	private bool _isRunning;
	private bool _failureLogged;
	private bool _disposed;

	private AriadneAudioBus(Mod owner, DynamicSoundEffectInstance stream, bool submitsFloat)
	{
		_owner = owner;
		_stream = stream;
		_submitsFloat = submitsFloat;
		_pcmBuffer = submitsFloat ? null : new byte[FramesPerBuffer * 2 * sizeof(short)];
		_targetQueuedBuffers = BuffersForMilliseconds(TargetQueueMilliseconds);
		_maximumQueuedBuffers = BuffersForMilliseconds(MaximumQueueMilliseconds);
		_gateFadeStep = 1f / MathF.Max(1f, GateFadeSeconds * SpatialAudioTransformCalculator.SampleRate);
	}

	internal static AriadneAudioBus? TryCreate(Mod owner)
	{
		if (Main.dedServ)
		{
			return null;
		}
		if (!SoundEngine.IsAudioSupported)
		{
			owner.Logger.Warn("Ariadne's audio bus is unavailable because this client does not support audio.");
			return null;
		}

		try
		{
			DynamicSoundEffectInstance stream = new(
				SpatialAudioTransformCalculator.SampleRate,
				AudioChannels.Stereo);
			bool submitsFloat = TrySubmitFloatProbe(stream);
			owner.Logger.Info(
				$"Ariadne audio bus running at {SpatialAudioTransformCalculator.SampleRate} Hz " +
				$"({AudioFormat.Diagnostic}), " +
				$"{(submitsFloat ? "32-bit float" : "16-bit")} output.");
			return new(owner, stream, submitsFloat);
		}
		catch (Exception exception)
		{
			owner.Logger.Warn(
				$"Ariadne's audio bus could not be initialized and every cue will remain silent: " +
				$"{exception.GetBaseException().Message}");
			return null;
		}
	}

	internal bool IsAvailable => !_disposed && _stream is not null;

	/// <summary>
	/// Submits one silent block as float, before any real audio, to find out whether
	/// this build of FNA carries the float path. It has to happen first: the extension
	/// rewrites the instance's wave format on its way in, so discovering the failure
	/// later would leave the voice expecting samples in a width it was not given.
	/// The block is not wasted, since the queue wants priming anyway.
	/// </summary>
	private static bool TrySubmitFloatProbe(DynamicSoundEffectInstance stream)
	{
		try
		{
			stream.SubmitFloatBufferEXT(new float[FramesPerBuffer * 2]);
			return true;
		}
		catch
		{
			return false;
		}
	}

	internal void Add(IAudioBusSource source)
	{
		if (_disposed)
		{
			return;
		}
		_sources.Add(source);
	}

	internal void Remove(IAudioBusSource source)
	{
		_sources.Remove(source);
	}

	/// <summary>
	/// Renders and queues audio up to the target depth. Called once per frame, after
	/// every system has set its targets for this tick.
	/// </summary>
	internal void Pump()
	{
		if (_disposed || _stream is null)
		{
			return;
		}

		try
		{
			// The world pass this runs from does not tick in menus, so an empty queue
			// after a gap is the pump having been away rather than the machine failing
			// to keep up. Only a gap-free frame can report a real underrun.
			uint updateCount = Main.GameUpdateCount;
			bool pumpedLastFrame = _hasPumped && updateCount == _lastPumpUpdateCount + 1;
			_lastPumpUpdateCount = updateCount;
			_hasPumped = true;

			if (_isRunning && pumpedLastFrame && _stream.PendingBufferCount == 0)
			{
				NoteUnderrun();
			}

			float masterGain = Math.Clamp(Main.soundVolume, 0f, 1f);
			bool canListen = GameplayAudioGate.CanListen();
			while (_stream.PendingBufferCount < _targetQueuedBuffers)
			{
				GenerateBuffer(masterGain, canListen);
				Submit(_stream);
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
			// Disposal must stay safe if the audio device has already disappeared.
		}
		_stream?.Dispose();
		_stream = null;
		_sources.Clear();
		_isRunning = false;
		_disposed = true;
	}

	private static int BuffersForMilliseconds(float milliseconds)
	{
		float bufferMilliseconds =
			FramesPerBuffer * 1_000f / SpatialAudioTransformCalculator.SampleRate;
		return Math.Max(2, (int)MathF.Ceiling(milliseconds / bufferMilliseconds));
	}

	/// <summary>
	/// Deepens the queue after the voice has run dry. The count read at pump time
	/// already trails reality, so observing zero means the device certainly starved,
	/// not that it might have.
	/// </summary>
	private void NoteUnderrun()
	{
		_underrunCount++;
		if (_targetQueuedBuffers < _maximumQueuedBuffers)
		{
			_targetQueuedBuffers = Math.Min(
				_maximumQueuedBuffers,
				_targetQueuedBuffers + BuffersForMilliseconds(QueueGrowthMilliseconds));
		}

		// Logged on a widening interval so a machine that underruns constantly does
		// not fill the log, while the first few still show up during a play test.
		if (_underrunCount >= _loggedUnderrunCount * 2 + 1)
		{
			_loggedUnderrunCount = _underrunCount;
			float queuedMilliseconds =
				_targetQueuedBuffers * FramesPerBuffer * 1_000f / SpatialAudioTransformCalculator.SampleRate;
			_owner.Logger.Info(
				$"Ariadne audio bus underran {_underrunCount} time(s); " +
				$"queue now {queuedMilliseconds:F0} ms.");
		}
	}

	private void GenerateBuffer(float masterGain, bool canListen)
	{
		Array.Clear(_leftMix);
		Array.Clear(_rightMix);
		for (int index = _sources.Count - 1; index >= 0; index--)
		{
			if (!_sources[index].Render(_leftMix, _rightMix))
			{
				_sources.RemoveAt(index);
			}
		}

		ApplyMasterGain(masterGain, canListen);
		_limiter.Process(_leftMix, _rightMix);
		for (int frame = 0; frame < FramesPerBuffer; frame++)
		{
			_interleaved[frame * 2] = _leftMix[frame];
			_interleaved[frame * 2 + 1] = _rightMix[frame];
		}
	}

	private void Submit(DynamicSoundEffectInstance stream)
	{
		if (_submitsFloat || _pcmBuffer is null)
		{
			stream.SubmitFloatBufferEXT(_interleaved);
			return;
		}

		for (int index = 0; index < _interleaved.Length; index++)
		{
			short encoded = (short)MathF.Round(
				Math.Clamp(_interleaved[index], -1f, 1f) * short.MaxValue);
			_pcmBuffer[index * 2] = (byte)encoded;
			_pcmBuffer[index * 2 + 1] = (byte)(encoded >> 8);
		}
		stream.SubmitBuffer(_pcmBuffer);
	}

	/// <summary>
	/// Applies Terraria's sound slider and the listening gate once, for the whole mix.
	/// The gate is a ramp rather than a switch because a stream can be mid-cycle when
	/// a menu opens.
	/// </summary>
	private void ApplyMasterGain(float masterGain, bool canListen)
	{
		float gateTarget = canListen ? 1f : 0f;
		for (int frame = 0; frame < FramesPerBuffer; frame++)
		{
			_gateGain = _gateGain < gateTarget
				? MathF.Min(gateTarget, _gateGain + _gateFadeStep)
				: MathF.Max(gateTarget, _gateGain - _gateFadeStep);
			float gain = masterGain * _gateGain;
			_leftMix[frame] *= gain;
			_rightMix[frame] *= gain;
		}
	}

	private void DisableAfterFailure(Exception exception)
	{
		if (!_failureLogged)
		{
			_owner.Logger.Warn(
				$"Ariadne's audio bus failed and has been disabled for this session: " +
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
	}

	/// <summary>
	/// Holds the summed mix under a ceiling without the timbre change a per-sample
	/// hyperbolic tangent imposes on everything that reaches it.
	///
	/// The mix is delayed by one block, and the gain a block needs is decided from its
	/// own peak before that block is heard, so a transient is turned down ahead of
	/// itself rather than squared off. Between blocks the gain moves linearly, and it
	/// returns toward unity slowly enough that a single loud cue does not audibly duck
	/// the terrain bed underneath it.
	/// </summary>
	private sealed class MasterLimiter
	{
		private const float Ceiling = 0.97f;
		private const float ReleaseSeconds = 0.150f;

		private readonly int _frameCount;
		private readonly float[] _pendingLeft;
		private readonly float[] _pendingRight;
		private readonly float _releaseCoefficient;
		private float _currentGain = 1f;

		internal MasterLimiter(int frameCount)
		{
			_frameCount = frameCount;
			_pendingLeft = new float[frameCount];
			_pendingRight = new float[frameCount];
			float blockSeconds = frameCount / (float)SpatialAudioTransformCalculator.SampleRate;
			_releaseCoefficient = 1f - MathF.Exp(-blockSeconds / ReleaseSeconds);
		}

		internal void Process(Span<float> left, Span<float> right)
		{
			float peak = 0f;
			for (int frame = 0; frame < _frameCount; frame++)
			{
				peak = MathF.Max(peak, MathF.Max(MathF.Abs(left[frame]), MathF.Abs(right[frame])));
			}

			float required = peak > Ceiling ? Ceiling / peak : 1f;
			float released = _currentGain + (1f - _currentGain) * _releaseCoefficient;
			float nextGain = MathF.Min(required, released);
			float step = (nextGain - _currentGain) / _frameCount;

			for (int frame = 0; frame < _frameCount; frame++)
			{
				float gain = _currentGain + step * frame;
				float outputLeft = _pendingLeft[frame] * gain;
				float outputRight = _pendingRight[frame] * gain;
				_pendingLeft[frame] = left[frame];
				_pendingRight[frame] = right[frame];
				left[frame] = outputLeft;
				right[frame] = outputRight;
			}

			_currentGain = nextGain;
		}
	}
}
