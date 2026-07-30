#nullable enable

using System;

namespace Ariadne.Audio;

/// <summary>
/// Everything that happens to the summed mix between the last source writing into it
/// and the buffer being handed to the device: the sound slider, the listening gate's
/// ramp, and the ceiling.
///
/// It is separate from <see cref="AriadneAudioBus"/> because it is the part with no
/// device in it. The bus owns a voice, a queue and a failure path that only exist
/// against a real endpoint; this owns arithmetic, so it can be driven a block at a
/// time by anything that can supply samples, and measured off-line.
/// </summary>
internal sealed class AudioBusMixChain
{
	/// <summary>
	/// Small enough that the bus's queue is a latency in the tens of milliseconds
	/// rather than the seventy the separate streams carried, and large enough that a
	/// block is worth the per-block work in the limiter.
	/// </summary>
	internal const int FramesPerBuffer = 256;

	/// <summary>
	/// How quickly the mix fades when the listening gate closes. A hard cut on a
	/// running terrain bed is a click; this is short enough to still read as immediate.
	/// </summary>
	private const float GateFadeSeconds = 0.008f;

	private readonly MasterLimiter _limiter;
	private readonly float _gateFadeStep;
	private float _gateGain;

	internal AudioBusMixChain(int frameCount)
	{
		_limiter = new(frameCount);
		_gateFadeStep = 1f / MathF.Max(1f, GateFadeSeconds * SpatialAudioTransformCalculator.SampleRate);
	}

	/// <summary>Where the gate's ramp currently sits, for diagnostics.</summary>
	internal float GateGain => _gateGain;

	/// <summary>
	/// The gain the limiter ended the last block on. One is untouched; anything less is
	/// how far the mix had to be held down.
	/// </summary>
	internal float LimiterGain => _limiter.CurrentGain;

	/// <summary>
	/// Carries one block from the summed mix to what the device is given. The mix is
	/// delayed by a block on the way through, which is the limiter's look-ahead.
	/// </summary>
	internal void Process(Span<float> left, Span<float> right, float masterGain, bool canListen)
	{
		ApplyMasterGain(left, right, masterGain, canListen);
		_limiter.Process(left, right);
	}

	/// <summary>
	/// Applies Terraria's sound slider and the listening gate once, for the whole mix.
	/// The gate is a ramp rather than a switch because a stream can be mid-cycle when
	/// a menu opens.
	/// </summary>
	private void ApplyMasterGain(Span<float> left, Span<float> right, float masterGain, bool canListen)
	{
		float gateTarget = canListen ? 1f : 0f;
		for (int frame = 0; frame < left.Length; frame++)
		{
			_gateGain = _gateGain < gateTarget
				? MathF.Min(gateTarget, _gateGain + _gateFadeStep)
				: MathF.Max(gateTarget, _gateGain - _gateFadeStep);
			float gain = masterGain * _gateGain;
			left[frame] *= gain;
			right[frame] *= gain;
		}
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
		private float _pendingRequiredGain = 1f;

		internal MasterLimiter(int frameCount)
		{
			_frameCount = frameCount;
			_pendingLeft = new float[frameCount];
			_pendingRight = new float[frameCount];
			float blockSeconds = frameCount / (float)SpatialAudioTransformCalculator.SampleRate;
			_releaseCoefficient = 1f - MathF.Exp(-blockSeconds / ReleaseSeconds);
		}

		internal float CurrentGain => _currentGain;

		internal void Process(Span<float> left, Span<float> right)
		{
			float peak = 0f;
			for (int frame = 0; frame < _frameCount; frame++)
			{
				peak = MathF.Max(peak, MathF.Max(MathF.Abs(left[frame]), MathF.Abs(right[frame])));
			}

			// The block about to be output is the one measured on the previous call, so
			// the release must not lift the gain above what that block itself needed.
			// Without this the recovery from a loud cue began during the loud cue, and
			// its own peak came back out just over the ceiling.
			float required = peak > Ceiling ? Ceiling / peak : 1f;
			float released = _currentGain + (1f - _currentGain) * _releaseCoefficient;
			float nextGain = MathF.Min(MathF.Min(required, released), _pendingRequiredGain);
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
			_pendingRequiredGain = required;
		}
	}
}
