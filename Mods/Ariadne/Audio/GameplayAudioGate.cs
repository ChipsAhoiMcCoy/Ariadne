#nullable enable

using Terraria;
using Terraria.Audio;
using Terraria.GameInput;

namespace Ariadne.Audio;

/// <summary>
/// Defines the unobstructed gameplay state in which continuous accessibility audio may run.
/// Keeping this gate shared ensures every stream clears queued audio at the same boundaries.
/// </summary>
internal static class GameplayAudioGate
{
	internal static bool CanListen()
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
}
