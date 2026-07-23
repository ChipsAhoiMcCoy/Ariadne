#nullable enable

using Terraria;
using Terraria.GameContent.Creative;
using Terraria.GameContent.UI;
using Terraria.GameInput;
using Terraria.Graphics.Capture;

namespace Terrarium.Ingame.Controls;

internal static class WorldInputContext
{
	internal static bool CanOwnWorldCursor()
	{
		if (Main.dedServ ||
			Main.gameMenu ||
			!Main.hasFocus ||
			Main.gamePaused ||
			Main.blockInput ||
			Main.playerInventory ||
			Main.drawingPlayerChat ||
			Main.editSign ||
			Main.editChest ||
			Main.mapFullscreen ||
			Main.ingameOptionsWindow ||
			Main.inFancyUI ||
			Main.InGameUI.CurrentState is not null ||
			PlayerInput.WritingText ||
			WiresUI.Open ||
			CaptureManager.Instance.Active)
		{
			return false;
		}

		Player player = Main.LocalPlayer;
		if (!player.active ||
			player.dead ||
			player.ghost ||
			player.talkNPC >= 0 ||
			player.sign >= 0)
		{
			return false;
		}

		if (Main.CreativeMenu.Enabled && !Main.CreativeMenu.Blocked)
		{
			return false;
		}

		// Housing placement deliberately retains Terraria's physical world cursor
		// until it receives its own semantic placement workflow.
		return Main.instance.mouseNPCType < 0;
	}
}
