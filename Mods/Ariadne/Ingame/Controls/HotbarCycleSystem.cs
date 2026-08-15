#nullable enable

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.Audio;
using Terraria.GameInput;
using Terraria.ID;
using Terraria.ModLoader;
using Ariadne.Audio;
using Ariadne.Configs;

namespace Ariadne.Ingame.Controls;

/// <summary>
/// Steps the selected hotbar slot with the Ariadne modifier held.
///
/// The number row selects a slot outright, which is fine when the listener knows what is
/// in slot seven and useless when they do not. Stepping is how a slot is found rather than
/// recalled, and it puts the whole hotbar on two keys next to the movement hand instead of
/// ten keys the fingers have to leave the keyboard to reach.
///
/// Nothing here speaks, normally. Changing the selection is enough on its own, because
/// <see cref="HotbarAnnouncementSystem"/> already watches for exactly that and runs later
/// in the same tick. The native hotbar selection tick is played here because this path
/// bypasses Terraria's number-key selection handler.
/// </summary>
[Autoload(Side = ModSide.Client)]
internal sealed class HotbarCycleSystem : ModSystem
{
	public override void PostUpdateInput()
	{
		// Reading a mod keybind before PlayerInput has taken the mod's triggers into its
		// key status throws, and the exception costs every system that runs after this one
		// in the same update. The world gate below would catch it, but only after the read.
		if (Main.gameMenu)
		{
			return;
		}

		if (AriadneMod.CombatTargetModifierKeybind?.Current != true ||
			!WorldInputContext.IsUnobstructedGameplay())
		{
			return;
		}

		// Read before anything is cleared. The suppression below exempts mod keybinds, but
		// the order is kept honest anyway: a chord that consumed its own trigger before
		// looking at it is exactly the failure this pair of lines is arranged to prevent.
		int step = 0;
		if (AriadneMod.HotbarPreviousKeybind?.JustPressed == true)
		{
			step = -1;
		}
		else if (AriadneMod.HotbarNextKeybind?.JustPressed == true)
		{
			step = 1;
		}

		// Applied while the chord is held rather than only on its press edge, because a
		// native binding underneath it goes on acting for as long as the key is down.
		KeyboardState keyboard = Keyboard.GetState();
		SuppressNativeBindings(keyboard, AriadneMod.HotbarPreviousKeybind);
		SuppressNativeBindings(keyboard, AriadneMod.HotbarNextKeybind);

		if (step != 0)
		{
			Select(step);
		}
	}

	/// <summary>
	/// Moves the selection one slot, wrapping at either end. Empty slots are stepped
	/// through rather than skipped: holding nothing is a real choice, and skipping would
	/// make the hotbar a different length every time something ran out.
	/// </summary>
	private static void Select(int step)
	{
		Player player = Main.LocalPlayer;
		int slotCount = AccessibleInventoryController.HotbarSlotCount;
		int slot = player.selectedItem;
		if (slot < 0 || slot >= slotCount)
		{
			slot = 0;
		}

		int nextSlot = (slot + step + slotCount) % slotCount;
		if (player.selectedItem == nextSlot)
		{
			return;
		}

		player.selectedItem = nextSlot;
		SoundEngine.PlaySound(SoundID.MenuTick);

		// A deliberate press deserves an answer even when the passive announcement is
		// switched off. The two paths are exclusive on the same setting, so the slot is
		// never spoken twice.
		if (!ModContent.GetInstance<AriadneClientConfig>().HotbarAnnouncementsEnabled &&
			GameplayAudioGate.CanListen())
		{
			AriadneMod.ScreenReader.Output(AccessibleInventoryController.DescribeItem(
				AccessibleInventoryController.HotbarSlotName(player.selectedItem),
				player.HeldItem));
		}
	}

	/// <summary>
	/// Withholds whatever Terraria has bound to this chord's key. The modifier means
	/// nothing to the game, so the vanilla binding fires alongside the chord: E is
	/// Grapple in every stock profile, and without this the hook would go out on every
	/// step forward through the hotbar. Keys are read from the binding rather than named
	/// here so a rebind stays covered, and only Terraria's own triggers are cleared:
	/// Ariadne's share the same table, and taking out this chord's own binding was what
	/// stopped it working at all.
	/// </summary>
	private static void SuppressNativeBindings(KeyboardState keyboard, ModKeybind? keybind)
	{
		if (keybind is null)
		{
			return;
		}

		List<string> assigned = keybind.GetAssignedKeys(InputMode.Keyboard);
		foreach (string name in assigned)
		{
			if (Enum.TryParse(name, out Keys key) && keyboard.IsKeyDown(key))
			{
				AccessibleInputSuppression.ConsumeNativeTriggersBoundTo(key);
			}
		}
	}
}
