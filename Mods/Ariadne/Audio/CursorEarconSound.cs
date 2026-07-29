#nullable enable

using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using ReLogic.Utilities;
using Terraria;
using Terraria.Audio;
using Terraria.ID;
using Terraria.ModLoader;
using Ariadne.Configs;
using Ariadne.Ingame;

namespace Ariadne.Audio;

/// <summary>
/// Plays the native sound associated with the tile or liquid under the world cursor.
/// Installed Terraria PCM is cached and rendered through Ariadne's spatializer;
/// normal SoundEngine playback remains the fallback for unavailable assets.
/// </summary>
internal sealed class CursorEarconSound : IDisposable
{
	private const int DelayTailFrames = 64;
	private const float MaximumCueSeconds = 1f;
	private const string CursorSoundIdentifierPrefix = "Ariadne/CursorEarcon/";

	private static readonly string[] CommonNativeSoundPaths =
	[
		"Terraria/Sounds/Dig_0",
		"Terraria/Sounds/Dig_1",
		"Terraria/Sounds/Dig_2",
		"Terraria/Sounds/Tink_0",
		"Terraria/Sounds/Tink_1",
		"Terraria/Sounds/Tink_2",
		"Terraria/Sounds/Grass",
		"Terraria/Sounds/Shatter",
		"Terraria/Sounds/Coins",
		"Terraria/Sounds/Item_1",
		"Terraria/Sounds/Item_11",
		"Terraria/Sounds/Item_27",
		"Terraria/Sounds/Item_48",
		"Terraria/Sounds/Item_49",
		"Terraria/Sounds/Item_50",
		"Terraria/Sounds/Item_52",
		"Terraria/Sounds/Item_127",
		"Terraria/Sounds/Item_173",
		"Terraria/Sounds/Item_177",
		"Terraria/Sounds/Splash_0",
		"Terraria/Sounds/Splash_1",
		"Terraria/Sounds/Splash_2",
		"Terraria/Sounds/Splash_3",
		"Terraria/Sounds/Splash_4",
		"Terraria/Sounds/Splash_5",
		"Terraria/Sounds/Liquid_1",
	];

	[ThreadStatic]
	private static CursorEarconSound? _scopedTileSoundPlayer;

	private static bool _playSoundHookInstalled;

	private readonly Mod _owner;
	private readonly AriadneAudioBus _bus;
	private readonly NativeSoundPcmCache _pcmCache;
	private readonly List<RenderedCursorCue> _renderedSounds = [];
	private readonly List<SlotId> _fallbackSlots = [];
	private readonly Dictionary<string, int> _nextVariantBySoundPath = [];
	private readonly Dictionary<int, int> _nextLiquidSoundByType = [];

	private Vector2 _scopedWorldPosition;
	private bool _disabledAfterFailure;
	private bool _disposed;

	private CursorEarconSound(Mod owner, AriadneAudioBus bus)
	{
		_owner = owner;
		_bus = bus;
		_pcmCache = new(owner);
		foreach (string soundPath in CommonNativeSoundPaths)
		{
			_pcmCache.Queue(soundPath);
		}
	}

	internal static CursorEarconSound? Create(Mod owner)
	{
		if (Main.dedServ)
		{
			return null;
		}
		if (!SoundEngine.IsAudioSupported)
		{
			owner.Logger.Warn("Cursor earcons are unavailable because this client does not support audio.");
			return null;
		}

		AriadneAudioBus? bus = AudioBusSystem.Bus;
		if (bus is null)
		{
			owner.Logger.Warn("Cursor earcons are unavailable because the audio bus could not be created.");
			return null;
		}

		try
		{
			InstallPlaySoundHook();
			return new(owner, bus);
		}
		catch (Exception exception)
		{
			owner.Logger.Warn(
				$"Cursor earcons are unavailable because Terraria's native sound dispatcher " +
				$"could not be connected: {exception.GetBaseException().Message}");
			return null;
		}
	}

