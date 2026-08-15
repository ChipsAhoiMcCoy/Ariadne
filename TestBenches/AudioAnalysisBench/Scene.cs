#nullable enable

using Ariadne.Audio;

namespace Ariadne.TestBenches.AudioAnalysis;

/// <summary>
/// The bus without a device on the end of it.
///
/// <see cref="AriadneAudioBus"/> owns a voice, a queue and a failure path that only
/// mean anything against a real endpoint. What it does to the samples themselves is
/// this: clear, sum the sources in registration order, hand the block to the real
/// <see cref="AudioBusMixChain"/>. Nothing here decides level or shape.
/// </summary>
internal sealed class OfflineBus
{
	internal const int FramesPerBuffer = AudioBusMixChain.FramesPerBuffer;

	private readonly List<IAudioBusSource> _sources = [];
	private readonly AudioBusMixChain _mixChain = new(FramesPerBuffer);

	internal float MinimumLimiterGain { get; private set; } = 1f;

	internal int SourceCount => _sources.Count;

	internal void Add(IAudioBusSource source) => _sources.Add(source);

	internal void RenderBlock(Span<float> left, Span<float> right, float masterGain, bool canListen)
	{
		left.Clear();
		right.Clear();
		for (int index = _sources.Count - 1; index >= 0; index--)
		{
			if (!_sources[index].Render(left, right))
			{
				_sources.RemoveAt(index);
			}
		}

		_mixChain.Process(left, right, masterGain, canListen);
		MinimumLimiterGain = MathF.Min(MinimumLimiterGain, _mixChain.LimiterGain);
	}

	/// <summary>The summed mix before the master gain and the ceiling, for headroom.</summary>
	internal void RenderBlockPreChain(Span<float> left, Span<float> right)
	{
		left.Clear();
		right.Clear();
		for (int index = _sources.Count - 1; index >= 0; index--)
		{
			if (!_sources[index].Render(left, right))
			{
				_sources.RemoveAt(index);
			}
		}
	}
}

/// <summary>
/// The real <see cref="HostileMobToneBed"/> on the bus. Levels and the handover rule
/// are the mod's, so what the swarm scenario measures is the shipped behaviour rather
/// than a restatement of it.
/// </summary>
internal sealed class HostileMobBed : IAudioBusSource
{
	private readonly HostileMobToneBed _bed = new();
	private HostileMobToneTarget _target;

	internal void SetTarget(int index, float normalizedX, float normalizedY, float proximity)
	{
		if (index == 0)
		{
			_target = new(true, false, normalizedX, normalizedY, proximity);
		}
	}

	internal void ClearTarget(int index)
	{
		if (index == 0)
		{
			_target = default;
		}
	}

	/// <summary>Hands the voice to a different mob, the way the system does.</summary>
	internal void RetireEmitter(int index)
	{
		if (index == 0)
		{
			_bed.Handoff();
		}
	}

	/// <summary>
	/// Hands the whole set over at once, as <c>HostileMobToneSystem</c> does after it
	/// has reconciled its assignments for the tick.
	/// </summary>
	internal void Apply(float masterGain, in SpatialAudioSettings settings)
	{
		_bed.SetTarget(_target, masterGain, settings, distanceAttenuationEnabled: true);
	}

	public bool Render(Span<float> left, Span<float> right)
	{
		_bed.Render(left, right);
		return true;
	}
}

/// <summary>
/// The terrain bed's three surfaces, targeted the way <see cref="WallToneAudioStream"/>
/// targets them, with the same per-design calibration re-run here.
/// </summary>
internal sealed class TerrainBed : IAudioBusSource
{
	private const float BedVoiceOffsetDecibels = 3f;

	private static readonly WallToneVoiceDesign SideDesign = new(280f, 1_800f, 1.0f);
	private static readonly WallToneVoiceDesign CeilingDesign = new(900f, 4_200f, 3.0f);

