#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.Map;
using Terraria.ModLoader;
using Terraria.ObjectData;

namespace Ariadne.Ingame.Scanner;

internal static class ScannerSnapshotBuilder
{
	private readonly record struct ResourceTile(int X, int Y, ushort Type, string Name);
	private readonly record struct LiquidTile(int X, int Y, int LiquidType);
	private readonly record struct TreeTile(int X, int Y, ushort Type, string Name);
	private sealed record ObjectCandidate(Point16 Root, ushort Type, string Name, Rectangle Bounds, ScannerTargetKind Kind);

	private static readonly (int X, int Y)[] EightDirections =
	[
		(-1, -1), (0, -1), (1, -1),
		(-1, 0),           (1, 0),
		(-1, 1),  (0, 1),  (1, 1),
	];

	private static readonly (int X, int Y)[] FourDirections =
	[
		(0, -1), (-1, 0), (1, 0), (0, 1),
	];

	/// <summary>Every category, for callers that want the whole picture.</summary>
	internal static readonly IReadOnlySet<ScannerCategoryKind> AllCategories =
		new HashSet<ScannerCategoryKind>(Enum.GetValues<ScannerCategoryKind>());

	/// <summary>The lit contents of the visible screen, in full.</summary>
	internal static ScannerSnapshot Capture() => Capture(GetViewport(), AllCategories);

	/// <summary>
	/// The lit contents of an arbitrary world rectangle, narrowed to the categories asked
	/// for. The radar sweeps a range of its own rather than the camera, and wants only the
	/// few categories it is armed with, so skipping the rest keeps a twice-a-second sweep
	/// from paying for eight passes it would throw away.
	/// </summary>
	internal static ScannerSnapshot Capture(
		Rectangle worldBounds,
		IReadOnlySet<ScannerCategoryKind> categories)
	{
		Vector2 playerPosition = Main.LocalPlayer.Center;
		Dictionary<ScannerCategoryKind, List<ScannerTarget>> targets = [];
		foreach (ScannerCategoryKind kind in Enum.GetValues<ScannerCategoryKind>())
		{
			targets[kind] = [];
		}

		ScanTiles(worldBounds, categories, targets);
		ScanNpcs(worldBounds, categories, targets);
		ScanItems(worldBounds, categories, targets);

		List<ScannerCategory> found = [];
		foreach ((ScannerCategoryKind kind, List<ScannerTarget> categoryTargets) in targets)
		{
			if (categoryTargets.Count == 0)
			{
				continue;
			}

			categoryTargets.Sort((left, right) => CompareTargets(left, right, playerPosition));
			found.Add(new ScannerCategory(kind, CategoryName(kind), categoryTargets.ToImmutableArray()));
		}

		found.Sort((left, right) =>
		{
			float leftDistance = DistanceSquared(left.Targets[0].WorldPosition, playerPosition);
			float rightDistance = DistanceSquared(right.Targets[0].WorldPosition, playerPosition);
			int distanceComparison = leftDistance.CompareTo(rightDistance);
			return distanceComparison != 0
				? distanceComparison
				: StringComparer.OrdinalIgnoreCase.Compare(left.Name, right.Name);
		});

		return new ScannerSnapshot(worldBounds, playerPosition, found.ToImmutableArray());
	}

	private static Rectangle GetViewport()
	{
		Vector2 position = Main.Camera.ScaledPosition;
		Vector2 size = Main.Camera.ScaledSize;
		return new Rectangle(
			(int)MathF.Floor(position.X),
			(int)MathF.Floor(position.Y),
			Math.Max(1, (int)MathF.Ceiling(size.X)),
			Math.Max(1, (int)MathF.Ceiling(size.Y)));
	}

