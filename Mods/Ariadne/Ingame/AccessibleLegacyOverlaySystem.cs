#nullable enable

using System;
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.ModLoader;

namespace Ariadne.Ingame;

/// <summary>
/// Announces legacy overlays which are drawn directly by Main instead of a UIState.
/// </summary>
[Autoload(Side = ModSide.Client)]
internal sealed class AccessibleLegacyOverlaySystem : ModSystem
{
	private bool _wasChatOpen;
	private bool _wasDead;
	private string _previousChatText = string.Empty;
	private KeyboardState _previousKeyboard;
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

		KeyboardState keyboard = Keyboard.GetState();
		MonitorChat(keyboard);
		MonitorDeathScreen();
		_previousKeyboard = keyboard;
	}

	private void MonitorChat(KeyboardState keyboard)
	{
		if (Main.drawingPlayerChat && !_wasChatOpen)
		{
			_wasChatOpen = true;
			_previousChatText = Main.chatText;
			AriadneMod.ScreenReader.Output(
				"Chat edit field. Type a message and press Enter to send, or Escape to cancel. Up and Down scroll visible chat. F1 reads chat help.");
		}
		else if (!Main.drawingPlayerChat && _wasChatOpen)
		{
			_wasChatOpen = false;
			_previousChatText = string.Empty;
			AriadneMod.ScreenReader.Output("Chat closed.");
			return;
		}

		if (!Main.drawingPlayerChat)
		{
			return;
		}

		if (Pressed(keyboard, Keys.F1))
		{
			AriadneMod.ScreenReader.Output(
				"Chat help. Type a message normally. Enter sends the current message. Escape closes chat without sending. Up and Down scroll through visible chat messages. Text changes are announced as you edit.");
		}

		string value = Main.chatText;
		if (value == _previousChatText)
		{
			return;
		}

		string oldValue = _previousChatText;
		_previousChatText = value;
		if (value.Length == oldValue.Length + 1 && value.StartsWith(oldValue, StringComparison.Ordinal))
		{
			AriadneMod.ScreenReader.Output(value[^1].ToString(), interrupt: false);
		}
		else if (oldValue.Length == value.Length + 1 && oldValue.StartsWith(value, StringComparison.Ordinal))
		{
			AriadneMod.ScreenReader.Output($"Deleted {oldValue[^1]}", interrupt: false);
		}
		else
		{
			AriadneMod.ScreenReader.Output(string.IsNullOrEmpty(value) ? "Empty" : value, interrupt: false);
		}
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

	private bool Pressed(KeyboardState keyboard, Keys key)
	{
		return keyboard.IsKeyDown(key) && _previousKeyboard.IsKeyUp(key);
	}

	private static int GetRespawnSeconds(Player player)
	{
		return Math.Max(0, (int)(1f + player.respawnTimer / 60f));
	}

	private void Reset()
	{
		_wasChatOpen = false;
		_wasDead = false;
		_previousChatText = string.Empty;
		_previousKeyboard = Keyboard.GetState();
		_lastRespawnSecond = -1;
	}
}
