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