	private static void ScanTiles(
		Rectangle viewport,
		IReadOnlySet<ScannerCategoryKind> categories,
		Dictionary<ScannerCategoryKind, List<ScannerTarget>> targets)
	{
		bool wantResources = categories.Contains(ScannerCategoryKind.OresAndValuables);
		bool wantLiquids = categories.Contains(ScannerCategoryKind.Liquids);
		bool wantTrees = categories.Contains(ScannerCategoryKind.TreesAndLargePlants);
		bool wantContainers = categories.Contains(ScannerCategoryKind.Containers);
		bool wantObjects = categories.Contains(ScannerCategoryKind.PlacedObjects);
		if (!wantResources && !wantLiquids && !wantTrees && !wantContainers && !wantObjects)
		{
			return;
		}

		int firstX = Math.Clamp((int)MathF.Floor(viewport.Left / 16f), 1, Main.maxTilesX - 2);
		int lastX = Math.Clamp((int)MathF.Ceiling(viewport.Right / 16f) - 1, 1, Main.maxTilesX - 2);
		int firstY = Math.Clamp((int)MathF.Floor(viewport.Top / 16f), 1, Main.maxTilesY - 2);
		int lastY = Math.Clamp((int)MathF.Ceiling(viewport.Bottom / 16f) - 1, 1, Main.maxTilesY - 2);

		Dictionary<(int X, int Y), ResourceTile> resources = [];
		Dictionary<(int X, int Y), LiquidTile> liquids = [];
		Dictionary<(int X, int Y), TreeTile> trees = [];
		Dictionary<(short X, short Y), ObjectCandidate> objects = [];

		for (int x = firstX; x <= lastX; x++)
		{
			for (int y = firstY; y <= lastY; y++)
			{
				if (Lighting.Brightness(x, y) <= 0f)
				{
					continue;
				}

				// A tile the caller did not ask for still falls out of the classification
				// here rather than at the collection below it, so an unwanted chest is
				// dropped instead of being read as an ordinary placed object.
				Tile tile = Main.tile[x, y];
				if (wantLiquids && tile.LiquidAmount > 0)
				{
					liquids[(x, y)] = new LiquidTile(x, y, tile.LiquidType);
				}

				if (!tile.HasTile)
				{
					continue;
				}

				ushort type = tile.TileType;
				if (IsTreeOrLargePlant(type))
				{
					if (wantTrees)
					{
						trees[(x, y)] = new TreeTile(x, y, type, GetTileName(x, y, type));
					}
					continue;
				}

				if (type < TileID.Sets.IsAContainer.Length && TileID.Sets.IsAContainer[type])
				{
					if (wantContainers)
					{
						AddObjectCandidate(
							objects,
							x,
							y,
							tile,
							TileObjectData.GetTileData(tile),
							ScannerTargetKind.Container);
					}
					continue;
				}

				if (Main.IsTileSpelunkable(x, y))
				{
					if (wantResources)
					{
						resources[(x, y)] = new ResourceTile(x, y, type, GetTileName(x, y, type));
					}
					continue;
				}

				if (!wantObjects)
				{
					continue;
				}

				TileObjectData? objectData = TileObjectData.GetTileData(tile);
				if (objectData is not null)
				{
					AddObjectCandidate(objects, x, y, tile, objectData, ScannerTargetKind.PlacedObject);
				}
			}
		}

		AddResourceGroups(resources, targets[ScannerCategoryKind.OresAndValuables]);
		AddLiquidGroups(liquids, targets[ScannerCategoryKind.Liquids]);
		AddTreeGroups(trees, targets[ScannerCategoryKind.TreesAndLargePlants]);
		AddObjects(objects.Values, targets);
	}

