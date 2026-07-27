#nullable enable

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ObjectData;
using Ariadne.Ingame.Teleport;

namespace Ariadne.Ingame.Scanner;

internal readonly record struct ScannerActivationResult(bool Success, string Message);

internal sealed class ScannerTeleportCoordinator
{
	private const int MaximumSearchDistanceInTiles = 40;
	private const string TeleportContext = "Ariadne.VisibleSurroundingsScanner";
	private readonly TeleportExecutor _executor = new();

	private readonly record struct ResolvedTarget(
		Rectangle Bounds,
		NPC? Npc = null,
		Item? Item = null,
		Point16 TilePosition = default);

	private readonly record struct LandingScore(
		float TargetDistanceSquared,
		int OppositeSidePenalty,
		float PlayerDistanceSquared) : IComparable<LandingScore>
	{
		public int CompareTo(LandingScore other)
		{
			int targetComparison = TargetDistanceSquared.CompareTo(other.TargetDistanceSquared);
			if (targetComparison != 0)
			{
				return targetComparison;
			}
			int sideComparison = OppositeSidePenalty.CompareTo(other.OppositeSidePenalty);
			return sideComparison != 0 ? sideComparison : PlayerDistanceSquared.CompareTo(other.PlayerDistanceSquared);
		}
	}

	private readonly record struct LandingCandidate(Vector2 Position, LandingScore Score);

	internal ScannerActivationResult Activate(
		ScannerTarget target,
		Action closeScanner,
		Action<IReadOnlyList<string>> requestInventoryFocus)
	{
		if (Main.gameMenu || !Main.LocalPlayer.active || Main.LocalPlayer.dead)
		{
			return new ScannerActivationResult(false, "The scanner cannot move the player right now.");
		}

		if (!TryResolve(target, out ResolvedTarget resolved))
		{
			return new ScannerActivationResult(false, $"{target.Name} is no longer available. Scan again to refresh the list.");
		}

		Player player = Main.LocalPlayer;
		if (!TryFindLanding(player, target, resolved, out Vector2 destination))
		{
			return new ScannerActivationResult(false, $"No safe landing position was found near {target.Name}.");
		}

		closeScanner();
		_executor.Execute(player, destination, $"scanner teleport near {target.Name}");

		PerformInteraction(target, resolved, player, requestInventoryFocus);
		return new ScannerActivationResult(true, $"Moved near {target.Name}.");
	}

	internal void UpdateVerification() => _executor.UpdateVerification();

	internal void Reset() => _executor.Reset();

	private static bool TryResolve(ScannerTarget target, out ResolvedTarget resolved)
	{
		switch (target.Kind)
		{
			case ScannerTargetKind.Npc:
			case ScannerTargetKind.Enemy:
			case ScannerTargetKind.PassiveCreature:
				return TryResolveNpc(target, out resolved);
			case ScannerTargetKind.DroppedItem:
				return TryResolveItem(target, out resolved);
			case ScannerTargetKind.Container:
			case ScannerTargetKind.PlacedObject:
				return TryResolveObject(target, out resolved);
			case ScannerTargetKind.Liquid:
				return TryResolveLiquid(target, out resolved);
			default:
				return TryResolveTileGroup(target, out resolved);
		}
	}

	private static bool TryResolveNpc(ScannerTarget target, out ResolvedTarget resolved)
	{
		resolved = default;
		if (target.EntityIndex < 0 || target.EntityIndex >= Main.maxNPCs)
		{
			return false;
		}

		NPC npc = Main.npc[target.EntityIndex];
		if (!npc.active || npc.type != target.EntityType ||
			npc.GetGlobalNPC<ScannerNpcIdentity>().Identity != target.EntityIdentity)
		{
			return false;
		}

		Rectangle bounds = npc.Hitbox;
		for (int index = 0; index < Main.maxNPCs; index++)
		{
			NPC segment = Main.npc[index];
			if (segment.active && segment.realLife == target.EntityIndex)
			{
				bounds = Rectangle.Union(bounds, segment.Hitbox);
			}
		}
		resolved = new ResolvedTarget(bounds, Npc: npc);
		return true;
	}

	private static bool TryResolveItem(ScannerTarget target, out ResolvedTarget resolved)
	{
		resolved = default;
		if (target.EntityIndex < 0 || target.EntityIndex >= Main.maxItems)
		{
			return false;
		}

		Item item = Main.item[target.EntityIndex];
		if (!item.active || item.IsAir || item.type != target.EntityType ||
			item.GetGlobalItem<ScannerItemIdentity>().Identity != target.EntityIdentity ||
			item.timeSinceItemSpawned < target.EntityAge)
		{
			return false;
		}
		resolved = new ResolvedTarget(item.Hitbox, Item: item);
		return true;
	}

