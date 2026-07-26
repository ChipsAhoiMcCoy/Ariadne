#nullable enable

using System.Collections.Immutable;
using Microsoft.Xna.Framework;

namespace Ariadne.Ingame.Scanner;

internal enum ScannerCategoryKind
{
	OresAndValuables,
	Liquids,
	Npcs,
	Enemies,
	PassiveCreatures,
	DroppedItems,
	Containers,
	TreesAndLargePlants,
	PlacedObjects,
}

internal enum ScannerTargetKind
{
	Resource,
	Liquid,
	Npc,
	Enemy,
	PassiveCreature,
	DroppedItem,
	Container,
	Tree,
	PlacedObject,
}

internal enum ScannerInteractionKind
{
	None,
	TalkToNpc,
	PickupItem,
	RightClickTile,
}

internal readonly record struct ScannerTileReference(int X, int Y, ushort TileType, int LiquidType = 0);

internal sealed record ScannerTarget(
	string Id,
	ScannerTargetKind Kind,
	ScannerInteractionKind Interaction,
	string Name,
	Rectangle WorldBounds,
	Vector2 WorldPosition,
	ImmutableArray<ScannerTileReference> Tiles,
	int VisibleTileCount = 0,
	int EntityIndex = -1,
	int EntityType = 0,
	long EntityIdentity = 0,
	int EntityAge = 0,
	Vector2 EntitySnapshotPosition = default,
	int Health = 0,
	int MaxHealth = 0,
	int Stack = 0,
	bool IsBoss = false);

internal sealed record ScannerCategory(
	ScannerCategoryKind Kind,
	string Name,
	ImmutableArray<ScannerTarget> Targets);

internal sealed record ScannerSnapshot(
	Rectangle Viewport,
	Vector2 PlayerPosition,
	ImmutableArray<ScannerCategory> Categories)
{
	internal int TargetCount
	{
		get
		{
			int count = 0;
			foreach (ScannerCategory category in Categories)
			{
				count += category.Targets.Length;
			}
			return count;
		}
	}
}