	private static void AddObjectCandidate(
		Dictionary<(short X, short Y), ObjectCandidate> objects,
		int x,
		int y,
		Tile tile,
		TileObjectData? data,
		ScannerTargetKind kind)
	{
		Point16 root = TileObjectData.TopLeft(x, y);
		if (root == Point16.NegativeOne)
		{
			return;
		}

		(short X, short Y) key = (root.X, root.Y);
		if (objects.ContainsKey(key))
		{
			return;
		}

		int width = Math.Max(1, data?.Width ?? 1);
		int height = Math.Max(1, data?.Height ?? 1);
		Rectangle bounds = new(root.X * 16, root.Y * 16, width * 16, height * 16);
		string name = ContainerNameResolver.IsContainer(tile.TileType)
			? ContainerNameResolver.Describe(root, tile, GetTileName(root.X, root.Y, tile.TileType))
			: GetTileName(x, y, tile.TileType);
		objects[key] = new ObjectCandidate(root, tile.TileType, name, bounds, kind);
	}

	private static void AddResourceGroups(
		Dictionary<(int X, int Y), ResourceTile> candidates,
		List<ScannerTarget> destination)
	{
		HashSet<(int X, int Y)> visited = [];
		foreach (ResourceTile start in candidates.Values.OrderBy(tile => tile.Y).ThenBy(tile => tile.X))
		{
			if (!visited.Add((start.X, start.Y)))
			{
				continue;
			}

			List<ScannerTileReference> group = [];
			Queue<ResourceTile> queue = [];
			queue.Enqueue(start);
			while (queue.TryDequeue(out ResourceTile current))
			{
				group.Add(new ScannerTileReference(current.X, current.Y, current.Type));
				foreach ((int offsetX, int offsetY) in EightDirections)
				{
					(int X, int Y) coordinate = (current.X + offsetX, current.Y + offsetY);
					if (!visited.Contains(coordinate) &&
						candidates.TryGetValue(coordinate, out ResourceTile next) &&
						next.Type == start.Type &&
						next.Name.Equals(start.Name, StringComparison.Ordinal))
					{
						visited.Add(coordinate);
						queue.Enqueue(next);
					}
				}
			}

			AddTileGroup(destination, ScannerTargetKind.Resource, ScannerInteractionKind.None, start.Name, group, start.Type);
		}
	}

	private static void AddLiquidGroups(
		Dictionary<(int X, int Y), LiquidTile> candidates,
		List<ScannerTarget> destination)
	{
		HashSet<(int X, int Y)> visited = [];
		foreach (LiquidTile start in candidates.Values.OrderBy(tile => tile.Y).ThenBy(tile => tile.X))
		{
			if (!visited.Add((start.X, start.Y)))
			{
				continue;
			}

			List<ScannerTileReference> group = [];
			Queue<LiquidTile> queue = [];
			queue.Enqueue(start);
			while (queue.TryDequeue(out LiquidTile current))
			{
				group.Add(new ScannerTileReference(current.X, current.Y, 0, current.LiquidType));
				foreach ((int offsetX, int offsetY) in FourDirections)
				{
					(int X, int Y) coordinate = (current.X + offsetX, current.Y + offsetY);
					if (!visited.Contains(coordinate) &&
						candidates.TryGetValue(coordinate, out LiquidTile next) &&
						next.LiquidType == start.LiquidType)
					{
						visited.Add(coordinate);
						queue.Enqueue(next);
					}
				}
			}

			string name = LiquidName(start.LiquidType);
			AddTileGroup(destination, ScannerTargetKind.Liquid, ScannerInteractionKind.None, name, group, 0);
		}
	}

	private static void AddTreeGroups(
		Dictionary<(int X, int Y), TreeTile> candidates,
		List<ScannerTarget> destination)
	{
		HashSet<(int X, int Y)> visited = [];
		foreach (TreeTile start in candidates.Values.OrderBy(tile => tile.Y).ThenBy(tile => tile.X))
		{
			if (!visited.Add((start.X, start.Y)))
			{
				continue;
			}

			List<ScannerTileReference> group = [];
			Queue<TreeTile> queue = [];
			queue.Enqueue(start);
			while (queue.TryDequeue(out TreeTile current))
			{
				group.Add(new ScannerTileReference(current.X, current.Y, current.Type));
				foreach ((int offsetX, int offsetY) in EightDirections)
				{
					(int X, int Y) coordinate = (current.X + offsetX, current.Y + offsetY);
					if (!visited.Contains(coordinate) &&
						candidates.TryGetValue(coordinate, out TreeTile next) &&
						next.Type == start.Type)
					{
						visited.Add(coordinate);
						queue.Enqueue(next);
					}
				}
			}

			AddTileGroup(destination, ScannerTargetKind.Tree, ScannerInteractionKind.None, start.Name, group, start.Type);
		}
	}

