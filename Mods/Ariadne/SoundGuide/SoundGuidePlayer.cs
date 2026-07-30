#nullable enable

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Audio;
using Terraria.ModLoader;
using Ariadne.Audio;
using Ariadne.Configs;
using Ariadne.Ingame;
using Ariadne.Ingame.Vitals;
using Ariadne.Ingame.WallTones;

namespace Ariadne.SoundGuide;

internal enum WallToneRegion
{
	Left,
	Right,
	Ceiling,
	Floor,
}

/// <summary>A source placed by direction and by fraction of its own cue's range.</summary>
internal readonly record struct SpatialAudition(
	Vector2 Direction,
	float FractionOfRange);

/// <summary>
/// One surface in a terrain-bed audition: which voice answers it, which way it lies,
/// and how far along the configured range it sits.
/// </summary>
internal readonly record struct WallToneAudition(
	WallToneRegion Region,
	Vector2 Direction,
	float FractionOfRange);

/// <summary>
/// Sounds Ariadne's cues on demand from a menu, where none of them would otherwise
/// be heard.
///
/// Every cue is auditioned through the same bank or stream the game plays it
/// through, at the volume the listener has configured for it, so what the guide
/// demonstrates is what the world will actually sound like. The banks are short
/// rendered buffers and the streams are silent until they are given a target, so
/// owning a second set of them for the length of a menu costs less than teaching
/// each of the originals to be driven from two places.
/// </summary>
internal sealed class SoundGuidePlayer : IDisposable
{
	private const float TileSize = 16f;

	/// <summary>
	/// How long a bed is held: three seconds. Long enough to hear a sustained tone settle
	/// and to place it, rather than only to notice that something sounded.
	/// </summary>
	private const int BedSustainTicks = 180;

	/// <summary>
	/// How long a released bed is left to fade before its signal state is reset.
	///
	/// The emitters carry distance gain to silence on an exponential release, so the
	/// bed is never actually silent, only quiet: half a second of it left about a
	/// twentieth of the bed still sounding, and a twentieth of a terrain bed cut
	/// mid-waveform is exactly the faint tick that ended every bed audition. Eight
	/// time constants put it under a thousandth, where the reset cannot be heard.
	/// </summary>
	internal static readonly int BedReleaseTicks = (int)MathF.Ceiling(
		SpatialAudioEmitter.GainReleaseSeconds * 8f * TicksPerSecond);

	private const float TicksPerSecond = 60f;

	/// <summary>Matches the cursor earcon's own limit on a long native clip.</summary>
	private const float MaximumCursorCueSeconds = 1f;

	private const int CursorDelayTailFrames = 64;

	private readonly AriadneAudioBus _bus;
	private readonly FootstepSoundBank? _footsteps;
	private readonly WallBumpSoundBank? _bumps;
	private readonly HeartbeatSound? _heartbeat;
	private readonly WallToneAudioStream? _wallTones;
	private readonly HostileMobToneAudioStream? _mobTones;
	private readonly FreecamBodyBeaconAudioStream? _beacon;
	private readonly NativeSoundPcmCache _pcmCache;
	private readonly List<SpatialOneShotVoice> _spatialVoices = [];
	private readonly Dictionary<string, int> _nextVariantBySoundPath = [];

	/// <summary>
	/// Resets waiting on a release, one per bed. A list rather than a single pending
	/// reset because auditioning a second bed while the first is still fading leaves
	/// two of them in flight, and the one that was overwritten never came back.
	/// </summary>
	private readonly List<(Action Reset, int TicksRemaining)> _pendingBedResets = [];

	private Action<AriadneClientConfig>? _bedRefresh;
	private Action<AriadneClientConfig>? _bedSilence;
	private Action? _bedReset;
	private Action<AriadneClientConfig>? _repeatedShot;
	private int _bedTicksRemaining;
	private int _repeatsRemaining;
	private int _repeatIntervalTicks;
	private int _ticksUntilNextRepeat;
	private bool _disposed;

	private SoundGuidePlayer(Mod owner, AriadneAudioBus bus)
	{
		_bus = bus;
		_footsteps = FootstepSoundBank.Create(owner);
		_bumps = WallBumpSoundBank.Create(owner);
		_heartbeat = HeartbeatSound.Create(owner);
		_wallTones = WallToneAudioStream.TryCreate(owner);
		_mobTones = HostileMobToneAudioStream.TryCreate(owner);
		_beacon = FreecamBodyBeaconAudioStream.TryCreate(owner);
		_pcmCache = new(owner);
		foreach (string soundPath in SoundGuideCatalog.CursorEarconSoundPaths())
		{
			_pcmCache.Queue(soundPath);
		}

		// Held open for as long as the guide is, rather than for the length of each
		// cue. Nothing else is feeding the bus while a menu is up, so the mix is the
		// audition and silence, and a press never waits for the gate to open.
		bus.IsAuditioning = true;
	}