	private static bool TryResolveObject(ScannerTarget target, out ResolvedTarget resolved)
	{
		resolved = default;
		if (target.Tiles.IsDefaultOrEmpty)
		{
			return false;
		}

		ScannerTileReference reference = target.Tiles[0];
		if (!WorldGen.InWorld(reference.X, reference.Y, 1))
		{
			return false;
		}
		Tile tile = Main.tile[reference.X, reference.Y];
		if (!tile.HasTile || tile.TileType != reference.TileType ||
			TileObjectData.TopLeft(reference.X, reference.Y) != new Point16(reference.X, reference.Y))
		{
			return false;
		}

		TileObjectData? data = TileObjectData.GetTileData(tile);
		Rectangle bounds = new(
			reference.X * 16,
			reference.Y * 16,
			Math.Max(1, data?.Width ?? 1) * 16,
			Math.Max(1, data?.Height ?? 1) * 16);
		resolved = new ResolvedTarget(bounds, TilePosition: new Point16(reference.X, reference.Y));
		return true;
	}

	private static bool TryResolveLiquid(ScannerTarget target, out ResolvedTarget resolved)
	{
		List<ScannerTileReference> remaining = [];
		foreach (ScannerTileReference reference in target.Tiles)
		{
			if (!WorldGen.InWorld(reference.X, reference.Y, 1))
			{
				continue;
			}
			Tile tile = Main.tile[reference.X, reference.Y];
			if (tile.LiquidAmount > 0 && tile.LiquidType == reference.LiquidType)
			{
				remaining.Add(reference);
			}
		}

		return TryResolveRemainingTiles(remaining, out resolved);
	}

	private static bool TryResolveTileGroup(ScannerTarget target, out ResolvedTarget resolved)
	{
		List<ScannerTileReference> remaining = [];
		foreach (ScannerTileReference reference in target.Tiles)
		{
			if (!WorldGen.InWorld(reference.X, reference.Y, 1))
			{
				continue;
			}
			Tile tile = Main.tile[reference.X, reference.Y];
			if (tile.HasTile && tile.TileType == reference.TileType)
			{
				remaining.Add(reference);
			}
		}

		return TryResolveRemainingTiles(remaining, out resolved);
	}

	private static bool TryResolveRemainingTiles(List<ScannerTileReference> tiles, out ResolvedTarget resolved)
	{
		resolved = default;
		if (tiles.Count == 0)
		{
			return false;
		}

		int minX = int.MaxValue;
		int maxX = int.MinValue;
		int minY = int.MaxValue;
		int maxY = int.MinValue;
		foreach (ScannerTileReference tile in tiles)
		{
			minX = Math.Min(minX, tile.X);
			maxX = Math.Max(maxX, tile.X);
			minY = Math.Min(minY, tile.Y);
			maxY = Math.Max(maxY, tile.Y);
		}
		resolved = new ResolvedTarget(new Rectangle(
			minX * 16,
			minY * 16,
			(maxX - minX + 1) * 16,
			(maxY - minY + 1) * 16));
		return true;
	}

	private static bool TryFindLanding(Player player, ScannerTarget target, ResolvedTarget resolved, out Vector2 destination)
	{
		destination = default;
		int minimumX = Math.Clamp((int)MathF.Floor(resolved.Bounds.Left / 16f) - MaximumSearchDistanceInTiles, 1, Main.maxTilesX - 2);
		int maximumX = Math.Clamp((int)MathF.Ceiling(resolved.Bounds.Right / 16f) + MaximumSearchDistanceInTiles, 1, Main.maxTilesX - 2);
		int minimumY = Math.Clamp((int)MathF.Floor(resolved.Bounds.Top / 16f) - MaximumSearchDistanceInTiles, 1, Main.maxTilesY - 2);
		int maximumY = Math.Clamp((int)MathF.Ceiling(resolved.Bounds.Bottom / 16f) + MaximumSearchDistanceInTiles, 1, Main.maxTilesY - 2);
		bool playerWasLeft = player.Center.X <= resolved.Bounds.Center.X;
		List<LandingCandidate> candidates = [];

		for (int tileX = minimumX; tileX <= maximumX; tileX++)
		{
			for (int tileY = minimumY; tileY <= maximumY; tileY++)
			{
				Vector2 candidate = SafeLandingProbe.StandingPosition(player, tileX, tileY);
				if (!SafeLandingProbe.IsSafeLanding(player, candidate) || !IsCloseEnoughForInteraction(player, candidate, target, resolved))
				{
					continue;
				}

				Vector2 center = candidate + new Vector2(player.width / 2f, player.height / 2f);
				float targetDistance = DistanceSquaredToRectangle(center, resolved.Bounds);
				bool candidateIsLeft = center.X <= resolved.Bounds.Center.X;
				LandingScore score = new(
					targetDistance,
					candidateIsLeft == playerWasLeft ? 0 : 1,
					Vector2.DistanceSquared(candidate, player.position));
				candidates.Add(new LandingCandidate(candidate, score));
			}
		}

		candidates.Sort((left, right) => left.Score.CompareTo(right.Score));
		foreach (LandingCandidate candidate in candidates)
		{
			Point centerTile = (candidate.Position + new Vector2(player.width / 2f, player.height / 2f)).ToTileCoordinates();
			if (CombinedHooks.CanBeTeleportedTo(player, candidate.Position, centerTile.X, centerTile.Y, TeleportContext))
			{
				destination = candidate.Position;
				return true;
			}
		}
		return false;
	}

