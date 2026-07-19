#nullable enable

using System.Collections.Generic;
using Terraria.Localization;

namespace Terrarium.Menus;

internal sealed class AccessibleCreditsMenuState : AccessibleMenuState
{
	internal AccessibleCreditsMenuState(AccessibleMenuController controller)
		: base(controller)
	{
	}

	protected override string Title => Language.GetTextValue("UI.Credits");

	protected override void BuildEntries(List<AccessibleMenuEntry> entries)
	{
		entries.Add(new(
			() => "Terraria credits",
			() => Announce(
				"Terraria was created by Re-Logic. tModLoader is developed by the tModLoader team and community contributors. " +
				"Terrarium accessibility support is powered by Prism and its screen-reader backends."),
			description: () => "Press Enter to hear the credit summary."));
		entries.Add(new(
			() => "Third-party notices",
			() => Announce("Terrarium includes Prism under the Mozilla Public License 2.0. Complete notices are included with the mod."),
			description: () => "Press Enter to hear the bundled software notice."));
	}
}