	internal static SoundGuidePlayer? Create(Mod owner)
	{
		if (Main.dedServ)
		{
			return null;
		}

		AriadneAudioBus? bus = AudioBusSystem.Bus;
		if (bus is null)
		{
			owner.Logger.Warn(
				"The Ariadne sound guide cannot play anything because the audio bus " +
				"could not be created.");
			return null;
		}

		return new(owner, bus);
	}

	/// <summary>
	/// Advances every audition that outlives the frame it started on. Driven from
	/// <see cref="SoundGuideSystem"/> rather than from the screen, because a bed
	/// released as the screen closes still has its whole fade left to run and the
	/// screen is no longer being updated to carry it.
	/// </summary>
	internal void Update(AriadneClientConfig config)
	{
		if (_disposed)
		{
			return;
		}

		_pcmCache.Update();
		for (int index = _spatialVoices.Count - 1; index >= 0; index--)
		{
			if (_spatialVoices[index].IsFinished)
			{
				_spatialVoices.RemoveAt(index);
			}
		}

		AdvancePendingBedResets();
		if (_repeatsRemaining > 0 && --_ticksUntilNextRepeat <= 0)
		{
			_repeatedShot?.Invoke(config);
			_repeatsRemaining--;
			_ticksUntilNextRepeat = _repeatIntervalTicks;
		}

		if (_bedTicksRemaining > 0)
		{
			_bedRefresh?.Invoke(config);
			if (--_bedTicksRemaining == 0)
			{
				ReleaseBed(config);
			}
		}
	}

