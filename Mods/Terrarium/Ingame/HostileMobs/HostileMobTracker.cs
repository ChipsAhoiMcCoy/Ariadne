#nullable enable

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;

namespace Terrarium.Ingame.HostileMobs;

internal readonly record struct HostileMobIdentity(int RootNpcIndex, uint Generation);

internal readonly record struct HostileMobCandidate(
	HostileMobIdentity Identity,
	bool IsBoss,
	float DistanceSquared,
	float ViewportEdgeFraction,
	Vector2 NormalizedPosition);

/// <summary>
/// Builds one visible emitter candidate per hostile NPC health-group and tracks NPC-slot reuse.
/// </summary>
internal sealed class HostileMobTracker
{
	private const float HorizontalPositionExpansionExponent = 0.65f;

	private readonly bool[] _wasActive = new bool[Main.maxNPCs];
	private readonly int[] _lastNetIds = new int[Main.maxNPCs];
	private readonly uint[] _generations = new uint[Main.maxNPCs];
	private readonly Dictionary<HostileMobIdentity, VisibleBoundsBuilder> _builders = [];
	private readonly List<HostileMobCandidate> _candidates = [];

	internal IReadOnlyList<HostileMobCandidate> Capture(Player player)
	{
		UpdateSlotGenerations();
		_builders.Clear();
		_candidates.Clear();

		Vector2 viewportPosition = Main.Camera.ScaledPosition;
		Vector2 viewportSize = Main.Camera.ScaledSize;
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

			Rectangle hitbox = npc.Hitbox;
			float clippedLeft = MathF.Max(hitbox.Left, viewportLeft);
			float clippedTop = MathF.Max(hitbox.Top, viewportTop);
			float clippedRight = MathF.Min(hitbox.Right, viewportRight);
			float clippedBottom = MathF.Min(hitbox.Bottom, viewportBottom);
			if (clippedRight <= clippedLeft || clippedBottom <= clippedTop)
			{
				continue;
			}

			int rootIndex = ResolveRootIndex(npc, index);
			HostileMobIdentity identity = new(rootIndex, _generations[rootIndex]);
			bool isBoss = npc.boss || rootIndex != index && Main.npc[rootIndex].boss;
			if (_builders.TryGetValue(identity, out VisibleBoundsBuilder? builder))
			{
				builder.Include(clippedLeft, clippedTop, clippedRight, clippedBottom, isBoss);
			}
			else
			{
				_builders.Add(identity, new(
					clippedLeft,
					clippedTop,
					clippedRight,
					clippedBottom,
					isBoss));
			}
		}

		foreach ((HostileMobIdentity identity, VisibleBoundsBuilder builder) in _builders)
		{
			Vector2 center = builder.Center;
			Vector2 normalizedPosition = new(
				NormalizeExpandedHorizontalPosition(player.Center.X, center.X, viewportLeft, viewportRight),
				MathHelper.Clamp((center.Y - viewportTop) / viewportSize.Y * 2f - 1f, -1f, 1f));
			_candidates.Add(new(
				identity,
				builder.IsBoss,
				Vector2.DistanceSquared(player.Center, center),
				CalculateViewportEdgeFraction(player.Center, center, viewportLeft, viewportTop, viewportRight, viewportBottom),
				normalizedPosition));
		}

		return _candidates;
	}

	private static float NormalizeExpandedHorizontalPosition(
		float playerX,
		float candidateX,
		float viewportLeft,
		float viewportRight)
	{
		float offset = candidateX - playerX;
		if (MathF.Abs(offset) < 0.001f)
		{
			return 0f;
		}

		float directionalExtent = offset < 0f
			? playerX - viewportLeft
			: viewportRight - playerX;
		if (directionalExtent <= 0f)
		{
			return offset < 0f ? -1f : 1f;
		}

		float linearPosition = MathHelper.Clamp(offset / directionalExtent, -1f, 1f);
		float expandedAmount = MathF.Pow(MathF.Abs(linearPosition), HorizontalPositionExpansionExponent);
		return linearPosition < 0f ? -expandedAmount : expandedAmount;
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

	private static float CalculateViewportEdgeFraction(
		Vector2 playerCenter,
		Vector2 candidateCenter,
		float left,
		float top,
		float right,
		float bottom)
	{
		Vector2 delta = candidateCenter - playerCenter;
		if (delta.LengthSquared() < 0.001f)
		{
			return 0f;
		}

		float boundaryScale = float.PositiveInfinity;
		if (delta.X > 0f)
		{
			boundaryScale = MathF.Min(boundaryScale, (right - playerCenter.X) / delta.X);
		}
		else if (delta.X < 0f)
		{
			boundaryScale = MathF.Min(boundaryScale, (left - playerCenter.X) / delta.X);
		}
		if (delta.Y > 0f)
		{
			boundaryScale = MathF.Min(boundaryScale, (bottom - playerCenter.Y) / delta.Y);
		}
		else if (delta.Y < 0f)
		{
			boundaryScale = MathF.Min(boundaryScale, (top - playerCenter.Y) / delta.Y);
		}

		return float.IsFinite(boundaryScale) && boundaryScale > 0f
			? MathHelper.Clamp(1f / boundaryScale, 0f, 1f)
			: 1f;
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