	internal static readonly float TargetVoiceLoudness =
		AuthoredAudioLevels.SpatialVoiceReferenceLoudness - BedVoiceOffsetDecibels;
	internal static readonly float SideVoiceGain = CalibrateVoiceGain(SideDesign);
	internal static readonly float CeilingVoiceGain = CalibrateVoiceGain(CeilingDesign);

	internal static (float Side, float Ceiling) CalibratedVoiceLoudness() =>
		(
			MeasureCalibratedVoice(SideDesign, SideVoiceGain),
			MeasureCalibratedVoice(CeilingDesign, CeilingVoiceGain)
		);

	private readonly WallToneVoice _leftVoice = new(0x93A4_52E1u, SideDesign);
	private readonly WallToneVoice _rightVoice = new(0xD17B_8305u, SideDesign);
	private readonly WallToneVoice _ceilingVoice = new(0x6C8E_9CF3u, CeilingDesign);
	private readonly SpatialAudioEmitter _leftEmitter = new();
	private readonly SpatialAudioEmitter _rightEmitter = new();
	private readonly SpatialAudioEmitter _ceilingEmitter = new();
	private SpatialAudioSettings _settings;

	/// <summary>
	/// How many surfaces answer at once, hard against the listener. One is an open
	/// wall, two is the corridor the bed's level offset is authored for, four is being
	/// fully enclosed, which is the loudest the three-voice bed gets.
	/// </summary>
	internal void SetActiveSurfaces(int count, float masterGain, in SpatialAudioSettings settings)
	{
		_settings = settings;
		SetVoice(_leftVoice, _leftEmitter, -1f, 0f, count >= 1, masterGain * SideVoiceGain);
		SetVoice(_rightVoice, _rightEmitter, 1f, 0f, count >= 2, masterGain * SideVoiceGain);
		SetVoice(_ceilingVoice, _ceilingEmitter, 0f, -1f, count >= 3, masterGain * CeilingVoiceGain);
	}

	internal void SetEnclosed(float masterGain, in SpatialAudioSettings settings) =>
		SetActiveSurfaces(3, masterGain, settings);

	public bool Render(Span<float> left, Span<float> right)
	{
		_leftEmitter.Render(_leftVoice, _settings, left, right);
		_rightEmitter.Render(_rightVoice, _settings, left, right);
		_ceilingEmitter.Render(_ceilingVoice, _settings, left, right);
		return true;
	}

	private static void SetVoice(
		WallToneVoice voice,
		SpatialAudioEmitter emitter,
		float normalizedX,
		float normalizedY,
		bool active,
		float gain)
	{
		float proximity = active ? 1f : 0f;
		voice.SetTarget(voice.FrequencyForProximity(proximity), active ? gain : 0f);
		emitter.SetTarget(new(
			normalizedX,
			normalizedY,
			SpatialAudioDistanceGain.FromProximity(proximity)));
	}

	private static float CalibrateVoiceGain(WallToneVoiceDesign design)
	{
		WallToneVoice voice = new(0x51F0_2C7Bu, design);
		voice.SetTarget(voice.FrequencyForProximity(1f), gain: 1f);
		float[] samples = new float[SpatialAudioTransformCalculator.SampleRate];
		for (int index = 0; index < samples.Length; index++)
		{
			samples[index] = voice.ReadSample(pitchRatio: 1f);
		}
		return AuthoredAudioLevels.LoudnessTrim(
			samples,
			TargetVoiceLoudness);
	}

	private static float MeasureCalibratedVoice(WallToneVoiceDesign design, float gain)
	{
		WallToneVoice voice = new(0x51F0_2C7Bu, design);
		voice.SetTarget(voice.FrequencyForProximity(1f), gain: 1f);
		float[] samples = new float[SpatialAudioTransformCalculator.SampleRate];
		for (int index = 0; index < samples.Length; index++)
		{
			samples[index] = voice.ReadSample(pitchRatio: 1f) * gain;
		}
		return AudioLoudness.Measure(samples);
	}
}

/// <summary>The freecam body beacon, as <see cref="FreecamBodyBeaconAudioStream"/> drives it.</summary>
internal sealed class BodyBeacon : IAudioBusSource
{
	internal static readonly float VoiceGain = CalibrateVoiceGain();