	/// <summary>
	/// Ends whatever is sounding. A bed is handed to its own release rather than cut,
	/// and a one-shot is given its fade and left on the bus to retire itself, which is
	/// what keeps a second press from clicking over the first.
	/// </summary>
	internal void Stop(AriadneClientConfig config)
	{
		if (_disposed)
		{
			return;
		}

		_repeatsRemaining = 0;
		_repeatedShot = null;
		foreach (SpatialOneShotVoice voice in _spatialVoices)
		{
			voice.Stop();
		}

		if (_bedTicksRemaining > 0)
		{
			_bedTicksRemaining = 0;
			ReleaseBed(config);
		}
	}

	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		_disposed = true;
		_bus.IsAuditioning = false;
		foreach (SpatialOneShotVoice voice in _spatialVoices)
		{
			voice.Stop();
		}
		_spatialVoices.Clear();
		// The streams below reset themselves as they are disposed, so a reset still
		// waiting on a release has nothing left to do.
		_pendingBedResets.Clear();
		_wallTones?.Dispose();
		_mobTones?.Dispose();
		_beacon?.Dispose();
		_footsteps?.Dispose();
		_bumps?.Dispose();
		_heartbeat?.Dispose();
		_pcmCache.Dispose();
	}

	internal void PlayFootsteps(float pitch, AriadneClientConfig config)
	{
		Stop(config);
		// A walk rather than a single step: four in a row is what shows that the bank
		// rotates through its timbres instead of repeating one.
		PlayRepeatedly(
			shotConfig => _footsteps?.Play(shotConfig.FootstepVolumePercent / 100f, pitch),
			count: 4,
			intervalTicks: 18,
			config);
	}

	internal void PlayMovementBump(
		MovementBumpSurface surface,
		float pitch,
		AriadneClientConfig config)
	{
		Stop(config);
		_bumps?.Play(config.MovementBumpVolumePercent / 100f, pitch, surface);
	}

	internal void PlayHeartbeat(int stageIndex, AriadneClientConfig config)
	{
		Stop(config);
		LowHealthHeartbeatSystem.HeartbeatStage stage =
			LowHealthHeartbeatSystem.Stages[
				Math.Clamp(stageIndex, 0, LowHealthHeartbeatSystem.Stages.Length - 1)];
		// The rate is most of what a stage means, so it takes more than one beat to
		// tell two of them apart.
		PlayRepeatedly(
			shotConfig => _heartbeat?.Play(
				shotConfig.LowHealthHeartbeatVolumePercent / 100f * stage.VolumeScale,
				stage.Pitch),
			count: 4,
			intervalTicks: stage.IntervalTicks,
			config);
	}

	/// <summary>
	/// Sounds one combat-target cue. It is built here rather than through
	/// <see cref="CombatTargetCueSound"/> because that owner places its cue against
	/// the live observer, and a menu has no world to measure one against.
	/// </summary>
	internal void PlayCombatTargetCue(CombatTargetCue cue, AriadneClientConfig config)
	{
		Stop(config);
		AddSpatialVoice(new SpatialOneShotVoice(
			cue.CreateVoice(),
			cue.BusFrameCount,
			Vector2.Zero,
			config.ToSpatialAudioSettings(),
			Math.Clamp(config.HostileMobToneVolumePercent / 100f, 0f, 1f)));
	}

	/// <summary>
	/// Sounds the earcon a tile of this kind answers with. Terraria's installed PCM is
	/// used when the cache holds it, which is the path the cursor normally takes; a
	/// press made while a clip is still being decoded falls back to Terraria's own
	/// playback, exactly as the cursor does.
	/// </summary>
	internal void PlayCursorEarcon(SoundStyle style, AriadneClientConfig config)
	{
		Stop(config);
		string soundPath = NextVariantPath(in style);
		float volume = Math.Clamp(config.CursorEarconVolumePercent / 100f, 0f, 1f);
		if (_pcmCache.TryGetOrQueue(soundPath, out NativePcmClip? clip) && clip is not null)
		{
			float nativePitchRatio = MathF.Pow(2f, style.Pitch);
			int sourceFrameLimit = Math.Min(
				clip.Samples.Length,
				(int)MathF.Round(
					MaximumCursorCueSeconds * SpatialAudioTransformCalculator.SampleRate));
			int frameCount =
				PcmPlaybackVoice.FrameCountFor(sourceFrameLimit, nativePitchRatio) +
				CursorDelayTailFrames;
			AddSpatialVoice(new SpatialOneShotVoice(
				new PcmPlaybackVoice(clip.Samples, sourceFrameLimit, nativePitchRatio),
				frameCount,
				Vector2.Zero,
				config.ToSpatialAudioSettings(),
				Math.Clamp(volume * clip.LoudnessTrim, 0f, 1f)));
			return;
		}

		PlayNativeFallback(soundPath, in style, volume);
	}

	/// <summary>
	/// Plays a Terraria sound Ariadne triggers but does not mix, such as the splash
	/// that accompanies a breath announcement. It reaches the device through the
	/// game's own engine, so neither the bus nor Ariadne's volumes apply to it.
	/// </summary>
	internal void PlayNativeCue(SoundStyle style, AriadneClientConfig config)
	{
		Stop(config);
		SoundEngine.PlaySound(in style);
	}

	/// <summary>
	/// Holds the terrain bed against surfaces placed at a fraction of the configured
	/// wall-tone range, so what is heard is what that range gives in a real corridor.
	/// </summary>
	internal void SustainWallTones(
		IReadOnlyList<WallToneAudition> surfaces,
		AriadneClientConfig config)
	{
		Stop(config);
		if (_wallTones is null)
		{
			return;
		}

		WallToneAudition[] captured = [.. surfaces];
		SustainBed(
			bedConfig => _wallTones.UpdateTargets(
				BuildWallToneSnapshot(captured, bedConfig),
				bedConfig),
			bedConfig => _wallTones.UpdateTargets(
				WallToneSnapshot.Empty(bedConfig.WallToneRangeTiles * TileSize),
				bedConfig),
			_wallTones.StopAndReset,
			config);
	}

	/// <summary>
	/// Holds the hostile-enemy tone on one enemy placed at a fraction of the configured
	/// tone range, either as the nearest enemy or as the one the player holds.
	/// </summary>
	internal void SustainHostileMobTone(
		SpatialAudition enemy,
		bool isLocked,
		AriadneClientConfig config)
	{
		Stop(config);
		if (_mobTones is null)
		{
			return;
		}

		SustainBed(
			bedConfig =>
			{
				Vector2 normalized = NormalizeAtRange(
					enemy.Direction,
					enemy.FractionOfRange,
					bedConfig.HostileMobToneRangeTiles);
				_mobTones.UpdateTarget(
					new(
						true,
						isLocked,
						normalized.X,
						normalized.Y,
						Math.Clamp(1f - enemy.FractionOfRange, 0f, 1f)),
					bedConfig);
			},
			bedConfig => _mobTones.UpdateTarget(default, bedConfig),
			_mobTones.StopAndReset,
			config);
	}

	/// <summary>
	/// Holds the freecam beacon at one edge of the field, which is where the body is
	/// projected to once the camera has carried it out of view.
	/// </summary>
	internal void SustainBodyBeacon(Vector2 normalizedPosition, AriadneClientConfig config)
	{
		Stop(config);
		if (_beacon is null)
		{
			return;
		}

		SustainBed(
			bedConfig => _beacon.UpdateTarget(normalizedPosition, bedConfig),
			_ => _beacon.Silence(),
			_beacon.StopAndReset,
			config);
	}

	/// <summary>
	/// Where a source that far along the given direction lands in the shared field.
	/// The field is a fixed 120 by 67.5 tiles, so a nearby surface is only slightly
	/// off centre however short its own range is. That is what the world sounds like,
	/// and the guide must not flatter it.
	/// </summary>
	private static Vector2 NormalizeAtRange(
		Vector2 direction,
		float fractionOfRange,
		int rangeTiles)
	{
		Vector2 offset = direction * (rangeTiles * TileSize * fractionOfRange);
		return SpatialFieldPosition.Normalize(
			offset,
			Vector2.Zero,
			SpatialObserverSnapshot.FieldSize);
	}

	private static WallToneSnapshot BuildWallToneSnapshot(
		WallToneAudition[] surfaces,
		AriadneClientConfig config)
	{
		float maximumDistance = config.WallToneRangeTiles * TileSize;
		WallToneRegionSnapshot empty = WallToneRegionSnapshot.Empty(maximumDistance);
		WallToneRegionSnapshot left = empty;
		WallToneRegionSnapshot right = empty;
		WallToneRegionSnapshot ceiling = empty;
		WallToneRegionSnapshot floor = empty;
		foreach (WallToneAudition surface in surfaces)
		{
			WallToneRegionSnapshot region = new(
				true,
				maximumDistance * surface.FractionOfRange,
				maximumDistance,
				NormalizeAtRange(
					surface.Direction,
					surface.FractionOfRange,
					config.WallToneRangeTiles));
			switch (surface.Region)
			{
				case WallToneRegion.Left:
					left = region;
					break;
				case WallToneRegion.Right:
					right = region;
					break;
				case WallToneRegion.Ceiling:
					ceiling = region;
					break;
				default:
					floor = region;
					break;
			}
		}

		return new(left, right, ceiling, floor);
	}

	private void PlayNativeFallback(string soundPath, in SoundStyle style, float volume)
	{
		// Rebuilt rather than played as declared, because a resolved variant is a path
		// of its own and because an ambient loop such as the lava flow would otherwise
		// never stop.
		SoundStyle prepared = new(soundPath, SoundType.Sound)
		{
			Identifier = "Ariadne/SoundGuide/" + soundPath,
			MaxInstances = 1,
			SoundLimitBehavior = SoundLimitBehavior.ReplaceOldest,
			IsLooped = false,
			Volume = style.Volume * volume,
			Pitch = style.Pitch,
			PitchVariance = style.PitchVariance,
		};
		SoundEngine.PlaySound(in prepared);
	}

	private string NextVariantPath(in SoundStyle style)
	{
		ReadOnlySpan<int> variants = style.Variants;
		if (variants.IsEmpty)
		{
			return style.SoundPath;
		}

		int next = _nextVariantBySoundPath.GetValueOrDefault(style.SoundPath);
		_nextVariantBySoundPath[style.SoundPath] = (next + 1) % variants.Length;
		return style.SoundPath + variants[next % variants.Length];
	}

	private void AddSpatialVoice(SpatialOneShotVoice voice)
	{
		_spatialVoices.Add(voice);
		_bus.Add(voice);
	}

	private void PlayRepeatedly(
		Action<AriadneClientConfig> shot,
		int count,
		int intervalTicks,
		AriadneClientConfig config)
	{
		shot(config);
		_repeatedShot = shot;
		_repeatsRemaining = Math.Max(0, count - 1);
		_repeatIntervalTicks = Math.Max(1, intervalTicks);
		_ticksUntilNextRepeat = _repeatIntervalTicks;
	}

	private void SustainBed(
		Action<AriadneClientConfig> refresh,
		Action<AriadneClientConfig> silence,
		Action reset,
		AriadneClientConfig config)
	{
		// This bed is about to sound again, so any reset its last release left waiting
		// has to be dropped: resetting a stream that has just been given a live target
		// is the step the release was scheduled to avoid in the first place.
		_pendingBedResets.RemoveAll(pending => pending.Reset.Equals(reset));
		_bedRefresh = refresh;
		_bedSilence = silence;
		_bedReset = reset;
		_bedTicksRemaining = BedSustainTicks;
		refresh(config);
	}

	private void ReleaseBed(AriadneClientConfig config)
	{
		_bedRefresh = null;
		_bedSilence?.Invoke(config);
		_bedSilence = null;
		if (_bedReset is not null)
		{
			_pendingBedResets.Add((_bedReset, BedReleaseTicks));
			_bedReset = null;
		}
	}

	private void AdvancePendingBedResets()
	{
		for (int index = _pendingBedResets.Count - 1; index >= 0; index--)
		{
			(Action reset, int ticksRemaining) = _pendingBedResets[index];
			if (--ticksRemaining > 0)
			{
				_pendingBedResets[index] = (reset, ticksRemaining);
				continue;
			}

			_pendingBedResets.RemoveAt(index);
			reset();
		}
	}
}
