#nullable enable

using System;
using Terraria;
using Terraria.Localization;
using Terraria.ModLoader;
using Ariadne.Audio;
using Ariadne.Configs;

namespace Ariadne.Ingame.Vitals;

/// <summary>
/// Pulses a synthesized heartbeat once health falls into the wounded band and
/// quickens it at each lower threshold, so remaining health is legible without
/// reading the life meter.
/// </summary>
[Autoload(Side = ModSide.Client)]
internal sealed class LowHealthHeartbeatSystem : ModSystem
{
	/// <summary>
	/// Health must climb this far back above a stage boundary before the beat
	/// eases off, so hovering on a threshold cannot flutter between two rates.
	/// </summary>
	private const float StageReleaseMargin = 0.02f;

	private const int CriticalStageIndex = 4;

	/// <summary>
	/// The rate, pitch and level the beat takes in each band of remaining health.
	/// Exposed so the sound guide can audition the same table rather than restate it,
	/// which would let the two drift apart.
	/// </summary>
	internal static readonly HeartbeatStage[] Stages =
	[
		new(MaximumHealthFraction: 0.50f, IntervalTicks: 100, Pitch: 0.00f, VolumeScale: 0.74f),
		new(MaximumHealthFraction: 0.40f, IntervalTicks: 82, Pitch: 0.05f, VolumeScale: 0.82f),
		new(MaximumHealthFraction: 0.30f, IntervalTicks: 66, Pitch: 0.10f, VolumeScale: 0.88f),
		new(MaximumHealthFraction: 0.20f, IntervalTicks: 52, Pitch: 0.15f, VolumeScale: 0.94f),
		new(MaximumHealthFraction: 0.10f, IntervalTicks: 40, Pitch: 0.20f, VolumeScale: 1.00f),
		new(MaximumHealthFraction: 0.05f, IntervalTicks: 30, Pitch: 0.26f, VolumeScale: 1.00f),
	];

	private HeartbeatSound? _sound;
	private int _stage = -1;
	private int _ticksUntilNextBeat;

	public override void Load()
	{
		_sound = HeartbeatSound.Create(Mod);
	}

	public override void OnWorldLoad() => ResetTracking();

	public override void OnWorldUnload() => ResetTracking();

	public override void PostUpdatePlayers()
	{
		AriadneClientConfig config = ModContent.GetInstance<AriadneClientConfig>();
		if (!config.LowHealthHeartbeatEnabled && !config.LowHealthAnnouncementsEnabled)
		{
			ResetTracking();
			return;
		}

		Player player = Main.LocalPlayer;
		if (!player.active || player.dead || player.ghost || player.statLifeMax2 <= 0)
		{
			ResetTracking();
			return;
		}
		if (!GameplayAudioGate.CanListen())
		{
			// Hold the current stage so returning from a menu neither repeats an
			// escalation nor invents a recovery the player never earned.
			return;
		}

		float healthFraction = Math.Clamp(player.statLife / (float)player.statLifeMax2, 0f, 1f);
		int stage = ResolveStage(healthFraction);
		if (stage < 0)
		{
			if (_stage >= 0)
			{
				Announce(config, "Mods.Ariadne.Announcements.HealthRecovered");
			}
			ResetTracking();
			return;
		}

		if (stage > _stage)
		{
			AnnounceEscalation(config, stage, healthFraction);
			_ticksUntilNextBeat = 0;
		}
		_stage = stage;

		if (!config.LowHealthHeartbeatEnabled || config.LowHealthHeartbeatVolumePercent <= 0)
		{
			return;
		}

		if (_ticksUntilNextBeat > 0)
		{
			_ticksUntilNextBeat--;
			return;
		}

		HeartbeatStage beat = Stages[stage];
		_sound?.Play(
			config.LowHealthHeartbeatVolumePercent / 100f * beat.VolumeScale,
			beat.Pitch);
		_ticksUntilNextBeat = beat.IntervalTicks;
	}

	public override void Unload()
	{
		_sound?.Dispose();
		_sound = null;
		ResetTracking();
	}

	private int ResolveStage(float healthFraction)
	{
		int stage = -1;
		for (int i = 0; i < Stages.Length; i++)
		{
			if (healthFraction <= Stages[i].MaximumHealthFraction)
			{
				stage = i;
			}
		}

		if (stage < _stage &&
			_stage >= 0 &&
			healthFraction < Stages[_stage].MaximumHealthFraction + StageReleaseMargin)
		{
			return _stage;
		}
		return stage;
	}

	private static void AnnounceEscalation(AriadneClientConfig config, int stage, float healthFraction)
	{
		string key = stage >= CriticalStageIndex
			? "Mods.Ariadne.Announcements.HealthCritical"
			: "Mods.Ariadne.Announcements.HealthLow";
		Announce(config, key, (int)MathF.Round(healthFraction * 100f));
	}

	private static void Announce(AriadneClientConfig config, string key, params object[] arguments)
	{
		if (config.LowHealthAnnouncementsEnabled)
		{
			AriadneMod.ScreenReader.Output(Language.GetTextValue(key, arguments));
		}
	}

	private void ResetTracking()
	{
		_stage = -1;
		_ticksUntilNextBeat = 0;
	}

	internal readonly record struct HeartbeatStage(
		float MaximumHealthFraction,
		int IntervalTicks,
		float Pitch,
		float VolumeScale);
}
