#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;

namespace Ariadne.Ingame.Controls;

internal readonly record struct CombatTargetIdentity(int RootNpcIndex, uint Generation, int RootNetId);

/// <summary>
/// What a cycle press did. The caller only needs to distinguish a released lock,
/// because that is the step that hands the cursor back to the player.
/// </summary>
internal enum CombatTargetCycleResult
{
	None,
	Selected,
	Released,
}

internal sealed class CombatTargetTracker
{
	private readonly bool[] _wasActive = new bool[Main.maxNPCs];
	private readonly int[] _lastNetIds = new int[Main.maxNPCs];
	private readonly uint[] _generations = new uint[Main.maxNPCs];
	private readonly Dictionary<CombatTargetIdentity, CombatTargetGroup> _groups = [];
	private readonly List<CombatTargetGroup> _candidates = [];

	private CombatTargetIdentity? _selectedIdentity;
	private int _selectedSegment = -1;
	private Vector2 _lastAimPoint;
	private Vector2? _pendingCuePosition;
	private Vector2? _pendingLossPosition;

	internal bool HasTarget => _selectedIdentity.HasValue && IsSelectedSegmentCurrent();

	internal Vector2 AimPoint => HasTarget ? Main.npc[_selectedSegment].Center : _lastAimPoint;

	internal bool TryTakeSelectionCue(out Vector2 position)
	{
		if (_pendingCuePosition is Vector2 pending)
		{
			position = pending;
			_pendingCuePosition = null;
			return true;
		}

		position = default;
		return false;
	}

	/// <summary>
	/// Where a target was when it went away on its own, once. Only a lock the player
	/// did not give up is reported: releasing one, aiming manually, or leaving the mode
	/// are all deliberate, already spoken, and do not want a sound for having worked.
	/// </summary>
	internal bool TryTakeLossCue(out Vector2 position)
	{
		if (_pendingLossPosition is Vector2 pending)
		{
			position = pending;
			_pendingLossPosition = null;
			return true;
		}

		position = default;
		return false;
	}

	internal void Update(Player player)
	{
		CaptureCandidates(player);
		if (_selectedIdentity is not CombatTargetIdentity identity)
		{
			return;
		}

		CombatTargetGroup? group = _candidates.FirstOrDefault(candidate => candidate.Identity == identity);
		if (group is not null)
		{
			if (!group.Segments.Contains(_selectedSegment))
			{
				_selectedSegment = group.Segments
					.OrderBy(index => Vector2.DistanceSquared(Main.npc[index].Center, _lastAimPoint))
					.First();
			}
			_lastAimPoint = Main.npc[_selectedSegment].Center;
			return;
		}

		bool groupStillActive = IsGroupStillActive(identity);
		Vector2 oldAimPoint = _lastAimPoint;
		ClearSilently();
		if (groupStillActive)
		{
			_pendingLossPosition = oldAimPoint;
			AriadneMod.ScreenReader.Output("Combat target cleared because it is no longer visible or reachable.");
			return;
		}

		CombatTargetGroup? replacement = _candidates
			.OrderBy(candidate => candidate.DistanceSquaredTo(oldAimPoint))
			.FirstOrDefault();
		if (replacement is null)
		{
			_pendingLossPosition = oldAimPoint;
			AriadneMod.ScreenReader.Output("Combat target lost.");
			return;
		}

		SelectGroup(replacement, player, oldAimPoint, "Combat target replaced.");
	}

