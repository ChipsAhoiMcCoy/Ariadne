#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.Xna.Framework.Audio;
using Terraria;
using Terraria.Audio;
using Terraria.ModLoader;

namespace Ariadne.Audio;

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
	private const int FramesPerBuffer = AudioBusMixChain.FramesPerBuffer;

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
	/// How long a gap between pumps is taken to mean the pump was away rather than the
	/// machine having stalled inside one frame.
	///
	/// The question this answers is whether an empty queue is the mod's fault. It used
	/// to be asked of <see cref="Main.GameUpdateCount"/>, which only advances inside
	/// Terraria's world pass, so on the title screen and while paused the counter never
	/// moved and no pump ever looked consecutive: underruns went uncounted and the
	/// queue never deepened, in exactly the place the sound guide is used. Wall time
	/// answers the same question everywhere. Half a second is far longer than any frame
	/// worth deepening the queue over, and far shorter than a world load or the time
	/// spent with the window unfocused, which are the gaps that must not be blamed on
	/// the machine.
	/// </summary>
	private const long MaximumPumpGapMilliseconds = 500L;

	private readonly Mod _owner;
	private readonly List<IAudioBusSource> _sources = [];
	private readonly float[] _leftMix = new float[FramesPerBuffer];
	private readonly float[] _rightMix = new float[FramesPerBuffer];
	private readonly float[] _interleaved = new float[FramesPerBuffer * 2];
	private readonly AudioBusMixChain _mixChain = new(FramesPerBuffer);
	private readonly Stopwatch _pumpClock = Stopwatch.StartNew();
	private readonly bool _submitsFloat;
	private readonly byte[]? _pcmBuffer;
	private DynamicSoundEffectInstance? _stream;
	private int _targetQueuedBuffers;
	private int _maximumQueuedBuffers;
	private int _underrunCount;
	private int _loggedUnderrunCount;
	private long _lastPumpMilliseconds;
	private bool _hasPumped;
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
	/// Whether a cue is being deliberately auditioned, which opens the listening gate
	/// on its own.
	///
	/// <see cref="GameplayAudioGate"/> answers whether unobstructed gameplay is in
	/// progress, and a menu never is; the sound guide's whole purpose is to be heard
	/// from a menu. Only the bus reads this, so the gate every cue owner consults is
	/// unchanged and nothing in the world resumes behind the guide.
	/// </summary>
	internal bool IsAuditioning { get; set; }

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
			// An empty queue after a long gap is the pump having been away rather than
			// the machine failing to keep up. Only a pump that followed close behind the
			// last one can report a real underrun.
			long nowMilliseconds = _pumpClock.ElapsedMilliseconds;
			bool followedLastPump = _hasPumped &&
				nowMilliseconds - _lastPumpMilliseconds <= MaximumPumpGapMilliseconds;
			_lastPumpMilliseconds = nowMilliseconds;
			_hasPumped = true;

			if (_isRunning && followedLastPump && _stream.PendingBufferCount == 0)
			{
				NoteUnderrun();
			}

			float masterGain = Math.Clamp(Main.soundVolume, 0f, 1f);
			bool canListen = IsAuditioning || GameplayAudioGate.CanListen();
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
		if (!Program.IsMainThread)
		{
			try
			{
				// tModLoader unloads mods on its loader worker. FNA requires every
				// DynamicSoundEffectInstance operation, including disposal, on the
				// game thread. This mirrors tModLoader's own ActiveSound and
				// MusicLoader cleanup paths and waits until the queued action finishes.
				Main.RunOnMainThread(DisposeOnMainThread).GetAwaiter().GetResult();
			}
			catch (Exception exception)
			{
				// An unload must remain able to complete even if the game is already
				// too far into shutdown to service its main-thread action queue.
				_owner.Logger.Warn(
					$"Ariadne's audio bus could not finish main-thread cleanup: " +
					$"{exception.GetBaseException().Message}");
				AbandonManagedReferences();
			}
			return;
		}

		DisposeOnMainThread();
	}

	private void DisposeOnMainThread()
	{
		if (_disposed)
		{
			return;
		}

		try
		{
			_stream?.Stop(true);
		}
		catch (Exception exception)
		{
			_owner.Logger.Warn(
				$"Ariadne's audio bus could not stop during cleanup: " +
				$"{exception.GetBaseException().Message}");
		}

		try
		{
			_stream?.Dispose();
		}
		catch (Exception exception)
		{
			_owner.Logger.Warn(
				$"Ariadne's audio bus could not dispose during cleanup: " +
				$"{exception.GetBaseException().Message}");
		}

		AbandonManagedReferences();
	}

	private void AbandonManagedReferences()
	{
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

		_mixChain.Process(_leftMix, _rightMix, masterGain, canListen);
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
}
