#nullable enable

using Terraria;
using Terraria.ModLoader;
using Ariadne.Audio;
using Ariadne.Configs;

namespace Ariadne.Ingame;

/// <summary>
/// Speaks the hotbar slot the player switches to, which the game otherwise shows
/// only as a highlighted icon. The selected slot's contents are reported as well as
/// the selection itself, so a stack spent down to nothing does not leave the player
/// believing they still hold whatever was last announced.
/// </summary>
[Autoload(Side = ModSide.Client)]
internal sealed class HotbarAnnouncementSystem : ModSystem
{
	private int _announcedSlot;
	private int _announcedItemType;
	private bool _isTracking;

	public override void OnWorldLoad() => ResetTracking();

	public override void OnWorldUnload() => ResetTracking();

	public override void PostUpdatePlayers()
	{
		if (!ModContent.GetInstance<AriadneClientConfig>().HotbarAnnouncementsEnabled)
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

		int slot = player.selectedItem;
		if (slot < 0 || slot >= AccessibleInventoryController.HotbarSlotCount)
		{
			return;
		}

		Item held = player.HeldItem;
		bool changed = _isTracking && (slot != _announcedSlot || held.type != _announcedItemType);
		_announcedSlot = slot;
		_announcedItemType = held.type;
		_isTracking = true;

		// The inventory, chat, and menus narrate their own selection. Keeping the
		// baseline current while they hold focus means closing one does not replay a
		// change the player has already been told about.
		if (changed && GameplayAudioGate.CanListen())
		{
			AriadneMod.ScreenReader.Output(AccessibleInventoryController.DescribeItem(
				AccessibleInventoryController.HotbarSlotName(slot),
				held));
		}
	}

	private void ResetTracking()
	{
		_announcedSlot = 0;
		_announcedItemType = 0;
		_isTracking = false;
	}
}