	/// <summary>
	/// Advances one step through the eligible targets ordered nearest to farthest.
	/// Stepping past the farthest one releases the lock, so a single key can both
	/// take a target and give it back.
	/// </summary>
	internal CombatTargetCycleResult CycleNext(Player player, Vector2 currentAimPoint)
	{
		CaptureCandidates(player);
		if (_candidates.Count == 0)
		{
			// A lock held until this press is one the enemy ended, not the player.
			bool heldATarget = _selectedIdentity.HasValue;
			Vector2 oldAimPoint = _lastAimPoint;
			ClearSilently();
			if (heldATarget)
			{
				_pendingLossPosition = oldAimPoint;
			}
			AriadneMod.ScreenReader.Output("No eligible combat targets.");
			return CombatTargetCycleResult.None;
		}

		List<CombatTargetGroup> ordered = _candidates
			.OrderBy(candidate => candidate.DistanceSquaredTo(player.Center))
			.ToList();
		int currentIndex = _selectedIdentity is CombatTargetIdentity selected
			? ordered.FindIndex(candidate => candidate.Identity == selected)
			: -1;
		if (currentIndex == ordered.Count - 1)
		{
			ClearSilently();
			return CombatTargetCycleResult.Released;
		}

		SelectGroup(ordered[currentIndex + 1], player, currentAimPoint, null);
		return CombatTargetCycleResult.Selected;
	}

	internal void CancelForManualAim()
	{
		ClearSilently();
	}

	internal bool Clear()
	{
		bool hadTarget = _selectedIdentity.HasValue;
		ClearSilently();
		return hadTarget;
	}

	internal void Reset()
	{
		Array.Clear(_wasActive);
		Array.Clear(_lastNetIds);
		Array.Clear(_generations);
		_groups.Clear();
		_candidates.Clear();
		ClearSilently();
		_lastAimPoint = Vector2.Zero;
	}

	private void SelectGroup(
		CombatTargetGroup group,
		Player player,
		Vector2 referenceAimPoint,
		string? prefix)
	{
		Vector2 direction = referenceAimPoint - player.Center;
		if (direction.LengthSquared() < 0.001f)
		{
			direction = new Vector2(player.direction == 0 ? 1 : player.direction, 0f);
		}
		direction.Normalize();

		_selectedIdentity = group.Identity;
		_selectedSegment = group.Segments
			.OrderByDescending(index => Vector2.Dot(
				SafeDirection(player.Center, Main.npc[index].Center),
				direction))
			.ThenBy(index => Vector2.DistanceSquared(Main.npc[index].Center, referenceAimPoint))
			.First();
		_lastAimPoint = Main.npc[_selectedSegment].Center;
		_pendingCuePosition = _lastAimPoint;
		// A target handed straight to another one was replaced, not lost.
		_pendingLossPosition = null;

		string announcement = DescribeSelection(player, group, _selectedSegment);
		AriadneMod.ScreenReader.Output(prefix is null ? announcement : $"{prefix} {announcement}");
	}