	private static void AddTileGroup(
		List<ScannerTarget> destination,
		ScannerTargetKind kind,
		ScannerInteractionKind interaction,
		string name,
		List<ScannerTileReference> group,
		ushort type)
	{
		Rectangle bounds = BoundsForTiles(group);
		destination.Add(new ScannerTarget(
			$"{kind}-{type}-{group[0].X}-{group[0].Y}",
			kind,
			interaction,
			name,
			bounds,
			bounds.Center.ToVector2(),
			group.ToImmutableArray(),
			VisibleTileCount: group.Count));
	}

	private static void AddObjects(
		IEnumerable<ObjectCandidate> candidates,
		Dictionary<ScannerCategoryKind, List<ScannerTarget>> targets)
	{
		foreach (ObjectCandidate candidate in candidates)
		{
			ScannerCategoryKind category = candidate.Kind == ScannerTargetKind.Container
				? ScannerCategoryKind.Containers
				: ScannerCategoryKind.PlacedObjects;
			ScannerTileReference tile = new(candidate.Root.X, candidate.Root.Y, candidate.Type);
			targets[category].Add(new ScannerTarget(
				$"{candidate.Kind}-{candidate.Type}-{candidate.Root.X}-{candidate.Root.Y}",
				candidate.Kind,
				ScannerInteractionKind.RightClickTile,
				candidate.Name,
				candidate.Bounds,
				candidate.Bounds.Center.ToVector2(),
				ImmutableArray.Create(tile),
				VisibleTileCount: 1));
		}
	}

	private static void ScanNpcs(
		Rectangle viewport,
		IReadOnlySet<ScannerCategoryKind> categories,
		Dictionary<ScannerCategoryKind, List<ScannerTarget>> targets)
	{
		if (!categories.Contains(ScannerCategoryKind.Npcs) &&
			!categories.Contains(ScannerCategoryKind.Enemies) &&
			!categories.Contains(ScannerCategoryKind.PassiveCreatures))
		{
			return;
		}

		Dictionary<int, List<NPC>> visibleGroups = [];
		for (int index = 0; index < Main.maxNPCs; index++)
		{
			NPC npc = Main.npc[index];
			if (!npc.active || !IsEntityVisibleAndLit(npc.Hitbox, viewport))
			{
				continue;
			}

			int rootIndex = npc.realLife >= 0 && npc.realLife < Main.maxNPCs ? npc.realLife : npc.whoAmI;
			if (!visibleGroups.TryGetValue(rootIndex, out List<NPC>? group))
			{
				group = [];
				visibleGroups[rootIndex] = group;
			}
			group.Add(npc);
		}

		foreach ((int rootIndex, List<NPC> visibleSegments) in visibleGroups)
		{
			NPC npc = Main.npc[rootIndex].active ? Main.npc[rootIndex] : visibleSegments[0];
			ScannerCategoryKind category;
			ScannerTargetKind kind;
			bool canTalk = CanTalk(npc);
			if (canTalk || npc.isLikeATownNPC)
			{
				category = ScannerCategoryKind.Npcs;
				kind = ScannerTargetKind.Npc;
			}
			else if (!npc.friendly && !npc.CountsAsACritter)
			{
				category = ScannerCategoryKind.Enemies;
				kind = ScannerTargetKind.Enemy;
			}
			else
			{
				category = ScannerCategoryKind.PassiveCreatures;
				kind = ScannerTargetKind.PassiveCreature;
			}

			if (!categories.Contains(category))
			{
				continue;
			}

			Rectangle bounds = visibleSegments[0].Hitbox;
			for (int index = 1; index < visibleSegments.Count; index++)
			{
				bounds = Rectangle.Union(bounds, visibleSegments[index].Hitbox);
			}

			targets[category].Add(new ScannerTarget(
				$"npc-{rootIndex}-{npc.type}-{(int)npc.position.X}-{(int)npc.position.Y}",
				kind,
				canTalk ? ScannerInteractionKind.TalkToNpc : ScannerInteractionKind.None,
				npc.FullName,
				bounds,
				bounds.Center.ToVector2(),
				ImmutableArray<ScannerTileReference>.Empty,
				EntityIndex: rootIndex,
				EntityType: npc.type,
				EntityIdentity: npc.GetGlobalNPC<ScannerNpcIdentity>().Identity,
				EntitySnapshotPosition: npc.Center,
				Health: npc.life,
				MaxHealth: npc.lifeMax,
				IsBoss: npc.boss));
		}
	}