	private readonly BodyBeaconVoice _voice = new();
	private readonly SpatialAudioEmitter _emitter = new(gainAttackSeconds: 0.003f);
	private SpatialAudioSettings _settings;

	internal void SetTarget(float normalizedX, float normalizedY, float masterGain, in SpatialAudioSettings settings)
	{
		_settings = settings;
		_voice.SetGain(masterGain * VoiceGain);
		_emitter.SetTarget(new(normalizedX, normalizedY, 1f));
	}

	public bool Render(Span<float> left, Span<float> right)
	{
		_emitter.Render(_voice, _settings, left, right);
		return true;
	}

	private static float CalibrateVoiceGain()
	{
		BodyBeaconVoice voice = new();
		voice.SetGain(1f);
		float[] samples = new float[SpatialAudioTransformCalculator.SampleRate];
		for (int index = 0; index < samples.Length; index++)
		{
			samples[index] = voice.ReadSample(pitchRatio: 1f);
		}
		return AuthoredAudioLevels.LoudnessTrim(
			samples,
			AuthoredAudioLevels.SpatialVoiceReferenceLoudness);
	}
}

/// <summary>
/// The authored one-shot designs, mirrored from <c>FootstepSoundBank</c> and
/// <c>WallBumpSoundBank</c>. Only the numbers are copied; the buffers themselves come
/// out of the real <see cref="ImpactToneSynthesizer"/> and its normalization.
/// </summary>
internal static class Cues
{
	internal static readonly (string Name, NavigationCueKind Kind)[] Navigation =
	[
		("ascending", NavigationCueKind.Ascending),
		("descending", NavigationCueKind.Descending),
		("safe drop", NavigationCueKind.SafeDrop),
		("unsafe drop", NavigationCueKind.UnsafeDrop),
		("platform landmark", NavigationCueKind.Platform),
		("minecart landmark", NavigationCueKind.MinecartTrack),
		("rope landmark", NavigationCueKind.Rope),
		("housing suitable", NavigationCueKind.HousingSuitable),
		("housing occupied", NavigationCueKind.HousingOccupied),
		("housing unsuitable", NavigationCueKind.HousingUnsuitable),
	];

	internal static readonly (string Name, ImpactToneDesign Design)[] Footsteps =
	[
		("footstep 1", new(180f, 40.0f, 0.180f, 0.135f, 0.18f, 25.0f, 0.120f, 0x16A3_7421u)),
		("footstep 2", new(186f, 39.5f, 0.185f, 0.130f, 0.22f, 25.5f, 0.115f, 0xB529_7A4Du)),
		("footstep 3", new(192f, 39.0f, 0.190f, 0.125f, 0.26f, 26.0f, 0.110f, 0x68E3_1DA4u)),
		("footstep 4", new(198f, 38.5f, 0.195f, 0.120f, 0.30f, 26.5f, 0.105f, 0x9C71_53B2u)),
	];

	internal static readonly (string Name, ImpactToneDesign Design)[] Bumps =
	[
		("bump terrain", new(146f, 130f, 0.050f, 0.20f, 0.32f, 65f, 0.45f, 0x4C1D_93E7u)),
		("bump door", new(232f, 105f, 0.180f, 0.14f, 0.28f, 42f, 0.30f, 0x7E36_2A55u)),
	];

	private static float[][]? _footstepTones;
	private static float[][]? _bumpTones;
	private static float[][]? _navigationTones;

	internal static float[][] NavigationTones =>
		_navigationTones ??= [.. Navigation.Select(entry => NavigationCueDesigns.Render(entry.Kind))];

	internal static float[][] FootstepTones =>
		_footstepTones ??= [.. Footsteps.Select(entry => ImpactToneSynthesizer.Render(entry.Design))];

	internal static float[][] BumpTones =>
		_bumpTones ??= [.. Bumps.Select(entry => ImpactToneSynthesizer.Render(entry.Design))];
}
