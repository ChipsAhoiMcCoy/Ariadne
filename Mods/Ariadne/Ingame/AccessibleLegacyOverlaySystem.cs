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
	private bool _wasDead;
	private int _lastRespawnSecond = -1;

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
			_lastRespawnSecond = GetRespawnSeconds(player);
			string coins = player.lostCoins > 0 ? $" Dropped coins: {player.lostCoinString}." : string.Empty;
			AriadneMod.ScreenReader.Output($"You died.{coins} Respawning in {_lastRespawnSecond} seconds.");
			return;
		}
		if (!player.dead && _wasDead)
		{
			_wasDead = false;
			_lastRespawnSecond = -1;
			AriadneMod.ScreenReader.Output("Respawned.");
			return;
		}
		if (!player.dead)
		{
			return;
		}

		int seconds = GetRespawnSeconds(player);
		if (seconds == _lastRespawnSecond)
		{
			return;
		}

		_lastRespawnSecond = seconds;
		if (seconds > 0 && (seconds <= 5 || seconds % 5 == 0))
		{
			AriadneMod.ScreenReader.Output($"Respawning in {seconds} seconds.", interrupt: false);
		}
	}

	private static int GetRespawnSeconds(Player player)
	{
		return Math.Max(0, (int)(1f + player.respawnTimer / 60f));
	}

	private void Reset()
	{
		_wasDead = false;
		_lastRespawnSecond = -1;
	}
}
