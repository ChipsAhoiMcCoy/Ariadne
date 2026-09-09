#nullable enable

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Ariadne.Logic;

internal readonly record struct GridLocation(int Column, int Row);

/// <summary>A held craft never follows focus onto a different recipe or repeats equipment.</summary>
internal sealed class CraftingRepeatLatch
{
	private string? _recipe;
	private bool _crafted;

	internal bool Allow(string recipe, bool pressed, bool triggered, bool stackable)
	{
		if (pressed) { _recipe = recipe; _crafted = false; }
		if (_recipe != recipe) { _recipe = null; return false; }
		if (!triggered || _crafted && !stackable) return false;
		_crafted = true;
		return true;
	}

	internal void Reset() { _recipe = null; _crafted = false; }
}

internal static class InventoryGridLogic
{
	internal static int ColumnCount(int itemCount, int requested) =>
		Math.Clamp(requested, 1, Math.Max(1, itemCount));

	internal static int ColumnLength(int itemCount, int columns, int column)
	{
		int baseLength = itemCount / columns;
		int longer = itemCount % columns;
		return baseLength + (column < longer ? 1 : 0);
	}

	internal static int ColumnStart(int itemCount, int columns, int column)
	{
		int baseLength = itemCount / columns;
		int longer = itemCount % columns;
		return column * baseLength + Math.Min(column, longer);
	}

	internal static GridLocation Locate(int index, int itemCount, int columns)
	{
		index = Math.Clamp(index, 0, Math.Max(0, itemCount - 1));
		for (int column = 0; column < columns; column++)
		{
			int start = ColumnStart(itemCount, columns, column);
			int length = ColumnLength(itemCount, columns, column);
			if (index < start + length) return new(column, index - start);
		}
		return new(columns - 1, ColumnLength(itemCount, columns, columns - 1) - 1);
	}

	internal static int IndexAt(int itemCount, int columns, int column, int row)
	{
		column = Math.Clamp(column, 0, columns - 1);
		row = Math.Clamp(row, 0, ColumnLength(itemCount, columns, column) - 1);
		return ColumnStart(itemCount, columns, column) + row;
	}

	internal static int MoveVerticalWithFooter(int index, int itemCount, int columns, int direction)
	{
		int footer = itemCount;
		GridLocation location = index == footer
			? new(columns - 1, ColumnLength(itemCount, columns, columns - 1))
			: Locate(index, itemCount, columns);
		int length = ColumnLength(itemCount, columns, location.Column) +
			(location.Column == columns - 1 ? 1 : 0);
		int row = (location.Row + direction + length) % length;
		int itemLength = ColumnLength(itemCount, columns, location.Column);
		return location.Column == columns - 1 && row == itemLength
			? footer
			: IndexAt(itemCount, columns, location.Column, row);
	}
}

internal sealed class FixedSnapshotCursor
{
	private int _count;
	private int _index;
	private ulong _lastTick;

	internal bool ShouldRefresh(int count, ulong now, ulong timeoutTicks) =>
		_count == 0 || count == 0 || count != _count || now < _lastTick || now - _lastTick >= timeoutTicks;

	internal void Refresh(int count, ulong now)
	{
		_count = Math.Max(0, count);
		_index = 0;
		_lastTick = now;
	}

	internal int Advance(ulong now)
	{
		_lastTick = now;
		if (_count == 0) return -1;
		int selected = _index;
		_index = (_index + 1) % _count;
		return selected;
	}

	internal void Reset()
	{
		_count = 0;
		_index = 0;
		_lastTick = 0;
	}
}

internal enum HerbGrowthStage
{
	Immature,
	Mature,
	Blooming,
}

internal static class HerbGrowthStageText
{
	internal static string Describe(HerbGrowthStage stage) => stage switch
	{
		HerbGrowthStage.Immature => "immature",
		HerbGrowthStage.Mature => "mature, not blooming",
		_ => "blooming, ready to harvest",
	};
}

internal readonly record struct SemanticBounds(float Left, float Top, float Right, float Bottom)
{
	internal float CenterX => (Left + Right) * 0.5f;
	internal float CenterY => (Top + Bottom) * 0.5f;
}

