#nullable enable

using System;
using System.Collections.Generic;
using System.Reflection;
using Terraria;
using Terraria.Localization;
using Terraria.ModLoader;
using Ariadne.SoundGuide;

namespace Ariadne.Menus;

internal sealed class AccessibleMainMenuState : AccessibleMenuState
{
	private static readonly MethodInfo? PrepareSinglePlayer = typeof(Main).GetMethod(
		"PrepareLoadedModsAndConfigsForSingleplayer",
		BindingFlags.NonPublic | BindingFlags.Static);

	internal AccessibleMainMenuState(AccessibleMenuController controller)
		: base(controller)
	{
	}

	protected override string Title => "Ariadne Main Menu";

	protected override bool CanGoBack => false;

	protected override void BuildEntries(List<AccessibleMenuEntry> entries)
	{
		entries.Add(new(() => Lang.menu[12].Value, OpenSinglePlayer));
		entries.Add(new(() => Lang.menu[13].Value, () => Controller.Navigate(new AccessibleMultiplayerMenuState(Controller))));
		entries.Add(new(() => Lang.menu[131].Value, () => Controller.Navigate(new AccessibleAchievementsMenuState(Controller))));
		entries.Add(new(() => Language.GetTextValue("UI.Workshop"), () => Controller.Navigate(new AccessibleWorkshopMenuState(Controller))));
		entries.Add(new(() => Lang.menu[14].Value, () => Controller.Navigate(new AccessibleSettingsMenuState(Controller))));
		entries.Add(new(
			() => Language.GetTextValue("Mods.Ariadne.SoundGuide.Title"),
			() => Controller.Navigate(new SoundGuideMenuState(Controller)),
			description: () => Language.GetTextValue("Mods.Ariadne.SoundGuide.Summary")));
		entries.Add(new(() => Language.GetTextValue("UI.Credits"), () => Controller.Navigate(new AccessibleCreditsMenuState(Controller))));
		entries.Add(new(() => Lang.menu[15].Value, Main.WeGameRequireExitGame));
	}

	private void OpenSinglePlayer()
	{
		if (PrepareSinglePlayer is null)
		{
			Announce("Single Player is unavailable because this tModLoader version changed its startup API.");
			return;
		}

		Main.ClearPendingPlayerSelectCallbacks();
		Main.menuMultiplayer = false;
		Main.menuServer = false;
		Main.menuMode = 1;
		try
		{
			// tModLoader exposes no public equivalent. This performs its required
			// pending-mod and config consistency check before player selection.
			PrepareSinglePlayer.Invoke(null, null);
			if (Main.menuMode == 1 && Main.MenuUI.CurrentState == this)
			{
				Controller.Navigate(new AccessiblePlayerSelectMenuState(Controller, MultiplayerIntent.SinglePlayer));
			}
		}
		catch (Exception exception)
		{
			Main.menuMode = 888;
			Announce("Single Player could not be opened. See the tModLoader client log.");
			ModContent.GetInstance<AriadneMod>().Logger.Error("Single Player startup failed.", exception);
		}
	}
}
