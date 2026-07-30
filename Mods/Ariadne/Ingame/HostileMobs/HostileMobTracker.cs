#nullable enable

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;

namespace Ariadne.Ingame.HostileMobs;

internal readonly record struct HostileMobIdentity(int RootNpcIndex, uint Generation);

/// <summary>One enemy the tone could follow, and where it sits in the listener's field.</summary>
internal readonly record struct HostileMobCandidate(
	HostileMobIdentity Identity,
	float DistanceSquared,
	float Proximity,
	Vector2 NormalizedPosition);

/// <summary>
/// Builds one candidate per hostile NPC health-group within the field and tracks
/// NPC-slot reuse.
/// </summary>
internal sealed class HostileMobTracker
{
	private readonly bool[] _wasActive = new bool[Main.maxNPCs];
	private readonly int[] _lastNetIds = new int[Main.maxNPCs];
	private readonly uint[] _generations = new uint[Main.maxNPCs];
	private readonly Dictionary<HostileMobIdentity, NearestSurfaceBuilder> _builders = [];
	private readonly List<HostileMobCandidate> _candidates = [];

	/// <summary>
	/// Every enemy in the field that could be sounded. <paramref name="heldNpcIndex"/> is
	/// the enemy the player holds, or a negative index if none is held; it is captured
	/// whether or not it passes the test below, because a lock and this test do not agree
	/// on everything. Vanilla's chase test wants a chaseable, mortal NPC with more than
	/// five life, while the lock deliberately accepts boss structure that fails parts of
	/// that. A held enemy that could not be heard would be the one case where the tone
	/// and the targeting key disagreed about the fight in progress.
	/// </summary>
	internal IReadOnlyList<HostileMobCandidate> Capture(
		SpatialObserverSnapshot observer,
		float rangePixels,
		int heldNpcIndex)
	{
		UpdateSlotGenerations();
		_builders.Clear();
		_candidates.Clear();

		int heldRootIndex = (uint)heldNpcIndex < Main.maxNPCs && Main.npc[heldNpcIndex].active
			? ResolveRootIndex(Main.npc[heldNpcIndex], heldNpcIndex)
			: -1;
		Vector2 fieldPosition = observer.FieldPosition;
		Vector2 fieldSize = SpatialObserverSnapshot.FieldSize;
		if (fieldSize.X <= 0f || fieldSize.Y <= 0f)
		{
			return _candidates;
		}

		float fieldLeft = fieldPosition.X;
		float fieldTop = fieldPosition.Y;
		float fieldRight = fieldLeft + fieldSize.X;
		float fieldBottom = fieldTop + fieldSize.Y;
		for (int index = 0; index < Main.maxNPCs; index++)
		{
			NPC npc = Main.npc[index];
			if (!npc.active || npc.life <= 0)
			{
				continue;
			}

			// The field only decides whether a mob is heard at all. Clipping the hitbox to
			// it as well dragged a mob straddling an edge back toward the middle, so its
			// cue retreated from that edge over the last stretch before it left the field.
			Rectangle hitbox = npc.Hitbox;
			if (MathF.Min(hitbox.Right, fieldRight) <= MathF.Max(hitbox.Left, fieldLeft) ||
				MathF.Min(hitbox.Bottom, fieldBottom) <= MathF.Max(hitbox.Top, fieldTop))
			{
				continue;
			}

			int rootIndex = ResolveRootIndex(npc, index);
			if (rootIndex != heldRootIndex && !npc.CanBeChasedBy(ignoreDontTakeDamage: true))
			{
				continue;
			}

			HostileMobIdentity identity = new(rootIndex, _generations[rootIndex]);
			Vector2 surface = NearestSurfacePoint(hitbox, observer.Center);
			float surfaceDistanceSquared = Vector2.DistanceSquared(observer.Center, surface);
			if (_builders.TryGetValue(identity, out NearestSurfaceBuilder? builder))
			{
				builder.Include(surface, surfaceDistanceSquared);
			}
			else
			{
				_builders.Add(identity, new(surface, surfaceDistanceSquared));
			}
		}

		// The field decides whether a mob is heard; this range decides how loud. They are
		// kept separate so that widening one does not silently reshape the other.
		float safeRangePixels = MathF.Max(rangePixels, 1f);
		foreach ((HostileMobIdentity identity, NearestSurfaceBuilder builder) in _builders)
		{
			float distanceSquared = builder.DistanceSquared;
			_candidates.Add(new(
				identity,
				distanceSquared,
				MathHelper.Clamp(1f - MathF.Sqrt(distanceSquared) / safeRangePixels, 0f, 1f),
				observer.NormalizeToField(builder.Surface)));
		}

		return _candidates;
	}

