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
	private readonly Dictionary<HostileMobIdentity, VisibleBoundsBuilder> _builders = [];
	private readonly List<HostileMobCandidate> _candidates = [];

	internal IReadOnlyList<HostileMobCandidate> Capture(SpatialObserverSnapshot observer)
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

			// The viewport only decides whether a mob is heard at all. Clipping the bounds
			// as well dragged a mob straddling an edge back toward the middle, so its cue
			// retreated from that edge over the last stretch before it left the screen.
			Rectangle hitbox = npc.Hitbox;
			if (MathF.Min(hitbox.Right, viewportRight) <= MathF.Max(hitbox.Left, viewportLeft) ||
				MathF.Min(hitbox.Bottom, viewportBottom) <= MathF.Max(hitbox.Top, viewportTop))
			{
				continue;
			}

			int rootIndex = ResolveRootIndex(npc, index);
			HostileMobIdentity identity = new(rootIndex, _generations[rootIndex]);
			bool isBoss = npc.boss || rootIndex != index && Main.npc[rootIndex].boss;
			if (_builders.TryGetValue(identity, out VisibleBoundsBuilder? builder))
			{
				builder.Include(hitbox.Left, hitbox.Top, hitbox.Right, hitbox.Bottom, isBoss);
			}
			else
			{
				_builders.Add(identity, new(
					hitbox.Left,
					hitbox.Top,
					hitbox.Right,
					hitbox.Bottom,
					isBoss));
			}
		}

		// A screen is far wider than it is tall, so measuring range against the edge the
		// mob happens to face made a mob overhead count as far more distant than one the
		// same number of tiles to the side. The half-diagonal is the one screen-derived
		// length that does not depend on direction, so range means range again.
		float rangePixels = viewportSize.Length() * 0.5f;
		foreach ((HostileMobIdentity identity, VisibleBoundsBuilder builder) in _builders)
		{
			Vector2 center = builder.Center;
			float distanceSquared = Vector2.DistanceSquared(observer.Center, center);
			_candidates.Add(new(
				identity,
				builder.IsBoss,
				distanceSquared,
				MathHelper.Clamp(1f - MathF.Sqrt(distanceSquared) / rangePixels, 0f, 1f),
				observer.NormalizeToViewport(center)));
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

	private sealed class VisibleBoundsBuilder
	{
		private float _left;
		private float _top;
		private float _right;
		private float _bottom;

		internal VisibleBoundsBuilder(float left, float top, float right, float bottom, bool isBoss)
		{
			_left = left;
			_top = top;
			_right = right;
			_bottom = bottom;
			IsBoss = isBoss;
		}

		internal bool IsBoss { get; private set; }

		internal Vector2 Center => new((_left + _right) * 0.5f, (_top + _bottom) * 0.5f);

		internal void Include(float left, float top, float right, float bottom, bool isBoss)
		{
			_left = MathF.Min(_left, left);
			_top = MathF.Min(_top, top);
			_right = MathF.Max(_right, right);
			_bottom = MathF.Max(_bottom, bottom);
			IsBoss |= isBoss;
		}
	}
}
