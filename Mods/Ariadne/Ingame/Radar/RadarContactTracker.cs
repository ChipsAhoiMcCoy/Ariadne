#nullable enable

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Ariadne.Configs;
using Ariadne.Ingame.Scanner;

namespace Ariadne.Ingame.Radar;

/// <summary>One thing the radar found, and everything needed to sound and name it.</summary>
internal readonly record struct RadarContact(
	ScannerTarget Target,
	int PipCount,
	float DistanceSquared,
	float Proximity)
{
	internal Vector2 WorldPosition => Target.WorldPosition;

	internal string Name => Target.Name;
}

/// <summary>
/// Finds what is in radar range and remembers what the listener has already been told
/// about.
///
/// The scan itself is the scanner's, deliberately. A radar that found things by its own
/// rules would sooner or later ping something the scanner then refused to list, and the
/// whole point of the radar is to say when opening the scanner is worth it.
///
/// What it cannot borrow is the scanner's notion of identity. <see cref="ScannerTarget.Id"/>
/// is built for a snapshot the listener is reading right now: a tile group takes its id
/// from the tile the flood fill happened to start at, which moves as lighting reveals
/// more of a vein, and the entity ids carry a live position or age. All three change from
/// one sweep to the next, so any of them used as a memory key would report the same ore
/// twice a second forever.
/// </summary>
internal sealed class RadarContactTracker
{
	private const float TileSize = 16f;

	/// <summary>
	/// How far past the radar's own range a remembered tile is kept. Without it, a
	/// listener standing on the boundary would hear the same vein announced every time
	/// they drifted a pixel back and forth across it.
	/// </summary>
	private const int SeenTileMarginTiles = 20;

	/// <summary>
	/// How many sweeps a creature or dropped item may be absent before the radar forgets
	/// it. Their positions are not knowable once they leave the sweep, so absence is the
	/// only measure available, and roughly ten seconds is long enough that a bunny hopping
	/// behind a rock is not a new discovery when it comes back out.
	/// </summary>
	private const int SeenEntityAbsenceLimit = 20;

	private readonly HashSet<long> _seenTiles = [];
	private readonly Dictionary<long, int> _seenEntities = [];
	private readonly HashSet<ScannerCategoryKind> _armed = [];
	private readonly HashSet<long> _presentEntities = [];
	private readonly List<RadarContact> _contacts = [];
	private readonly List<long> _expired = [];

	/// <summary>
	/// Everything in range, nearest first. Range is measured as a radius even though the
	/// scan itself is a rectangle, so that "thirty tiles" means the same in every
	/// direction.
	/// </summary>
	internal IReadOnlyList<RadarContact> Capture(
		SpatialObserverSnapshot observer,
		AriadneClientConfig config)
	{
		_contacts.Clear();
		BuildArmedCategories(config);
		if (_armed.Count == 0)
		{
			ForgetOutOfRange(observer, config.RadarRangeTiles);
			return _contacts;
		}

		float rangePixels = MathF.Max(config.RadarRangeTiles * TileSize, 1f);
		Rectangle bounds = new(
			(int)MathF.Floor(observer.Center.X - rangePixels),
			(int)MathF.Floor(observer.Center.Y - rangePixels),
			(int)MathF.Ceiling(rangePixels * 2f),
			(int)MathF.Ceiling(rangePixels * 2f));

		ScannerSnapshot snapshot = ScannerSnapshotBuilder.Capture(bounds, _armed);
		foreach (ScannerCategory category in snapshot.Categories)
		{
			int pipCount = PipCountFor(category.Kind);
			foreach (ScannerTarget target in category.Targets)
			{
				float distanceSquared = Vector2.DistanceSquared(target.WorldPosition, observer.Center);
				if (distanceSquared > rangePixels * rangePixels)
				{
					continue;
				}

				_contacts.Add(new(
					target,
					pipCount,
					distanceSquared,
					Math.Clamp(1f - MathF.Sqrt(distanceSquared) / rangePixels, 0f, 1f)));
			}
		}

		_contacts.Sort(static (left, right) => left.DistanceSquared.CompareTo(right.DistanceSquared));
		ForgetOutOfRange(observer, config.RadarRangeTiles);
		AgeAbsentEntities();
		return _contacts;
	}

	/// <summary>
	/// Whether this contact is something the listener has not been told about. A tile
	/// group counts as known when any single one of its tiles is remembered, which is what
	/// keeps a vein from being rediscovered as more of it is lit or as the flood fill
	/// re-anchors.
	/// </summary>
	internal bool IsNew(in RadarContact contact)
	{
		if (TryGetEntityKey(contact.Target, out long key))
		{
			return !_seenEntities.ContainsKey(key);
		}

		foreach (ScannerTileReference tile in contact.Target.Tiles)
		{
			if (_seenTiles.Contains(Pack(tile.X, tile.Y)))
			{
				return false;
			}
		}

		return true;
	}

