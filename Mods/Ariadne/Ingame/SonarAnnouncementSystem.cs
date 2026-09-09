#nullable enable

using Terraria;
using Terraria.ModLoader;

namespace Ariadne.Ingame;

[Autoload(Side = ModSide.Client)]
internal sealed class SonarAnnouncementSystem : ModSystem
{
	public override void Load() => On_PopupText.AssignAsSonarText += AnnounceCatch;
	public override void Unload() => On_PopupText.AssignAsSonarText -= AnnounceCatch;

	private static void AnnounceCatch(On_PopupText.orig_AssignAsSonarText orig, int index)
	{
		orig(index);
		if (Main.gameMenu || !Main.LocalPlayer.sonarPotion || index < 0 || index >= Main.popupText.Length)
			return;
		string name = Main.popupText[index].name;
		if (!string.IsNullOrWhiteSpace(name)) AriadneMod.ScreenReader.Output(name);
	}
}
