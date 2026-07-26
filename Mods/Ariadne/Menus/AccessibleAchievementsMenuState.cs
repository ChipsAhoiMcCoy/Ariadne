#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Terraria;
using Terraria.Achievements;
using Terraria.Localization;

namespace Ariadne.Menus;

internal sealed class AccessibleAchievementsMenuState : AccessibleMenuState
{
	private enum AchievementFilter
	{
		All,
		Incomplete,
		Completed,
	}

	private AchievementFilter _filter;
	private string _search = string.Empty;

	internal AccessibleAchievementsMenuState(AccessibleMenuController controller)
		: base(controller)
	{
	}

	protected override string Title => Language.GetTextValue("UI.Achievements");

	protected override void BuildEntries(List<AccessibleMenuEntry> entries)
	{
		entries.Add(new(
			() => $"Filter: {_filter}",
			NextFilter,
			previousValue: PreviousFilter,
			nextValue: NextFilter,
			role: "choice"));
		entries.Add(new(
			() => $"Search: {(string.IsNullOrWhiteSpace(_search) ? "none" : _search)}",
			EditSearch,
			role: "edit field"));
		entries.Add(new(
			() => Language.GetTextValue("tModLoader.AchievementsReset"),
			ConfirmReset,
			description: () => "Permanently clear all achievement progress."));

		IEnumerable<Achievement> achievements = Main.Achievements.CreateAchievementsList()
			.Where(achievement => !achievement.Hidden || achievement.IsCompleted)
			.Where(MatchesFilter)
			.Where(MatchesSearch)
			.OrderBy(achievement => achievement.IsCompleted)
			.ThenBy(achievement => achievement.FriendlyName.Value, StringComparer.CurrentCultureIgnoreCase);

		foreach (Achievement achievement in achievements)
		{
			Achievement capturedAchievement = achievement;
			entries.Add(new(
				() => $"{capturedAchievement.FriendlyName.Value}, {(capturedAchievement.IsCompleted ? "completed" : "incomplete")}",
				() => Announce(DescribeAchievement(capturedAchievement)),
				description: () => capturedAchievement.Description.Value,
				role: "achievement"));
		}
	}

	private void NextFilter() => _filter = (AchievementFilter)(((int)_filter + 1) % 3);

	private void PreviousFilter() => _filter = (AchievementFilter)(((int)_filter + 2) % 3);

	private void EditSearch()
	{
		Controller.Navigate(new AccessibleTextInputState(
			Controller,
			Language.GetTextValue("tModLoader.ModsTypeToSearch"),
			_search,
			80,
			value => { _search = value; Controller.Back(); }));
	}

	private bool MatchesFilter(Achievement achievement)
	{
		return _filter switch
		{
			AchievementFilter.Incomplete => !achievement.IsCompleted,
			AchievementFilter.Completed => achievement.IsCompleted,
			_ => true,
		};
	}

	private void ConfirmReset()
	{
		Controller.Navigate(new AccessibleConfirmationMenuState(
			Controller,
			Language.GetTextValue("tModLoader.AchievementsResetConfirm"),
			Language.GetTextValue("tModLoader.AchievementsResetConfirmTooltip"),
			ResetAchievements));
	}

	private void ResetAchievements()
	{
		Main.Achievements.ClearAll();
		Announce("Achievement progress was reset.");
		Controller.Back();
	}

	private bool MatchesSearch(Achievement achievement)
	{
		return string.IsNullOrWhiteSpace(_search) ||
			achievement.FriendlyName.Value.Contains(_search, StringComparison.CurrentCultureIgnoreCase) ||
			achievement.Description.Value.Contains(_search, StringComparison.CurrentCultureIgnoreCase);
	}

	private static string DescribeAchievement(Achievement achievement)
	{
		string state = achievement.IsCompleted ? "Completed" : "Incomplete";
		string category = achievement.Category.ToString();
		string progress = TrackerProgress(achievement);
		return $"{achievement.FriendlyName.Value}. {state}. Category: {category}. {achievement.Description.Value}{progress}";
	}

	private static string TrackerProgress(Achievement achievement)
	{
		if (!achievement.HasTracker)
		{
			return string.Empty;
		}

		IAchievementTracker tracker = achievement.GetTracker();
		return tracker.GetTrackerType() switch
		{
			TrackerType.Int when tracker is AchievementTracker<int> integer => $" Progress: {integer.Value} of {integer.MaxValue}.",
			TrackerType.Float when tracker is AchievementTracker<float> number => $" Progress: {number.Value:0.##} of {number.MaxValue:0.##}.",
			_ => string.Empty,
		};
	}
}
