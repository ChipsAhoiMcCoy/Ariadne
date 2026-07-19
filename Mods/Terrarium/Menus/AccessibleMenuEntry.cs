#nullable enable

using System;

namespace Terrarium.Menus;

internal sealed class AccessibleMenuEntry
{
	internal AccessibleMenuEntry(
		Func<string> label,
		Action? activate = null,
		Action? previousValue = null,
		Action? nextValue = null,
		Func<string>? description = null,
		Func<bool>? enabled = null,
		string role = "button",
		Func<string>? adjustmentAnnouncement = null)
	{
		Label = label;
		Activate = activate;
		PreviousValue = previousValue;
		NextValue = nextValue;
		Description = description;
		Enabled = enabled;
		Role = role;
		AdjustmentAnnouncement = adjustmentAnnouncement;
	}

	internal Func<string> Label { get; }

	internal Action? Activate { get; }

	internal Action? PreviousValue { get; }

	internal Action? NextValue { get; }

	internal Func<string>? Description { get; }

	internal Func<bool>? Enabled { get; }

	internal string Role { get; }

	internal Func<string>? AdjustmentAnnouncement { get; }

	internal bool IsEnabled => Enabled?.Invoke() ?? true;

	internal bool IsAdjustable => PreviousValue is not null || NextValue is not null;
}