internal static class SemanticCursorLogic
{
	internal static int FindNearest(IReadOnlyList<SemanticBounds> bounds, float x, float y)
	{
		int best = 0;
		float bestDistance = float.PositiveInfinity;
		for (int index = 0; index < bounds.Count; index++)
		{
			SemanticBounds item = bounds[index];
			float dx = x < item.Left ? item.Left - x : x > item.Right ? x - item.Right : 0f;
			float dy = y < item.Top ? item.Top - y : y > item.Bottom ? y - item.Bottom : 0f;
			float distance = dx * dx + dy * dy;
			if (distance < bestDistance) { bestDistance = distance; best = index; }
		}
		return best;
	}

	internal static int FindDirectional(IReadOnlyList<SemanticBounds> bounds, int current, int directionX, int directionY)
	{
		float originX = bounds[current].CenterX;
		float originY = bounds[current].CenterY;
		int best = current;
		float bestScore = float.PositiveInfinity;
		for (int index = 0; index < bounds.Count; index++)
		{
			if (index == current) continue;
			float dx = bounds[index].CenterX - originX;
			float dy = bounds[index].CenterY - originY;
			float primary = dx * directionX + dy * directionY;
			if (primary <= 0f) continue;
			float perpendicular = MathF.Abs(dx * directionY - dy * directionX);
			float score = primary + perpendicular * 2f + (dx * dx + dy * dy) * 0.001f;
			if (score < bestScore) { bestScore = score; best = index; }
		}
		return best;
	}
}

internal static class TraversalLogic
{
	internal static bool IsImpassable(float requestedMovement, float allowedMovement) =>
		MathF.Abs(allowedMovement) < MathF.Abs(requestedMovement) - 0.01f;

	internal static int LandmarkPriority(int kind) => kind switch { 2 => 3, 1 => 2, _ => 1 };

	internal static bool ShouldSpeakLandmark(int? previousKind, int currentKind, int previousDirection, int currentDirection) =>
		previousKind is null || previousKind.Value != currentKind || currentKind == 2 && previousDirection != currentDirection;

	internal static int ClampLookahead(int tiles) => Math.Clamp(tiles, 1, 12);

	internal static int FindFirstLedge(ReadOnlySpan<bool> reachable, ReadOnlySpan<bool> supported)
	{
		int length = Math.Min(reachable.Length, supported.Length);
		for (int index = 0; index < length; index++)
		{
			if (!reachable[index]) return -1;
			if (!supported[index]) return index;
		}
		return -1;
	}

	/// <returns>0 for no warning, 1 for safe, and 2 for unsafe.</returns>
	internal static int AssessDrop(float dropTiles, bool landingFound, bool hazardous, bool fallDamageSafe)
	{
		if (landingFound && dropTiles < 3f) return 0;
		return landingFound && !hazardous && fallDamageSafe ? 1 : 2;
	}
}

internal enum WorldTransitionMenuAction
{
	None,
	Hold,
	RestoreWorldSelection,
	Clear,
}

internal static class WorldTransitionLogic
{
	private static readonly Regex PercentageToken = new(
		@"\d+(?:[\.,]\d+)?\s*%",
		RegexOptions.Compiled | RegexOptions.CultureInvariant);
	private static readonly Regex EmptyDelimiters = new(
		@"\(\s*\)|\[\s*\]",
		RegexOptions.Compiled | RegexOptions.CultureInvariant);
	private static readonly Regex RepeatedWhitespace = new(
		@"\s+",
		RegexOptions.Compiled | RegexOptions.CultureInvariant);

	internal static string NormalizeStatus(string? status)
	{
		if (string.IsNullOrWhiteSpace(status)) return string.Empty;

		string normalized = PercentageToken.Replace(status, string.Empty);
		normalized = EmptyDelimiters.Replace(normalized, string.Empty);
		normalized = RepeatedWhitespace.Replace(normalized, " ").Trim();
		normalized = normalized.Trim(' ', '-', '\u2013', '\u2014', '|', ':');
		return normalized.Trim();
	}

	internal static WorldTransitionMenuAction MenuAction(
		bool hasPendingTransition,
		bool gameMenu,
		bool generatingWorld,
		int menuMode,
		bool showingWorldLoadScreen)
	{
		if (!hasPendingTransition) return WorldTransitionMenuAction.None;
		if (!gameMenu) return WorldTransitionMenuAction.Clear;
		if (menuMode == 6) return WorldTransitionMenuAction.RestoreWorldSelection;
		if (generatingWorld || menuMode == 10 || showingWorldLoadScreen)
		{
			return WorldTransitionMenuAction.Hold;
		}
		return WorldTransitionMenuAction.Clear;
	}
}
