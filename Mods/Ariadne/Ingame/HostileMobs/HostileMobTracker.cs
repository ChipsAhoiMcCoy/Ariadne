#nullable enable

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;

namespace Ariadne.Ingame.HostileMobs;

internal readonly record struct HostileMobIdentity(int RootNpcIndex, uint Generation);

internal readonly record struct HostileMobCandidate(
	HostileMobIdentity Identity,
	bool IsBoss,
	float DistanceSquared,
	float Proximity,
	Vector2 NormalizedPosition);

/// <summary>
/// Builds one visible emitter candidate per hostile NPC health-group and tracks NPC-slot reuse.
/// </summary>
internal sealed class HostileMobTracker
{
	private readonly bool[] _wasActive = new bool[Main.maxNPCs];
	private readonly int[] _lastNetIds = new int[Main.maxNPCs];
	private readonly uint[] _generations = new uint[Main.maxNPCs];
	private readonly Dictionary<HostileMobIdentity, NearestSurfaceBuilder> _builders = [];
	private readonly List<HostileMobCandidate> _candidates = [];

	internal IReadOnlyList<HostileMobCandidate> Capture(
		SpatialObserverSnapshot observer,
		float rangePixels)
	{
		UpdateSlotGenerations();
		_builders.Clear();
		_candidates.Clear();

		Vector2 viewportPosition = observer.ViewportPosition;
		Vector2 viewportSize = observer.ViewportSize;
		if (viewportSize.X <= 0f || viewportSize.Y <= 0f)
		{
			return _candidates;
		}

		float viewportLeft = viewportPosition.X;
		float viewportTop = viewportPosition.Y;
		float viewportRight = viewportLeft + viewportSize.X;
		float viewportBottom = viewportTop + viewportSize.Y;
		for (int index = 0; index < Main.maxNPCs; index++)
		{
			NPC npc = Main.npc[index];
			if (!npc.active || npc.life <= 0 || !npc.CanBeChasedBy(ignoreDontTakeDamage: true))
			{
				continue;
			}

			// The viewport only decides whether a mob is heard at all. Clipping the hitbox
			// to it as well dragged a mob straddling an edge back toward the middle, so its
			// cue retreated from that edge over the last stretch before it left the screen.
			Rectangle hitbox = npc.Hitbox;
			if (MathF.Min(hitbox.Right, viewportRight) <= MathF.Max(hitbox.Left, viewportLeft) ||
				MathF.Min(hitbox.Bottom, viewportBottom) <= MathF.Max(hitbox.Top, viewportTop))
			{
				continue;
			}

			int rootIndex = ResolveRootIndex(npc, index);
			HostileMobIdentity identity = new(rootIndex, _generations[rootIndex]);
			bool isBoss = npc.boss || rootIndex != index && Main.npc[rootIndex].boss;
			Vector2 surface = NearestSurfacePoint(hitbox, observer.Center);
			float surfaceDistanceSquared = Vector2.DistanceSquared(observer.Center, surface);
			if (_builders.TryGetValue(identity, out NearestSurfaceBuilder? builder))
			{
				builder.Include(surface, surfaceDistanceSquared, isBoss);
			}
			else
			{
				_builders.Add(identity, new(surface, surfaceDistanceSquared, isBoss));
			}
		}

		// The viewport decides whether a mob is heard; this range decides how loud. They
		// were once the same length, which tied the level of every mob to the resolution
		// and zoom of the screen it was drawn on, and put most of a wide monitor's field
		// into the quietest part of the curve.
		float safeRangePixels = MathF.Max(rangePixels, 1f);
		foreach ((HostileMobIdentity identity, NearestSurfaceBuilder builder) in _builders)
		{
			float distanceSquared = builder.DistanceSquared;
			_candidates.Add(new(
				identity,
				builder.IsBoss,
				distanceSquared,
				MathHelper.Clamp(1f - MathF.Sqrt(distanceSquared) / safeRangePixels, 0f, 1f),
				observer.NormalizeToViewport(builder.Surface)));
		}

		return _candidates;
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
		internal NearestSurfaceBuilder(Vector2 surface, float distanceSquared, bool isBoss)
		{
			Surface = surface;
			DistanceSquared = distanceSquared;
			IsBoss = isBoss;
		}

		internal Vector2 Surface { get; private set; }

		internal float DistanceSquared { get; private set; }

		internal bool IsBoss { get; private set; }

		internal void Include(Vector2 surface, float distanceSquared, bool isBoss)
		{
			if (distanceSquared < DistanceSquared)
			{
				Surface = surface;
				DistanceSquared = distanceSquared;
			}
			IsBoss |= isBoss;
		}
	}
}
