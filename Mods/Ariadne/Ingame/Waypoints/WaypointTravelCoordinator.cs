#nullable enable

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;
using Ariadne.Ingame.Teleport;

namespace Ariadne.Ingame.Waypoints;

internal readonly record struct WaypointTravelResult(bool Success, string Message);

internal sealed class WaypointTravelCoordinator
{
	private const int MaximumSearchDistanceInTiles = 30;
	private const string TeleportContext = "Ariadne.Waypoints";

	private readonly TeleportExecutor _executor = new();

	internal WaypointTravelResult Travel(Waypoint waypoint, Action closeMenu)
	{
		if (Main.gameMenu || !Main.LocalPlayer.active || Main.LocalPlayer.dead)
		{
			return new WaypointTravelResult(false, "Waypoints cannot move the player right now.");
		}

		Player player = Main.LocalPlayer;
		if (!TryFindLanding(player, waypoint, out Vector2 destination))
		{
			return new WaypointTravelResult(
				false,
				$"No safe landing position was found near {waypoint.Name}. The terrain there may have changed.");
		}

		closeMenu();
		_executor.Execute(player, destination, $"waypoint travel to {waypoint.Name}");
		return new WaypointTravelResult(true, $"Travelled to {waypoint.Name}.");
	}

	internal void UpdateVerification() => _executor.UpdateVerification();

	internal void Reset() => _executor.Reset();

	private static bool TryFindLanding(Player player, Waypoint waypoint, out Vector2 destination)
	{
		destination = default;
		Vector2 preferred = SafeLandingProbe.StandingPosition(player, waypoint.TileX, waypoint.TileY);
		if (IsUsable(player, preferred))
		{
			destination = preferred;
			return true;
		}

		// The saved spot may have been built over or flooded since. Take the closest
		// alternative rather than refusing outright.
		int minimumX = Math.Clamp(waypoint.TileX - MaximumSearchDistanceInTiles, 1, Main.maxTilesX - 2);
		int maximumX = Math.Clamp(waypoint.TileX + MaximumSearchDistanceInTiles, 1, Main.maxTilesX - 2);
		int minimumY = Math.Clamp(waypoint.TileY - MaximumSearchDistanceInTiles, 1, Main.maxTilesY - 2);
		int maximumY = Math.Clamp(waypoint.TileY + MaximumSearchDistanceInTiles, 1, Main.maxTilesY - 2);

		List<(Vector2 Position, float DistanceSquared)> candidates = [];
		for (int tileX = minimumX; tileX <= maximumX; tileX++)
		{
			for (int tileY = minimumY; tileY <= maximumY; tileY++)
			{
				Vector2 candidate = SafeLandingProbe.StandingPosition(player, tileX, tileY);
				if (!SafeLandingProbe.IsSafeLanding(player, candidate))
				{
					continue;
				}

				float deltaX = tileX - waypoint.TileX;
				float deltaY = tileY - waypoint.TileY;
				candidates.Add((candidate, deltaX * deltaX + deltaY * deltaY));
			}
		}

		candidates.Sort((left, right) => left.DistanceSquared.CompareTo(right.DistanceSquared));
		foreach ((Vector2 position, _) in candidates)
		{
			if (PassesTeleportHooks(player, position))
			{
				destination = position;
				return true;
			}
		}
		return false;
	}

	private static bool IsUsable(Player player, Vector2 position)
	{
		return SafeLandingProbe.IsSafeLanding(player, position) && PassesTeleportHooks(player, position);
	}

	private static bool PassesTeleportHooks(Player player, Vector2 position)
	{
		Point centerTile = (position + new Vector2(player.width / 2f, player.height / 2f)).ToTileCoordinates();
		return CombinedHooks.CanBeTeleportedTo(player, position, centerTile.X, centerTile.Y, TeleportContext);
	}
}
