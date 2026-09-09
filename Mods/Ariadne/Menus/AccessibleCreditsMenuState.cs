#nullable enable

using System.Collections.Generic;
using Terraria.Localization;

namespace Ariadne.Menus;

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
				"Ariadne is developed by ChipsAhoyMcCoy. Speech and braille support use Prism 0.17.3 by ethindp and the Prism contributors, and its screen-reader backends."),
			description: () => "Press Enter to hear the credit summary."));
		entries.Add(new(
			() => "Third-party notices",
			() => Announce("Ariadne includes the unchanged Prism 0.17.3 Windows runtime under the Mozilla Public License 2.0. " +
				"Corresponding source is available under that license at github.com slash ethindp slash prism, tag v0.17.3. " +
				"Complete credits, source links, and dependency licenses are packaged in ThirdParty and linked from the Ariadne release page."),
			description: () => "Press Enter to hear the bundled software notice."));
	}
}
