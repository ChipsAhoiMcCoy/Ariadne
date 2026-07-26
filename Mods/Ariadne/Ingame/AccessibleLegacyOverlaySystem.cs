#nullable enable

using System;
using Terraria;
using Terraria.ModLoader;

namespace Ariadne.Ingame;

/// <summary>
/// Announces legacy overlays which are drawn directly by Main instead of a UIState.
/// </summary>
[Autoload(Side = ModSide.Client)]
internal sealed class AccessibleLegacyOverlaySystem : ModSystem
{
	/// <summary>Longest per-second countdown, so a 60 second revive stays bearable.</summary>
	private const int MaxCountdownStart = 10;

	private bool _wasDead;
	private int _lastRespawnSecond = -1;
	private int _respawnTotalTicks;

	public override void OnWorldLoad()
	{
		Reset();
	}

	public override void OnWorldUnload()
	{
		Reset();
	}

	public override void PostUpdateEverything()
	{
		if (Main.gameMenu || Main.dedServ)
		{
			Reset();
			return;
		}

		MonitorDeathScreen();
	}

	private void MonitorDeathScreen()
	{
		Player player = Main.LocalPlayer;
		if (player.dead && !_wasDead)
		{
			_wasDead = true;
			_respawnTotalTicks = player.respawnTimer;
			_lastRespawnSecond = GetRespawnSeconds(player);
			string coins = player.lostCoins > 0 ? $" Dropped coins: {player.lostCoinString}." : string.Empty;
			AriadneMod.ScreenReader.Output($"You died.{coins} Respawning in {_lastRespawnSecond} seconds.");
			return;
		}
		if (!player.dead && _wasDead)
		{
			Reset();
			AriadneMod.ScreenReader.Output("Respawned.");
			return;
		}
		if (!player.dead)
		{
			return;
		}

		// A server can revise respawnTimer after the death edge through MessageID 12,
		// and that packet can even be what marks this player dead, so the countdown
		// length tracks the longest timer seen rather than the first one.
		_respawnTotalTicks = Math.Max(_respawnTotalTicks, player.respawnTimer);

		int seconds = GetRespawnSeconds(player);
		if (seconds == _lastRespawnSecond)
		{
			return;
		}

		_lastRespawnSecond = seconds;
		if (seconds > 0 && seconds <= GetCountdownStart())
		{
			AriadneMod.ScreenReader.Output($"Respawning in {seconds} seconds.", interrupt: false);
		}
	}

	/// <summary>
	/// Half the total wait, so the usual ten second revive counts from five and a
	/// longer boss or expert revive starts proportionally earlier.
	/// </summary>
	private int GetCountdownStart()
	{
		int total = Math.Max(0, (int)MathF.Ceiling(_respawnTotalTicks / 60f));
		return Math.Clamp(total / 2, 1, MaxCountdownStart);
	}

	// Vanilla's death screen draws respawnTimer / 60 + 1 so its counter never rests
	// on zero, which makes the first value land for a single frame. Speech needs a
	// number that holds for its full second instead, so round up.
	private static int GetRespawnSeconds(Player player)
	{
		return Math.Max(0, (int)MathF.Ceiling(player.respawnTimer / 60f));
	}

	private void Reset()
	{
		_wasDead = false;
		_lastRespawnSecond = -1;
		_respawnTotalTicks = 0;
	}
}