	private static void ScanItems(
		Rectangle viewport,
		IReadOnlySet<ScannerCategoryKind> categories,
		Dictionary<ScannerCategoryKind, List<ScannerTarget>> targets)
	{
		if (!categories.Contains(ScannerCategoryKind.DroppedItems))
		{
			return;
		}

		for (int index = 0; index < Main.maxItems; index++)
		{
			Item item = Main.item[index];
			if (!item.active || item.IsAir || item.stack <= 0 || !IsEntityVisibleAndLit(item.Hitbox, viewport))
			{
				continue;
			}

			targets[ScannerCategoryKind.DroppedItems].Add(new ScannerTarget(
				$"item-{index}-{item.type}-{item.timeSinceItemSpawned}",
				ScannerTargetKind.DroppedItem,
				ScannerInteractionKind.PickupItem,
				item.AffixName(),
				item.Hitbox,
				item.Center,
				ImmutableArray<ScannerTileReference>.Empty,
				EntityIndex: index,
				EntityType: item.type,
				EntityIdentity: item.GetGlobalItem<ScannerItemIdentity>().Identity,
				EntityAge: item.timeSinceItemSpawned,
				EntitySnapshotPosition: item.Center,
				Stack: item.stack));
		}
	}

	private static bool CanTalk(NPC npc)
	{
		bool vanillaCanTalk = npc.townNPC || npc.type is
			NPCID.BoundGoblin or NPCID.BoundWizard or NPCID.BoundMechanic or NPCID.WebbedStylist or
			NPCID.SleepingAngler or NPCID.SkeletonMerchant or NPCID.BartenderUnconscious or NPCID.GolferRescue;
		return NPCLoader.CanChat(npc) ?? vanillaCanTalk;
	}

	private static bool IsEntityVisibleAndLit(Rectangle hitbox, Rectangle viewport)
	{
		Rectangle visible = Rectangle.Intersect(hitbox, viewport);
		if (visible.Width <= 0 || visible.Height <= 0)
		{
			return false;
		}

		int firstX = Math.Clamp((int)MathF.Floor(visible.Left / 16f), 1, Main.maxTilesX - 2);
		int lastX = Math.Clamp((int)MathF.Ceiling(visible.Right / 16f) - 1, 1, Main.maxTilesX - 2);
		int firstY = Math.Clamp((int)MathF.Floor(visible.Top / 16f), 1, Main.maxTilesY - 2);
		int lastY = Math.Clamp((int)MathF.Ceiling(visible.Bottom / 16f) - 1, 1, Main.maxTilesY - 2);
		for (int x = firstX; x <= lastX; x++)
		{
			for (int y = firstY; y <= lastY; y++)
			{
				if (Lighting.Brightness(x, y) > 0f)
				{
					return true;
				}
			}
		}
		return false;
	}