	internal void MarkSeen(in RadarContact contact)
	{
		if (TryGetEntityKey(contact.Target, out long key))
		{
			_seenEntities[key] = 0;
			return;
		}

		foreach (ScannerTileReference tile in contact.Target.Tiles)
		{
			_seenTiles.Add(Pack(tile.X, tile.Y));
		}
	}

	/// <summary>
	/// The memory key for an entity contact, or false for a tile group.
	///
	/// NPCs and dropped items are numbered by two independent counters, so the same value
	/// identifies one of each. Items are held negative to keep the two apart in a single
	/// table; without it, a slime and a fallen torch could take turns suppressing each
	/// other's contact. Zero is never issued, because both counters are incremented before
	/// they are read.
	/// </summary>
	private static bool TryGetEntityKey(ScannerTarget target, out long key)
	{
		key = target.Kind == ScannerTargetKind.DroppedItem
			? -target.EntityIdentity
			: target.EntityIdentity;
		return target.EntityIdentity != 0;
	}

	internal void Reset()
	{
		_seenTiles.Clear();
		_seenEntities.Clear();
		_presentEntities.Clear();
		_contacts.Clear();
	}

	/// <summary>
	/// The categories the radar is armed with, mirroring what the Spelunker and Hunter
	/// potions reveal. Spelunker's tile set lands on ores, valuables and containers under
	/// the scanner's own routing; Hunter touches nothing but NPCs. Enemies are held out by
	/// default because the hostile tone already follows the nearest one.
	/// </summary>
	private void BuildArmedCategories(AriadneClientConfig config)
	{
		_armed.Clear();
		Arm(config.RadarDetectsOresAndValuables, ScannerCategoryKind.OresAndValuables);
		Arm(config.RadarDetectsContainers, ScannerCategoryKind.Containers);
		Arm(config.RadarDetectsCreatures, ScannerCategoryKind.Npcs);
		Arm(config.RadarDetectsCreatures, ScannerCategoryKind.PassiveCreatures);
		Arm(config.RadarDetectsEnemies, ScannerCategoryKind.Enemies);
		Arm(config.RadarDetectsDroppedItems, ScannerCategoryKind.DroppedItems);
		Arm(config.RadarDetectsLiquids, ScannerCategoryKind.Liquids);
		Arm(config.RadarDetectsTreesAndPlants, ScannerCategoryKind.TreesAndLargePlants);
		Arm(config.RadarDetectsPlacedObjects, ScannerCategoryKind.PlacedObjects);
	}

	private void Arm(bool wanted, ScannerCategoryKind kind)
	{
		if (wanted)
		{
			_armed.Add(kind);
		}
	}

	/// <summary>
	/// How many times the bell is struck for a category. The counts rise as the contact
	/// gets rarer in ordinary play, so the sound heard most often is also the shortest.
	/// </summary>
	private static int PipCountFor(ScannerCategoryKind kind) => kind switch
	{
		ScannerCategoryKind.OresAndValuables => 1,
		ScannerCategoryKind.Containers => 2,
		ScannerCategoryKind.Npcs or ScannerCategoryKind.PassiveCreatures => 3,
		_ => 4,
	};

	private void ForgetOutOfRange(SpatialObserverSnapshot observer, int rangeTiles)
	{
		if (_seenTiles.Count == 0)
		{
			return;
		}

		int centerTileX = (int)MathF.Floor(observer.Center.X / TileSize);
		int centerTileY = (int)MathF.Floor(observer.Center.Y / TileSize);
		int reach = rangeTiles + SeenTileMarginTiles;
		_seenTiles.RemoveWhere(packed =>
			Math.Abs(UnpackX(packed) - centerTileX) > reach ||
			Math.Abs(UnpackY(packed) - centerTileY) > reach);
	}

	private void AgeAbsentEntities()
	{
		if (_seenEntities.Count == 0)
		{
			return;
		}

		_presentEntities.Clear();
		foreach (RadarContact contact in _contacts)
		{
			if (TryGetEntityKey(contact.Target, out long present))
			{
				_presentEntities.Add(present);
			}
		}

		// Overwriting an existing key's value is the one mutation a dictionary permits
		// mid-enumeration; removals are collected and applied afterwards.
		_expired.Clear();
		foreach ((long identity, int absentSweeps) in _seenEntities)
		{
			if (_presentEntities.Contains(identity))
			{
				_seenEntities[identity] = 0;
				continue;
			}

			if (absentSweeps >= SeenEntityAbsenceLimit)
			{
				_expired.Add(identity);
				continue;
			}

			_seenEntities[identity] = absentSweeps + 1;
		}

		foreach (long identity in _expired)
		{
			_seenEntities.Remove(identity);
		}
	}

	private static long Pack(int x, int y) => ((long)x << 32) | (uint)y;

	private static int UnpackX(long packed) => (int)(packed >> 32);

	private static int UnpackY(long packed) => (int)(uint)packed;
}
