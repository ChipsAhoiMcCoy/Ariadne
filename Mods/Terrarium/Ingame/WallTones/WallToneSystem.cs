#nullable enable

using System;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Audio;
using Terraria.GameInput;
using Terraria.Localization;
using Terraria.ModLoader;
using Terrarium.Audio;
using Terrarium.Configs;

namespace Terrarium.Ingame.WallTones;

[Autoload(Side = ModSide.Client)]
internal sealed class WallToneSystem : ModSystem
{
	private const float MinimumTeleportThresholdPixels = 96f;

	private WallToneAudioStream? _audio;
	private WallToneSnapshot _snapshot;
	private Vector2 _previousPlayerCenter;
	private bool? _sessionEnabledOverride;
	private bool _hasPreviousPlayerCenter;
	private bool _hasConfiguredEnabledState;
	private bool _lastConfiguredEnabled;

	public override void Load()
	{
		_audio = WallToneAudioStream.TryCreate(Mod);
		TerrariumClientConfig config = ModContent.GetInstance<TerrariumClientConfig>();
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
		TerrariumClientConfig config = ModContent.GetInstance<TerrariumClientConfig>();
		if (!IsEffectivelyEnabled(config) || !CanListenDuringGameplay())
		{
			ResetTerrainHistory();
			return;
		}

		Player player = Main.LocalPlayer;
		Vector2 playerCenter = player.Center;
		if (_hasPreviousPlayerCenter)
		{
			float teleportThreshold = MathF.Max(
				MinimumTeleportThresholdPixels,
				MathF.Max(player.width, player.height) * 4f);
			if (Vector2.DistanceSquared(playerCenter, _previousPlayerCenter) > teleportThreshold * teleportThreshold)
			{
				_audio?.ResetForDiscontinuity();
			}
		}

		_previousPlayerCenter = playerCenter;
		_hasPreviousPlayerCenter = true;
		_snapshot = WallToneTerrainProbe.Sample(player, config.WallToneRangeTiles);
	}

	public override void PostUpdateInput()
	{
		TerrariumClientConfig config = ModContent.GetInstance<TerrariumClientConfig>();
		SynchronizeConfiguredEnabledState(config);
		if (CanUseSessionToggle() && TerrariumMod.ToggleWallTonesKeybind?.JustPressed == true)
		{
			_sessionEnabledOverride = !IsEffectivelyEnabled(config);
			string announcementKey = _sessionEnabledOverride.Value
				? "Mods.Terrarium.Announcements.WallTonesOn"
				: "Mods.Terrarium.Announcements.WallTonesOff";
			TerrariumMod.ScreenReader.Output(Language.GetTextValue(announcementKey));
		}

		bool shouldStream = IsEffectivelyEnabled(config) &&
			config.WallToneVolumePercent > 0 &&
			Main.soundVolume > 0f &&
			CanListenDuringGameplay();
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

	private void SynchronizeConfiguredEnabledState(TerrariumClientConfig config)
	{
		if (_hasConfiguredEnabledState && config.WallToneEnabled != _lastConfiguredEnabled)
		{
			_sessionEnabledOverride = null;
		}

		_lastConfiguredEnabled = config.WallToneEnabled;
		_hasConfiguredEnabledState = true;
	}

	private bool IsEffectivelyEnabled(TerrariumClientConfig config)
	{
		return _sessionEnabledOverride ?? config.WallToneEnabled;
	}

	private static bool CanUseSessionToggle()
	{
		return !Main.gameMenu &&
			!Main.gamePaused &&
			Main.hasFocus &&
			CanListenDuringGameplay();
	}

	private static bool CanListenDuringGameplay()
	{
		if (Main.dedServ ||
			Main.gameMenu ||
			Main.gamePaused ||
			!Main.hasFocus ||
			SoundEngine.AreSoundsPaused ||
			Main.playerInventory ||
			Main.drawingPlayerChat ||
			Main.editSign ||
			Main.editChest ||
			Main.mapFullscreen ||
			Main.ingameOptionsWindow ||
			Main.inFancyUI ||
			Main.InGameUI.CurrentState is not null ||
			(Main.CreativeMenu.Enabled && !Main.CreativeMenu.Blocked) ||
			PlayerInput.WritingText)
		{
			return false;
		}

		Player player = Main.LocalPlayer;
		return player.active &&
			!player.dead &&
			!player.ghost &&
			player.talkNPC < 0 &&
			player.sign < 0;
	}

	private void ResetTerrainHistory()
	{
		_snapshot = default;
		_previousPlayerCenter = Vector2.Zero;
		_hasPreviousPlayerCenter = false;
	}
}
