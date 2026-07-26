#nullable enable

using System;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Localization;
using Terraria.ModLoader;
using Ariadne.Audio;
using Ariadne.Configs;

namespace Ariadne.Ingame.WallTones;

[Autoload(Side = ModSide.Client)]
internal sealed class WallToneSystem : ModSystem
{
	private const float MinimumTeleportThresholdPixels = 96f;

	private WallToneAudioStream? _audio;
	private WallToneSnapshot _snapshot;
	private Vector2 _previousObserverCenter;
	private uint _observerRevision;
	private bool? _sessionEnabledOverride;
	private bool _hasPreviousObserverCenter;
	private bool _hasObserverRevision;
	private bool _hasConfiguredEnabledState;
	private bool _lastConfiguredEnabled;

	public override void Load()
	{
		_audio = WallToneAudioStream.TryCreate(Mod);
		AriadneClientConfig config = ModContent.GetInstance<AriadneClientConfig>();
		_lastConfiguredEnabled = config.WallToneEnabled;
		_hasConfiguredEnabledState = true;
		_snapshot = WallToneSnapshot.Empty(config.WallToneRangeTiles * 16f);
	}

	public override void OnWorldLoad()
	{
		ResetTerrainHistory();
		_audio?.StopAndReset();
	}

	public override void OnWorldUnload()
	{
		ResetTerrainHistory();
		_audio?.StopAndReset();
	}

	public override void PostUpdatePlayers()
	{
		AriadneClientConfig config = ModContent.GetInstance<AriadneClientConfig>();
		if (!IsEffectivelyEnabled(config) || !GameplayAudioGate.CanListen())
		{
			ResetTerrainHistory();
			return;
		}

		SpatialObserverSnapshot observer = SpatialObserverContext.Current;
		SynchronizeObserverRevision();
		Vector2 observerCenter = observer.Center;
		if (_hasPreviousObserverCenter)
		{
			float teleportThreshold = MathF.Max(
				MinimumTeleportThresholdPixels,
				MathF.Max(observer.Width, observer.Height) * 4f);
			if (Vector2.DistanceSquared(observerCenter, _previousObserverCenter) >
				teleportThreshold * teleportThreshold)
			{
				_audio?.ResetForDiscontinuity();
			}
		}

		_previousObserverCenter = observerCenter;
		_hasPreviousObserverCenter = true;
		_snapshot = WallToneTerrainProbe.Sample(observer, config.WallToneRangeTiles);
	}

	public override void PostUpdateInput()
	{
		AriadneClientConfig config = ModContent.GetInstance<AriadneClientConfig>();
		SynchronizeObserverRevision();
		SynchronizeConfiguredEnabledState(config);
		if (CanUseSessionToggle() && AriadneMod.ToggleWallTonesKeybind?.JustPressed == true)
		{
			_sessionEnabledOverride = !IsEffectivelyEnabled(config);
			string announcementKey = _sessionEnabledOverride.Value
				? "Mods.Ariadne.Announcements.WallTonesOn"
				: "Mods.Ariadne.Announcements.WallTonesOff";
			AriadneMod.ScreenReader.Output(Language.GetTextValue(announcementKey));
		}

		bool shouldStream = IsEffectivelyEnabled(config) &&
			config.WallToneVolumePercent > 0 &&
			Main.soundVolume > 0f &&
			GameplayAudioGate.CanListen();
		if (!shouldStream)
		{
			_audio?.StopAndReset();
			ResetTerrainHistory();
			return;
		}

		_audio?.UpdateTargets(_snapshot, config);
		_audio?.Pump();
	}

	public override void Unload()
	{
		_audio?.Dispose();
		_audio = null;
		_sessionEnabledOverride = null;
		_hasConfiguredEnabledState = false;
		_lastConfiguredEnabled = false;
		ResetTerrainHistory();
	}

	private void SynchronizeConfiguredEnabledState(AriadneClientConfig config)
	{
		if (_hasConfiguredEnabledState && config.WallToneEnabled != _lastConfiguredEnabled)
		{
			_sessionEnabledOverride = null;
		}

		_lastConfiguredEnabled = config.WallToneEnabled;
		_hasConfiguredEnabledState = true;
	}

	private bool IsEffectivelyEnabled(AriadneClientConfig config)
	{
		return _sessionEnabledOverride ?? config.WallToneEnabled;
	}

	private static bool CanUseSessionToggle()
	{
		return !Main.gameMenu &&
			!Main.gamePaused &&
			Main.hasFocus &&
			GameplayAudioGate.CanListen();
	}

	private void ResetTerrainHistory()
	{
		_snapshot = default;
		_previousObserverCenter = Vector2.Zero;
		_hasPreviousObserverCenter = false;
		_hasObserverRevision = false;
		_observerRevision = 0;
	}

	private void SynchronizeObserverRevision()
	{
		uint revision = SpatialObserverContext.Revision;
		if (_hasObserverRevision && revision != _observerRevision)
		{
			_audio?.ResetForDiscontinuity();
			_snapshot = default;
			_previousObserverCenter = Vector2.Zero;
			_hasPreviousObserverCenter = false;
		}

		_observerRevision = revision;
		_hasObserverRevision = true;
	}
}
