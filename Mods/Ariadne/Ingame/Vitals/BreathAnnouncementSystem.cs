#nullable enable

using System;
using Terraria;
using Terraria.Audio;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;
using Ariadne.Audio;
using Ariadne.Configs;

namespace Ariadne.Ingame.Vitals;

/// <summary>
/// Reports submersion and the draining breath meter, which are otherwise only
/// visible as an on-screen bubble bar. Each report is paired with Terraria's own
/// splash so the remaining air is recognizable before the words finish.
/// </summary>
[Autoload(Side = ModSide.Client)]
internal sealed class BreathAnnouncementSystem : ModSystem
{
	private const int ThresholdStepPercent = 10;

	/// <summary>
	/// Bobbing at a liquid surface flips the drowning test rapidly. Require the
	/// new state to hold before speaking either transition.
	/// </summary>
	private const int StableUpdateCount = 12;

	private bool _submerged;
	private int _pendingUpdates;
	private int _nextThresholdPercent;
	private bool _announcedEmpty;

	public override void OnWorldLoad() => ResetTracking();

	public override void OnWorldUnload() => ResetTracking();

	public override void PostUpdatePlayers()
	{
		if (!ModContent.GetInstance<AriadneClientConfig>().BreathAnnouncementsEnabled)
		{
			ResetTracking();
			return;
		}

		Player player = Main.LocalPlayer;
		if (!player.active || player.dead || player.ghost)
		{
			ResetTracking();
			return;
		}
		if (!GameplayAudioGate.CanListen())
		{
			return;
		}

		UpdateSubmersion(player);
		if (_submerged)
		{
			AnnounceRemainingBreath(player);
		}
	}

	private void UpdateSubmersion(Player player)
	{
		bool submerged = Collision.DrownCollision(
			player.position,
			player.width,
			player.height,
			player.gravDir);
		if (submerged == _submerged)
		{
			_pendingUpdates = 0;
			return;
		}

		_pendingUpdates++;
		if (_pendingUpdates < StableUpdateCount)
		{
			return;
		}

		_submerged = submerged;
		_pendingUpdates = 0;
		if (!submerged)
		{
			ClearBreathThresholds();
			Report("Mods.Ariadne.Announcements.Surfaced", SoundID.SplashWeak);
			return;
		}

		int percent = BreathPercent(player);
		_nextThresholdPercent = percent <= 0
			? 0
			: (percent - 1) / ThresholdStepPercent * ThresholdStepPercent;
		_announcedEmpty = percent <= 0;
		Report(
			player.honeyWet
				? "Mods.Ariadne.Announcements.SubmergedInHoney"
				: "Mods.Ariadne.Announcements.Underwater",
			SoundID.Splash);
	}

	private void AnnounceRemainingBreath(Player player)
	{
		int percent = BreathPercent(player);
		if (percent <= 0)
		{
			if (!_announcedEmpty)
			{
				_announcedEmpty = true;
				_nextThresholdPercent = 0;
				// Terraria already plays its own drowning sound as breath runs out.
				AriadneMod.ScreenReader.Output(
					Language.GetTextValue("Mods.Ariadne.Announcements.BreathEmpty"));
			}
			return;
		}

		_announcedEmpty = false;
		int crossed = 0;
		while (_nextThresholdPercent >= ThresholdStepPercent && percent <= _nextThresholdPercent)
		{
			crossed = _nextThresholdPercent;
			_nextThresholdPercent -= ThresholdStepPercent;
		}

		if (crossed > 0)
		{
			Report("Mods.Ariadne.Announcements.BreathRemaining", SoundID.SplashWeak, crossed);
		}
	}

	private static int BreathPercent(Player player)
	{
		return player.breathMax <= 0
			? 0
			: Math.Clamp((int)(player.breath * 100L / player.breathMax), 0, 100);
	}

	private static void Report(string key, SoundStyle cue, params object[] arguments)
	{
		SoundEngine.PlaySound(cue);
		AriadneMod.ScreenReader.Output(Language.GetTextValue(key, arguments));
	}

	private void ClearBreathThresholds()
	{
		_nextThresholdPercent = 0;
		_announcedEmpty = false;
	}

	private void ResetTracking()
	{
		_submerged = false;
		_pendingUpdates = 0;
		ClearBreathThresholds();
	}
}
