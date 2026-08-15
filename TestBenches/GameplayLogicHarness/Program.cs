#nullable enable

using Microsoft.Xna.Framework.Input;
using Ariadne.Accessibility;
using Ariadne.Logic;

namespace Ariadne.TestBenches.GameplayLogic;

internal static class Program
{
	private static int _failures;

	private static int Main()
	{
		InventoryColumnsAndTrashFooter();
		TraversalAndWallClassification();
		LedgeLookaheadAndSafety();
		LandmarkPriorityAndSpeech();
		RadarSnapshotCycling();
		HousingNavigation();
		HerbsAndKeyNames();
		WorldTransitionStatusAndMenuLifecycle();

		Console.WriteLine();
		Console.WriteLine(_failures == 0 ? "All checks passed." : $"{_failures} check(s) FAILED.");
		return _failures == 0 ? 0 : 1;
	}

	private static void InventoryColumnsAndTrashFooter()
	{
		bool passed = true;
		for (int columns = 1; columns <= 10; columns++)
		{
			int actual = InventoryGridLogic.ColumnCount(40, columns);
			int total = Enumerable.Range(0, actual).Sum(column => InventoryGridLogic.ColumnLength(40, actual, column));
			passed &= total == 40;
			int finalStart = InventoryGridLogic.ColumnStart(40, actual, actual - 1);
			passed &= InventoryGridLogic.MoveVerticalWithFooter(39, 40, actual, 1) == 40;
			passed &= InventoryGridLogic.MoveVerticalWithFooter(40, 40, actual, 1) == finalStart;
			passed &= InventoryGridLogic.MoveVerticalWithFooter(finalStart, 40, actual, -1) == 40;
			passed &= InventoryGridLogic.MoveVerticalWithFooter(40, 40, actual, -1) == 39;
		}
		Report("Inventory 1-10 columns keep 40 items and append a reversible Trash footer", passed);
	}

	private static void TraversalAndWallClassification()
	{
		bool passed =
			TraversalLogic.IsImpassable(2f, 0f) &&
			TraversalLogic.IsImpassable(2f, 1f) &&
			!TraversalLogic.IsImpassable(2f, 2f);
		Report("Full, head, and torso blocks stop the path while steps, stairs, and hills remain traversable", passed);
	}

	private static void LedgeLookaheadAndSafety()
	{
		bool[] reachable = [true, true, true, true];
		bool[] supported = [true, true, false, false];
		bool passed =
			TraversalLogic.ClampLookahead(0) == 1 &&
			TraversalLogic.ClampLookahead(20) == 12 &&
			TraversalLogic.FindFirstLedge(reachable, supported) == 2 &&
			TraversalLogic.FindFirstLedge([true, false, true], [true, false, false]) == -1 &&
			TraversalLogic.AssessDrop(2.99f, true, false, true) == 0 &&
			TraversalLogic.AssessDrop(3f, true, false, true) == 1 &&
			TraversalLogic.AssessDrop(3f, true, true, true) == 2 &&
			TraversalLogic.AssessDrop(30f, true, false, false) == 2 &&
			TraversalLogic.AssessDrop(30f, false, false, true) == 2;
		Report("Ledge lookahead stops at walls and classifies shallow, safe, hazardous, damaging, and bottomless drops", passed);
	}

	private static void LandmarkPriorityAndSpeech()
	{
		bool passed =
			TraversalLogic.LandmarkPriority(2) > TraversalLogic.LandmarkPriority(1) &&
			TraversalLogic.LandmarkPriority(1) > TraversalLogic.LandmarkPriority(0) &&
			TraversalLogic.ShouldSpeakLandmark(null, 0, 0, 0) &&
			!TraversalLogic.ShouldSpeakLandmark(0, 0, 0, 0) &&
			TraversalLogic.ShouldSpeakLandmark(0, 1, 0, 0) &&
			TraversalLogic.ShouldSpeakLandmark(2, 2, 0, 1);
		Report("Landmarks prefer rope then track then platform and speech re-arms only on run changes", passed);
	}

	private static void RadarSnapshotCycling()
	{
		FixedSnapshotCursor cursor = new();
		bool passed = cursor.ShouldRefresh(3, 0, 240);
		cursor.Refresh(3, 0);
		passed &= cursor.Advance(1) == 0;
		passed &= cursor.Advance(2) == 1;
		passed &= cursor.Advance(3) == 2;
		passed &= cursor.Advance(4) == 0;
		passed &= !cursor.ShouldRefresh(3, 243, 240);
		passed &= cursor.ShouldRefresh(3, 244, 240);
		Report("Manual radar snapshot advances one contact, wraps, and resets after four seconds", passed);
	}

	private static void HousingNavigation()
	{
		SemanticBounds[] rooms =
		[
			new(0, 0, 10, 10),
			new(20, 0, 30, 10),
			new(0, 20, 10, 30),
		];
		bool passed =
			SemanticCursorLogic.FindNearest(rooms, 5, 5) == 0 &&
			SemanticCursorLogic.FindDirectional(rooms, 0, 1, 0) == 1 &&
			SemanticCursorLogic.FindDirectional(rooms, 0, 0, 1) == 2 &&
			SemanticCursorLogic.FindDirectional(rooms, 0, -1, 0) == 0;
		Report("Housing cursor starts within the nearest bounds and moves semantically by direction", passed);
	}

	private static void HerbsAndKeyNames()
	{
		bool passed =
			HerbGrowthStageText.Describe(HerbGrowthStage.Immature) == "immature" &&
			HerbGrowthStageText.Describe(HerbGrowthStage.Mature) == "mature, not blooming" &&
			HerbGrowthStageText.Describe(HerbGrowthStage.Blooming) == "blooming, ready to harvest" &&
			SpokenKeyName.Format(Keys.OemSemicolon) == "semicolon" &&
			SpokenKeyName.Format(Keys.OemQuotes) == "apostrophe" &&
			SpokenKeyName.Format(Keys.Back) == "Backspace" &&
			SpokenKeyName.Format(Keys.Enter) == "Enter";
		Report("Herb stages and raw XNA keyboard names are humanized", passed);
	}

	private static void WorldTransitionStatusAndMenuLifecycle()
	{
		bool passed =
			WorldTransitionLogic.NormalizeStatus("Loading liquids 5%") == "Loading liquids" &&
			WorldTransitionLogic.NormalizeStatus("Loading liquids 6%") == "Loading liquids" &&
			WorldTransitionLogic.NormalizeStatus("0.0% - Loading liquids - 42.5%") == "Loading liquids" &&
			WorldTransitionLogic.NormalizeStatus("Loading map: (42%)") == "Loading map" &&
			WorldTransitionLogic.NormalizeStatus("Generating ocean sand...") == "Generating ocean sand..." &&
			WorldTransitionLogic.MenuAction(true, true, true, 888, true) == WorldTransitionMenuAction.Hold &&
			WorldTransitionLogic.MenuAction(true, true, true, 6, true) == WorldTransitionMenuAction.RestoreWorldSelection &&
			WorldTransitionLogic.MenuAction(true, false, false, 10, false) == WorldTransitionMenuAction.Clear &&
			WorldTransitionLogic.MenuAction(false, true, false, 6, false) == WorldTransitionMenuAction.None;
		Report("World transitions deduplicate percentages and restore the accessible world list after generation", passed);
	}

	private static void Report(string name, bool passed)
	{
		if (!passed) _failures++;
		Console.WriteLine($"{(passed ? "pass" : "FAIL")}  {name}");
	}
}