	private void CaptureCandidates(Player player)
	{
		UpdateSlotGenerations();
		_groups.Clear();
		_candidates.Clear();

		for (int index = 0; index < Main.maxNPCs; index++)
		{
			NPC npc = Main.npc[index];
			if (!IsEligible(player, npc))
			{
				continue;
			}

			int rootIndex = ResolveRootIndex(npc, index);
			NPC root = Main.npc[rootIndex];
			int rootNetId = root.active ? root.netID : _lastNetIds[rootIndex];
			CombatTargetIdentity identity = new(rootIndex, _generations[rootIndex], rootNetId);
			if (!_groups.TryGetValue(identity, out CombatTargetGroup? group))
			{
				group = new CombatTargetGroup(identity);
				_groups.Add(identity, group);
				_candidates.Add(group);
			}
			group.Segments.Add(index);
		}
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

	private static bool IsEligible(Player player, NPC npc)
	{
		if (!npc.active ||
			npc.life <= 0 ||
			npc.dontTakeDamage ||
			npc.immortal ||
			npc.friendly ||
			npc.isLikeATownNPC ||
			!npc.CanBeChasedBy(player))
		{
			return false;
		}

		Rectangle viewport = new(
			(int)MathF.Floor(Main.Camera.ScaledPosition.X),
			(int)MathF.Floor(Main.Camera.ScaledPosition.Y),
			Math.Max(1, (int)MathF.Ceiling(Main.Camera.ScaledSize.X)),
			Math.Max(1, (int)MathF.Ceiling(Main.Camera.ScaledSize.Y)));
		if (!viewport.Intersects(npc.Hitbox))
		{
			return false;
		}

		Vector3 light = Lighting.GetSubLight(npc.Center);
		if (light.Length() / 3f < 0.03f)
		{
			return false;
		}

		Item item = player.HeldItem;
		if ((uint)item.type < ItemID.Sets.LockOnIgnoresCollision.Length &&
			ItemID.Sets.LockOnIgnoresCollision[item.type])
		{
			return true;
		}

		Vector2 predictedPosition = npc.Center + npc.velocity * 20f;
		return Collision.CanHit(player.Center, 0, 0, predictedPosition, 0, 0) ||
			Collision.CanHitLine(player.Center, 0, 0, predictedPosition, 0, 0) ||
			Collision.CanHit(player.Center, 0, 0, npc.Center, 0, 0) ||
			Collision.CanHitLine(player.Center, 0, 0, npc.Center, 0, 0);
	}

	private bool IsGroupStillActive(CombatTargetIdentity identity)
	{
		if ((uint)identity.RootNpcIndex >= Main.maxNPCs ||
			_generations[identity.RootNpcIndex] != identity.Generation)
		{
			return false;
		}

		NPC root = Main.npc[identity.RootNpcIndex];
		if (root.active &&
			root.life > 0 &&
			root.netID == identity.RootNetId)
		{
			return true;
		}

		for (int index = 0; index < Main.maxNPCs; index++)
		{
			NPC npc = Main.npc[index];
			if (npc.active &&
				npc.life > 0 &&
				npc.realLife == identity.RootNpcIndex)
			{
				return true;
			}
		}
		return false;
	}

	private bool IsSelectedSegmentCurrent()
	{
		return (uint)_selectedSegment < Main.maxNPCs &&
			Main.npc[_selectedSegment].active &&
			Main.npc[_selectedSegment].life > 0;
	}

	private static int ResolveRootIndex(NPC npc, int ownIndex)
	{
		int rootIndex = npc.realLife;
		return (uint)rootIndex < Main.maxNPCs
			? rootIndex
			: ownIndex;
	}

	private static string DescribeSelection(Player player, CombatTargetGroup group, int selectedSegment)
	{
		NPC segment = Main.npc[selectedSegment];
		NPC root = Main.npc[group.Identity.RootNpcIndex];
		NPC display = root.active ? root : segment;
		Vector2 delta = segment.Center - player.Center;
		int distance = Math.Max(0, (int)MathF.Round(delta.Length() / 16f));
		return $"{display.FullName}, {Math.Max(0, display.life)}/{Math.Max(1, display.lifeMax)} HP, " +
			$"{DescribeDirection(delta)}, {distance} tiles away.";
	}

	private static string DescribeDirection(Vector2 delta)
	{
		if (delta.LengthSquared() < 16f)
		{
			return "at your position";
		}

		float angle = MathF.Atan2(delta.Y, delta.X);
		int octant = (int)MathF.Round(angle / (MathF.PI / 4f));
		return (((octant % 8) + 8) % 8) switch
		{
			0 => "east",
			1 => "southeast",
			2 => "south",
			3 => "southwest",
			4 => "west",
			5 => "northwest",
			6 => "north",
			_ => "northeast",
		};
	}

	private static Vector2 SafeDirection(Vector2 origin, Vector2 target)
	{
		Vector2 direction = target - origin;
		return direction.LengthSquared() < 0.001f ? Vector2.UnitX : Vector2.Normalize(direction);
	}

	private void ClearSilently()
	{
		_selectedIdentity = null;
		_selectedSegment = -1;
		_pendingCuePosition = null;
		// Callers that mean "the enemy ended this" set the loss afterwards; clearing it
		// here is what keeps a deliberate release from inheriting an earlier one.
		_pendingLossPosition = null;
	}

	private sealed class CombatTargetGroup
	{
		internal CombatTargetGroup(CombatTargetIdentity identity)
		{
			Identity = identity;
		}

		internal CombatTargetIdentity Identity { get; }

		internal List<int> Segments { get; } = [];

		internal float DistanceSquaredTo(Vector2 point)
		{
			return Segments.Min(index => Vector2.DistanceSquared(Main.npc[index].Center, point));
		}

	}
}