	internal void Play(Point tilePosition, AriadneClientConfig config)
	{
		StopCurrent();
		if (_disposed ||
			_disabledAfterFailure ||
			!CanPlay(config) ||
			!WorldGen.InWorld(tilePosition.X, tilePosition.Y, 1))
		{
			return;
		}

		Tile tile = Framing.GetTileSafely(tilePosition.X, tilePosition.Y);
		if (!tile.HasTile && tile.LiquidAmount <= 0)
		{
			return;
		}

		Vector2 worldPosition = new(
			tilePosition.X * 16f + 8f,
			tilePosition.Y * 16f + 8f);

		try
		{
			if (tile.LiquidAmount > 0)
			{
				PlayLiquid(tile.LiquidType, worldPosition, config);
				return;
			}

			PlayNativeTileHitSound(tilePosition, tile, worldPosition);
		}
		catch (Exception exception)
		{
			DisableAfterFailure(exception);
		}
	}

	internal void Update(AriadneClientConfig config)
	{
		if (_disposed)
		{
			return;
		}

		_pcmCache.Update();
		if (!CanPlay(config))
		{
			StopCurrent();
			return;
		}

		try
		{
			SpatialAudioSettings settings = config.ToSpatialAudioSettings();
			for (int index = _renderedSounds.Count - 1; index >= 0; index--)
			{
				RenderedCursorCue cue = _renderedSounds[index];
				if (cue.Voice.IsFinished)
				{
					_renderedSounds.RemoveAt(index);
					continue;
				}

				// The cue is re-placed every tick rather than frozen at the position it
				// was fired from, so freecam or a moving player carries it.
				cue.Voice.Update(
					SpatialObserverContext.Current.NormalizeToViewport(cue.WorldPosition),
					settings,
					CueVolume(config, cue.LoudnessTrim));
			}

			for (int index = _fallbackSlots.Count - 1; index >= 0; index--)
			{
				if (!SoundEngine.TryGetActiveSound(
						_fallbackSlots[index],
						out ActiveSound? sound) ||
					!sound.IsPlayingOrPaused)
				{
					_fallbackSlots.RemoveAt(index);
				}
			}
		}
		catch (Exception exception)
		{
			DisableAfterFailure(exception);
		}
	}