	private static Rectangle BoundsForTiles(List<ScannerTileReference> tiles)
	{
		int minX = tiles.Min(tile => tile.X);
		int maxX = tiles.Max(tile => tile.X);
		int minY = tiles.Min(tile => tile.Y);
		int maxY = tiles.Max(tile => tile.Y);
		return new Rectangle(minX * 16, minY * 16, (maxX - minX + 1) * 16, (maxY - minY + 1) * 16);
	}

	private static bool IsTreeOrLargePlant(ushort type)
	{
		if (type < TileID.Sets.IsATreeTrunk.Length && TileID.Sets.IsATreeTrunk[type])
		{
			return true;
		}

		return type is TileID.Trees or TileID.MushroomTrees or TileID.Cactus or TileID.PineTree or
			TileID.PalmTree or TileID.VanityTreeSakura or TileID.VanityTreeYellowWillow or TileID.TreeAsh or
			TileID.TreeTopaz or TileID.TreeAmethyst or TileID.TreeSapphire or TileID.TreeEmerald or
			TileID.TreeRuby or TileID.TreeDiamond or TileID.TreeAmber;
	}

	private static string GetTileName(int x, int y, ushort type)
	{
		if (type < MapHelper.tileLookup.Length && MapHelper.tileLookup[type] != 0)
		{
			MapTile mapTile = MapHelper.CreateMapTile(x, y, byte.MaxValue);
			if (mapTile.Type != 0)
			{
				string mapName = Lang.GetMapObjectName(mapTile.Type);
				if (!string.IsNullOrWhiteSpace(mapName))
				{
					return mapName;
				}
			}
		}

		ModTile? modTile = TileLoader.GetTile(type);
		if (modTile is not null)
		{
			return Humanize(modTile.Name);
		}

		string? vanillaName = TileID.Search.GetName(type);
		return string.IsNullOrWhiteSpace(vanillaName) ? $"Tile {type}" : Humanize(vanillaName);
	}

	private static string LiquidName(int liquidType) => liquidType switch
	{
		LiquidID.Lava => "Lava",
		LiquidID.Honey => "Honey",
		LiquidID.Shimmer => "Shimmer",
		_ => "Water",
	};

	private static string CategoryName(ScannerCategoryKind kind) => kind switch
	{
		ScannerCategoryKind.OresAndValuables => "Ores and valuables",
		ScannerCategoryKind.Liquids => "Liquids",
		ScannerCategoryKind.Npcs => "NPCs",
		ScannerCategoryKind.Enemies => "Enemies",
		ScannerCategoryKind.PassiveCreatures => "Passive creatures",
		ScannerCategoryKind.DroppedItems => "Dropped items",
		ScannerCategoryKind.Containers => "Containers",
		ScannerCategoryKind.TreesAndLargePlants => "Trees and large plants",
		_ => "Placed objects",
	};

	private static int CompareTargets(ScannerTarget left, ScannerTarget right, Vector2 playerPosition)
	{
		int distanceComparison = DistanceSquared(left.WorldPosition, playerPosition)
			.CompareTo(DistanceSquared(right.WorldPosition, playerPosition));
		if (distanceComparison != 0)
		{
			return distanceComparison;
		}

		int nameComparison = StringComparer.OrdinalIgnoreCase.Compare(left.Name, right.Name);
		if (nameComparison != 0)
		{
			return nameComparison;
		}

		int xComparison = left.WorldBounds.X.CompareTo(right.WorldBounds.X);
		return xComparison != 0 ? xComparison : left.WorldBounds.Y.CompareTo(right.WorldBounds.Y);
	}

	private static float DistanceSquared(Vector2 left, Vector2 right) => Vector2.DistanceSquared(left, right);

	private static string Humanize(string value)
	{
		System.Text.StringBuilder result = new();
		for (int index = 0; index < value.Length; index++)
		{
			char current = value[index];
			if (index > 0 && char.IsUpper(current) && char.IsLower(value[index - 1]))
			{
				result.Append(' ');
			}
			result.Append(current);
		}
		return result.ToString();
	}
}
