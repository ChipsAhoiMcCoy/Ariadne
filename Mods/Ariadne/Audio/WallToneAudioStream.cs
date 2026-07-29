#nullable enable

using System;
using Terraria;
using Terraria.ModLoader;
using Ariadne.Configs;
using Ariadne.Ingame.WallTones;

namespace Ariadne.Audio;

internal sealed class WallToneAudioStream : IAudioBusSource, IDisposable
{
	/// <summary>
	/// Terrain answers from more than one direction at once, and a corridor commonly
	/// puts a side and the floor in the same ear. Each voice therefore sits this far
	/// under the shared reference so the bed as a whole arrives on it.
	/// </summary>
	private const float BedVoiceOffsetDecibels = 3f;

	private static readonly int CalibrationFrames = SpatialAudioTransformCalculator.SampleRate;

	// The sides are the neutral reference. The ceiling sits higher and narrower so it
	// reads thin and focused, the floor lower and broader so it reads as a rumble.
	// Timbre, the mixer's vertical pitch law and pan then all name the same surface
	// instead of the distinction resting on any one of them.
	private static readonly WallToneVoiceDesign SideDesign = new(320f, 2_400f, 1.4f);
	private static readonly WallToneVoiceDesign CeilingDesign = new(480f, 3_200f, 2.2f);
	private static readonly WallToneVoiceDesign FloorDesign = new(180f, 1_200f, 0.9f);

	// Band and resonance decide how loud a voice sounds at a given gain, so one
	// shared gain left the ceiling voice far above the floor voice. Each design is
	// measured against the reference instead of being trimmed by ear.
	private static readonly float SideVoiceGain = CalibrateVoiceGain(SideDesign);
	private static readonly float CeilingVoiceGain = CalibrateVoiceGain(CeilingDesign);
	private static readonly float FloorVoiceGain = CalibrateVoiceGain(FloorDesign);

	private readonly AriadneAudioBus _bus;
	private readonly WallToneVoice _leftVoice = new(0x93A4_52E1u, SideDesign);
	private readonly WallToneVoice _rightVoice = new(0xD17B_8305u, SideDesign);
	private readonly WallToneVoice _ceilingVoice = new(0x6C8E_9CF3u, CeilingDesign);
	private readonly WallToneVoice _floorVoice = new(0x2B57_41ADu, FloorDesign);
	private readonly SpatialAudioEmitter _leftEmitter = new();
	private readonly SpatialAudioEmitter _rightEmitter = new();
	private readonly SpatialAudioEmitter _ceilingEmitter = new();
	private readonly SpatialAudioEmitter _floorEmitter = new();
	private SpatialAudioSettings _settings;
	private bool _isReset = true;
	private bool _disposed;

	private WallToneAudioStream(AriadneAudioBus bus)
	{
		_bus = bus;
		bus.Add(this);
	}

	internal static WallToneAudioStream? TryCreate(Mod owner)
	{
		if (Main.dedServ)
		{
			return null;
		}

		AriadneAudioBus? bus = AudioBusSystem.Bus;
		if (bus is null)
		{
			owner.Logger.Warn("Wall tones are unavailable because the audio bus could not be created.");
			return null;
		}

		return new(bus);
	}

	internal void UpdateTargets(WallToneSnapshot snapshot, AriadneClientConfig config)
	{
		if (_disposed)
		{
			return;
		}

		_settings = config.ToSpatialAudioSettings();
		// Terraria's sound slider is applied once, by the bus, for the whole mix.
		float masterGain = Math.Clamp(config.WallToneVolumePercent / 100f, 0f, 1f);
		SetVoiceTarget(_leftVoice, _leftEmitter, snapshot.Left, masterGain * SideVoiceGain);
		SetVoiceTarget(_rightVoice, _rightEmitter, snapshot.Right, masterGain * SideVoiceGain);
		SetVoiceTarget(_ceilingVoice, _ceilingEmitter, snapshot.Ceiling, masterGain * CeilingVoiceGain);
		SetVoiceTarget(_floorVoice, _floorEmitter, snapshot.Floor, masterGain * FloorVoiceGain);
		_isReset = false;
	}