	private static bool IsCloseEnoughForInteraction(Player player, Vector2 position, ScannerTarget target, ResolvedTarget resolved)
	{
		Vector2 originalPosition = player.position;
		try
		{
			player.position = position;
			switch (target.Interaction)
			{
				case ScannerInteractionKind.RightClickTile:
					return player.IsInTileInteractionRange(resolved.TilePosition.X, resolved.TilePosition.Y, TileReachCheckSettings.Simple);
				case ScannerInteractionKind.TalkToNpc:
					Rectangle talkRange = new(
						(int)(player.Center.X - Player.tileRangeX * 16f),
						(int)(player.Center.Y - Player.tileRangeY * 16f),
						Player.tileRangeX * 32,
						Player.tileRangeY * 32);
					return talkRange.Intersects(resolved.Bounds);
				case ScannerInteractionKind.PickupItem:
					int grabRange = resolved.Item is Item item ? player.GetItemGrabRange(item) : 0;
					return DistanceSquaredBetweenRectangles(player.Hitbox, resolved.Bounds) <= grabRange * grabRange;
				default:
					return true;
			}
		}
		finally
		{
			player.position = originalPosition;
		}
	}

	private static void PerformInteraction(
		ScannerTarget target,
		ResolvedTarget resolved,
		Player player,
		Action<IReadOnlyList<string>> requestInventoryFocus)
	{
		switch (target.Interaction)
		{
			case ScannerInteractionKind.TalkToNpc when resolved.Npc is NPC npc:
				Main.CancelHairWindow();
				Main.SetNPCShopIndex(0);
				Main.InGuideCraftMenu = false;
				Main.InReforgeMenu = false;
				player.dropItemCheck();
				Main.npcChatCornerItem = 0;
				player.sign = -1;
				Main.editSign = false;
				player.SetTalkNPC(npc.whoAmI);
				player.chest = -1;
				Recipe.FindRecipes();
				Main.npcChatText = npc.GetChat();
				// Match native NPC interaction. AccessibleIngameMenuSystem promotes the
				// resulting conversation to its focused semantic menu on the next input
				// update without exposing the full player inventory.
				Main.playerInventory = false;
				break;
			case ScannerInteractionKind.RightClickTile:
				PerformNativeRightClick(player, resolved.TilePosition);
				if (player.chest != -1)
				{
					requestInventoryFocus(["interactions", "container"]);
				}
				else if (player.sign >= 0)
				{
					// Signs, like NPC chat, are promoted to a focused semantic menu by the
					// in-game menu system and do not require the inventory to be visible.
					Main.playerInventory = false;
				}
				break;
		}
	}

	private static void PerformNativeRightClick(Player player, Point16 tilePosition)
	{
		bool previousAttempted = player.tileInteractAttempted;
		bool previousRelease = player.releaseUseTile;
		int previousTargetX = Player.tileTargetX;
		int previousTargetY = Player.tileTargetY;
		try
		{
			Player.tileTargetX = tilePosition.X;
			Player.tileTargetY = tilePosition.Y;
			player.tileInteractAttempted = true;
			player.releaseUseTile = true;
			player.TileInteractionsCheck(tilePosition.X, tilePosition.Y);
		}
		finally
		{
			player.tileInteractAttempted = previousAttempted;
			player.releaseUseTile = previousRelease;
			Player.tileTargetX = previousTargetX;
			Player.tileTargetY = previousTargetY;
		}
	}

	private static float DistanceSquaredToRectangle(Vector2 point, Rectangle rectangle)
	{
		float deltaX = Math.Max(rectangle.Left - point.X, Math.Max(0f, point.X - rectangle.Right));
		float deltaY = Math.Max(rectangle.Top - point.Y, Math.Max(0f, point.Y - rectangle.Bottom));
		return deltaX * deltaX + deltaY * deltaY;
	}

	private static float DistanceSquaredBetweenRectangles(Rectangle left, Rectangle right)
	{
		float deltaX = Math.Max(right.Left - left.Right, Math.Max(0f, left.Left - right.Right));
		float deltaY = Math.Max(right.Top - left.Bottom, Math.Max(0f, left.Top - right.Bottom));
		return deltaX * deltaX + deltaY * deltaY;
	}
}