	internal void StopAndReset()
	{
		if (_disposed)
		{
			return;
		}

		StopCurrent();
		_nextVariantBySoundPath.Clear();
		_nextLiquidSoundByType.Clear();
	}

	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		StopCurrent();
		_pcmCache.Dispose();
		_nextVariantBySoundPath.Clear();
		_nextLiquidSoundByType.Clear();
		_disposed = true;
	}

	private static void InstallPlaySoundHook()
	{
		if (_playSoundHookInstalled)
		{
			return;
		}

		MethodInfo? playSound = typeof(SoundEngine).GetMethod(
			nameof(SoundEngine.PlaySound),
			BindingFlags.Public | BindingFlags.Static,
			binder: null,
			[
				typeof(SoundStyle).MakeByRefType(),
				typeof(Vector2?),
				typeof(SoundUpdateCallback),
			],
			modifiers: null);
		if (playSound is null)
		{
			throw new MissingMethodException(
				typeof(SoundEngine).FullName,
				"PlaySound(in SoundStyle, Vector2?, SoundUpdateCallback)");
		}

		MonoModHooks.Add(playSound, (PlaySoundHook)InterceptPlaySound);
		_playSoundHookInstalled = true;
	}

	private static SlotId InterceptPlaySound(
		PlaySoundOrig original,
		in SoundStyle style,
		Vector2? position,
		SoundUpdateCallback? updateCallback)
	{
		CursorEarconSound? player = _scopedTileSoundPlayer;
		if (player is null)
		{
			return original(in style, position, updateCallback);
		}

		return player.PlaySelectedTileStyle(original, in style, updateCallback);
	}

	private void PlayNativeTileHitSound(Point tilePosition, Tile tile, Vector2 worldPosition)
	{
		_scopedWorldPosition = worldPosition;
		_scopedTileSoundPlayer = this;
		try
		{
			WorldGen.KillTile_PlaySounds(
				tilePosition.X,
				tilePosition.Y,
				fail: true,
				tile);
		}
		finally
		{
			_scopedTileSoundPlayer = null;
		}
	}

	private SlotId PlaySelectedTileStyle(
		PlaySoundOrig original,
		in SoundStyle source,
		SoundUpdateCallback? originalCallback)
	{
		ResolvedNativeSound resolved = ResolveVariant(in source);
		AriadneClientConfig config = ModContent.GetInstance<AriadneClientConfig>();
		if (originalCallback is null &&
			!HasEnabledResourcePacks() &&
			_pcmCache.TryGetOrQueue(resolved.SoundPath, out NativePcmClip? clip) &&
			clip is not null)
		{
			PlayRendered(clip, in resolved, _scopedWorldPosition, config);
			return SlotId.Invalid;
		}

		return PlayFallback(
			original,
			in resolved,
			_scopedWorldPosition,
			originalCallback);
	}

	private void PlayLiquid(
		int liquidType,
		Vector2 worldPosition,
		AriadneClientConfig config)
	{
		SoundStyle source = NextLiquidStyle(liquidType);
		ResolvedNativeSound resolved = ResolveVariant(in source);
		if (!HasEnabledResourcePacks() &&
			_pcmCache.TryGetOrQueue(resolved.SoundPath, out NativePcmClip? clip) &&
			clip is not null)
		{
			PlayRendered(clip, in resolved, worldPosition, config);
			return;
		}

		SoundStyle prepared = PrepareFallbackStyle(in resolved, worldPosition);
		SlotId slot = SoundEngine.PlaySound(
			in prepared,
			worldPosition,
			sound => UpdateFallbackSound(sound, originalCallback: null));
		TrackFallback(slot);
	}

	private SoundStyle NextLiquidStyle(int liquidType)
	{
		SoundStyle[] styles = liquidType switch
		{
			LiquidID.Lava => [SoundID.Lavafall],
			LiquidID.Shimmer =>
			[
				SoundID.Shimmer1,
				SoundID.Shimmer2,
				SoundID.ShimmerWeak1,
				SoundID.ShimmerWeak2,
			],
			_ => [SoundID.Splash, SoundID.SplashWeak],
		};

		int next = _nextLiquidSoundByType.GetValueOrDefault(liquidType);
		_nextLiquidSoundByType[liquidType] = (next + 1) % styles.Length;
		return styles[next % styles.Length];
	}

	private ResolvedNativeSound ResolveVariant(in SoundStyle source)
	{
		ReadOnlySpan<int> variants = source.Variants;
		string soundPath = source.SoundPath;
		if (!variants.IsEmpty)
		{
			int next = _nextVariantBySoundPath.GetValueOrDefault(source.SoundPath);
			soundPath += variants[next % variants.Length];
			_nextVariantBySoundPath[source.SoundPath] = (next + 1) % variants.Length;
		}

		return new(
			soundPath,
			source.Volume,
			source.Pitch,
			source.PitchVariance);
	}

	private void PlayRendered(
		NativePcmClip clip,
		in ResolvedNativeSound source,
		Vector2 worldPosition,
		AriadneClientConfig config)
	{
		Vector2 normalizedPosition =
			SpatialObserverContext.Current.NormalizeToViewport(worldPosition);
		SpatialAudioSettings settings = config.ToSpatialAudioSettings();
		SpatialAudioTransform transform = SpatialAudioTransformCalculator.Calculate(
			normalizedPosition.X,
			normalizedPosition.Y,
			settings);
		float randomizedPitch = source.Pitch +
			(Random.Shared.NextSingle() - 0.5f) * source.PitchVariance;
		float nativePitchRatio = MathF.Pow(2f, randomizedPitch);
		int sourceFrameLimit = Math.Min(
			clip.Samples.Length,
			(int)MathF.Round(
				MaximumCueSeconds * SpatialAudioTransformCalculator.SampleRate));
		float playbackRatio = Math.Max(
			0.05f,
			transform.PitchRatio * nativePitchRatio);
		int frameCount = Math.Max(
			1,
			(int)MathF.Ceiling(sourceFrameLimit / playbackRatio)) +
			DelayTailFrames;
		SpatialOneShotVoice voice = new(
			new PcmPlaybackVoice(clip.Samples, sourceFrameLimit, nativePitchRatio),
			frameCount,
			normalizedPosition,
			settings,
			CueVolume(config, clip.LoudnessTrim));
		_bus.Add(voice);
		_renderedSounds.Add(new(voice, worldPosition, clip.LoudnessTrim));
	}

	/// <summary>
	/// Terraria's sound slider is applied once, by the bus. The clip's own trim is
	/// still folded in here rather than into the samples, so a cue stays levelled
	/// against the shared reference while the slider scales it.
	/// </summary>
	private static float CueVolume(AriadneClientConfig config, float loudnessTrim)
	{
		float configuredVolume = Math.Clamp(
			config.CursorEarconVolumePercent / 100f,
			0f,
			1f);
		return Math.Clamp(configuredVolume * loudnessTrim, 0f, 1f);
	}

	private SlotId PlayFallback(
		PlaySoundOrig original,
		in ResolvedNativeSound source,
		Vector2 worldPosition,
		SoundUpdateCallback? originalCallback)
	{
		SoundStyle prepared = PrepareFallbackStyle(in source, worldPosition);
		SoundUpdateCallback callback = sound =>
			UpdateFallbackSound(sound, originalCallback);
		SlotId slot = original(
			in prepared,
			worldPosition,
			callback);
		TrackFallback(slot);
		return slot;
	}

	private static SoundStyle PrepareFallbackStyle(
		in ResolvedNativeSound source,
		Vector2 worldPosition)
	{
		// This path hands the world position to Terraria, which pans it under its own
		// law, so only the vertical offset below comes from the shared spatial field.
		Vector2 normalizedPosition =
			SpatialObserverContext.Current.NormalizeToViewport(worldPosition);
		float verticalPitchOffset = -0.5f * normalizedPosition.Y;
		return new SoundStyle(source.SoundPath, SoundType.Sound)
		{
			Identifier = CursorSoundIdentifierPrefix + source.SoundPath,
			MaxInstances = 1,
			SoundLimitBehavior = SoundLimitBehavior.ReplaceOldest,
			PlayOnlyIfFocused = true,
			PauseBehavior = PauseBehavior.StopWhenGamePaused,
			IsLooped = false,
			Volume = source.Volume,
			Pitch = Math.Clamp(source.Pitch + verticalPitchOffset, -1f, 1f),
			PitchVariance = source.PitchVariance,
		};
	}

	private bool UpdateFallbackSound(
		ActiveSound sound,
		SoundUpdateCallback? originalCallback)
	{
		if (originalCallback is not null && !originalCallback(sound))
		{
			return false;
		}

		AriadneClientConfig config = ModContent.GetInstance<AriadneClientConfig>();
		if (_disposed || _disabledAfterFailure || !CanPlay(config))
		{
			return false;
		}

		sound.Volume = Math.Clamp(config.CursorEarconVolumePercent / 100f, 0f, 1f);
		return true;
	}

	private static bool CanPlay(AriadneClientConfig config)
	{
		return config.CursorEarconsEnabled &&
			config.CursorEarconVolumePercent > 0 &&
			GameplayAudioGate.CanListen();
	}

	private static bool HasEnabledResourcePacks()
	{
		if (Main.AssetSourceController is null)
		{
			return false;
		}

		foreach (var _ in Main.AssetSourceController.ActiveResourcePackList.EnabledPacks)
		{
			return true;
		}
		return false;
	}

	private void TrackFallback(SlotId slot)
	{
		if (slot.IsValid)
		{
			_fallbackSlots.Add(slot);
		}
	}

	private void StopCurrent()
	{
		foreach (RenderedCursorCue cue in _renderedSounds)
		{
			cue.Voice.Stop();
			_bus.Remove(cue.Voice);
		}
		_renderedSounds.Clear();

		foreach (SlotId slot in _fallbackSlots)
		{
			try
			{
				if (SoundEngine.TryGetActiveSound(slot, out ActiveSound? sound))
				{
					sound.Stop();
				}
			}
			catch
			{
				// Cleanup must remain safe if the audio device disappears.
			}
		}
		_fallbackSlots.Clear();
	}

	private void DisableAfterFailure(Exception exception)
	{
		if (!_disabledAfterFailure)
		{
			_owner.Logger.Warn(
				$"Native cursor-earcon playback failed and has been disabled for this session: " +
				$"{exception.GetBaseException().Message}");
		}

		_disabledAfterFailure = true;
		StopCurrent();
	}

	/// <summary>
	/// A cue still sounding on the bus, with the world position it belongs to so it
	/// can be re-placed each tick, and the trim that keeps it on the shared reference.
	/// </summary>
	private readonly record struct RenderedCursorCue(
		SpatialOneShotVoice Voice,
		Vector2 WorldPosition,
		float LoudnessTrim);

	private readonly record struct ResolvedNativeSound(
		string SoundPath,
		float Volume,
		float Pitch,
		float PitchVariance);

	private delegate SlotId PlaySoundOrig(
		in SoundStyle style,
		Vector2? position,
		SoundUpdateCallback? updateCallback);

	private delegate SlotId PlaySoundHook(
		PlaySoundOrig original,
		in SoundStyle style,
		Vector2? position,
		SoundUpdateCallback? updateCallback);
}