	public bool Render(Span<float> left, Span<float> right)
	{
		if (_disposed)
		{
			return false;
		}

		_leftEmitter.Render(_leftVoice, _settings, left, right);
		_rightEmitter.Render(_rightVoice, _settings, left, right);
		_ceilingEmitter.Render(_ceilingVoice, _settings, left, right);
		_floorEmitter.Render(_floorVoice, _settings, left, right);
		return true;
	}

	internal void StopAndReset()
	{
		if (_disposed || _isReset)
		{
			return;
		}
		ResetSignalState();
	}

	internal void ResetForDiscontinuity()
	{
		if (_disposed)
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

		_bus.Remove(this);
		_disposed = true;
		ResetSignalState();
	}

	/// <summary>
	/// Measures one design at its nearest surface and returns the gain that puts it
	/// on the bed's share of the reference loudness.
	/// </summary>
	private static float CalibrateVoiceGain(WallToneVoiceDesign design)
	{
		WallToneVoice voice = new(0x51F0_2C7Bu, design);
		voice.SetTarget(voice.FrequencyForProximity(1f), gain: 1f);
		float[] samples = new float[CalibrationFrames];
		for (int index = 0; index < samples.Length; index++)
		{
			samples[index] = voice.ReadSample(pitchRatio: 1f);
		}

		return AuthoredAudioLevels.LoudnessTrim(
			samples,
			AuthoredAudioLevels.SpatialVoiceReferenceLoudness - BedVoiceOffsetDecibels);
	}

	private static void SetVoiceTarget(
		WallToneVoice voice,
		SpatialAudioEmitter emitter,
		WallToneRegionSnapshot snapshot,
		float masterGain)
	{
		float proximity = snapshot.Proximity;
		float distanceGain = SpatialAudioDistanceGain.FromProximity(proximity);
		voice.SetTarget(voice.FrequencyForProximity(proximity), masterGain);
		emitter.SetTarget(new(
			snapshot.NormalizedPosition.X,
			snapshot.NormalizedPosition.Y,
			snapshot.HasHit ? distanceGain : 0f));
	}

	private void ResetSignalState()
	{
		_leftVoice.Reset();
		_rightVoice.Reset();
		_ceilingVoice.Reset();
		_floorVoice.Reset();
		_leftEmitter.Reset();
		_rightEmitter.Reset();
		_ceilingEmitter.Reset();
		_floorEmitter.Reset();
		_isReset = true;
	}
}

/// <summary>
/// The band a terrain voice sweeps between its farthest and nearest surface, and
/// the filter resonance that gives it its character.
/// </summary>
internal readonly record struct WallToneVoiceDesign(
	float MinimumFrequency,
	float MaximumFrequency,
	float FilterQ);

internal sealed class WallToneVoice : ISpatialMonoSource
{
	private static readonly float FrequencySmoothing = SmoothingCoefficient(0.035f);
	private static readonly float GainSmoothing = SmoothingCoefficient(0.020f);

	private readonly uint _initialNoiseState;
	private readonly WallToneVoiceDesign _design;
	private uint _noiseState;
	private float _targetFrequency;
	private float _targetGain;
	private float _currentFrequency;
	private float _currentGain;
	private float _integratorOne;
	private float _integratorTwo;

	internal WallToneVoice(uint seed, WallToneVoiceDesign design)
	{
		_initialNoiseState = seed;
		_noiseState = _initialNoiseState;
		_design = design;
		_targetFrequency = design.MinimumFrequency;
		_currentFrequency = design.MinimumFrequency;
	}

	internal float FrequencyForProximity(float proximity)
	{
		return _design.MinimumFrequency * MathF.Pow(
			_design.MaximumFrequency / _design.MinimumFrequency,
			Math.Clamp(proximity, 0f, 1f));
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
		float bandPassedNoise = FilterBandPass(NextWhiteNoise(ref _noiseState), centerFrequency, _design.FilterQ);
		return bandPassedNoise * _currentGain;
	}

	public void Reset()
	{
		_noiseState = _initialNoiseState;
		_targetFrequency = _design.MinimumFrequency;
		_targetGain = 0f;
		_currentFrequency = _design.MinimumFrequency;
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