	/// <summary>
	/// The candidate one NPC belongs to, out of the capture just taken. A held enemy is
	/// known by the segment the player locked, and a lock is held on the whole animal:
	/// locking one coil of a worm and hearing the tone from a different coil is the same
	/// enemy, so the segment is resolved to its health group before it is looked up.
	/// </summary>
	internal bool TryFindContaining(int npcIndex, out HostileMobCandidate candidate)
	{
		candidate = default;
		if ((uint)npcIndex >= Main.maxNPCs)
		{
			return false;
		}

		int rootIndex = ResolveRootIndex(Main.npc[npcIndex], npcIndex);
		foreach (HostileMobCandidate held in _candidates)
		{
			if (held.Identity.RootNpcIndex == rootIndex)
			{
				candidate = held;
				return true;
			}
		}

		return false;
	}

	internal void Reset()
	{
		Array.Clear(_wasActive);
		Array.Clear(_lastNetIds);
		Array.Clear(_generations);
		_builders.Clear();
		_candidates.Clear();
	}

	private void UpdateSlotGenerations()
	{
		for (int index = 0; index < Main.maxNPCs; index++)
		{
			NPC npc = Main.npc[index];
			if (!npc.active)
			{
				_wasActive[index] = false;
				continue;
			}

			if (!_wasActive[index] || _lastNetIds[index] != npc.netID)
			{
				_generations[index]++;
				if (_generations[index] == 0)
				{
					_generations[index] = 1;
				}
			}
			_wasActive[index] = true;
			_lastNetIds[index] = npc.netID;
		}
	}

	private static int ResolveRootIndex(NPC npc, int ownIndex)
	{
		int rootIndex = npc.realLife;
		return (uint)rootIndex < Main.maxNPCs && Main.npc[rootIndex].active
			? rootIndex
			: ownIndex;
	}

	/// <summary>
	/// Where on a hitbox a mob sounds from: the point of it nearest the listener. A body
	/// is heard from its surface rather than from its middle, and the middle is the wrong
	/// answer by half a hitbox for anything larger than a zombie.
	/// </summary>
	private static Vector2 NearestSurfacePoint(Rectangle hitbox, Vector2 listener)
	{
		return new(
			MathHelper.Clamp(listener.X, hitbox.Left, hitbox.Right),
			MathHelper.Clamp(listener.Y, hitbox.Top, hitbox.Bottom));
	}

	/// <summary>
	/// Keeps the nearest surface point across every segment of one health group. Merging
	/// the segments into a single rectangle and taking its middle placed a worm's cue at
	/// the centre of the ring it had coiled into, which is where none of it was, and the
	/// nearest point on that merged rectangle would have been no better: a worm arcing
	/// past overhead encloses the listener, and the enclosing rectangle then reports a
	/// distance of zero. The nearest of the segments' own nearest points is the one that
	/// stays on the animal.
	/// </summary>
	private sealed class NearestSurfaceBuilder
	{
		internal NearestSurfaceBuilder(Vector2 surface, float distanceSquared)
		{
			Surface = surface;
			DistanceSquared = distanceSquared;
		}

		internal Vector2 Surface { get; private set; }

		internal float DistanceSquared { get; private set; }

		internal void Include(Vector2 surface, float distanceSquared)
		{
			if (distanceSquared >= DistanceSquared)
			{
				return;
			}

			Surface = surface;
			DistanceSquared = distanceSquared;
		}
	}
}
