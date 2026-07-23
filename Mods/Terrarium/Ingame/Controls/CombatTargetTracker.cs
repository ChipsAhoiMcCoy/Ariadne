#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;

namespace Terrarium.Ingame.Controls;

internal readonly record struct CombatTargetIdentity(int RootNpcIndex, uint Generation, int RootNetId);

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
			TerrariumMod.ScreenReader.Output("Combat target cleared because it is no longer visible or reachable.");
			return;
		}

		CombatTargetGroup? replacement = _candidates
			.OrderBy(candidate => candidate.DistanceSquaredTo(oldAimPoint))
			.FirstOrDefault();
		if (replacement is null)
		{
			TerrariumMod.ScreenReader.Output("Combat target lost.");
			return;
		}

		SelectGroup(replacement, player, oldAimPoint, "Combat target replaced.");
	}

	internal void SelectPrevious(Player player, Vector2 currentAimPoint)
	{
		SelectSpatial(player, currentAimPoint, -1);
	}

	internal void SelectNext(Player player, Vector2 currentAimPoint)
	{
		SelectSpatial(player, currentAimPoint, 1);
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

	private void SelectSpatial(Player player, Vector2 currentAimPoint, int offset)
	{
		CaptureCandidates(player);
		if (_candidates.Count == 0)
		{
			ClearSilently();
			TerrariumMod.ScreenReader.Output("No eligible combat targets.");
			return;
		}

		if (_selectedIdentity is not CombatTargetIdentity selected)
		{
			CombatTargetGroup initial = _candidates
				.OrderByDescending(candidate => candidate.BestDirectionDot(player.Center, currentAimPoint))
				.ThenBy(candidate => candidate.DistanceSquaredTo(player.Center))
				.First();
			SelectGroup(initial, player, currentAimPoint, null);
			return;
		}

		List<CombatTargetGroup> spatial = _candidates
			.OrderBy(candidate => candidate.AngleAround(player.Center))
			.ThenBy(candidate => candidate.DistanceSquaredTo(player.Center))
			.ToList();
		int currentIndex = spatial.FindIndex(candidate => candidate.Identity == selected);
		if (currentIndex < 0)
		{
			CombatTargetGroup initial = spatial
				.OrderByDescending(candidate => candidate.BestDirectionDot(player.Center, currentAimPoint))
				.First();
			SelectGroup(initial, player, currentAimPoint, null);
			return;
		}

		int nextIndex = (currentIndex + offset + spatial.Count) % spatial.Count;
		SelectGroup(spatial[nextIndex], player, currentAimPoint, null);
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

		string announcement = DescribeSelection(player, group, _selectedSegment);
		TerrariumMod.ScreenReader.Output(prefix is null ? announcement : $"{prefix} {announcement}");
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

		internal float BestDirectionDot(Vector2 origin, Vector2 aimPoint)
		{
			Vector2 aimDirection = SafeDirection(origin, aimPoint);
			return Segments.Max(index => Vector2.Dot(
				SafeDirection(origin, Main.npc[index].Center),
				aimDirection));
		}

		internal float AngleAround(Vector2 origin)
		{
			Vector2 center = Vector2.Zero;
			foreach (int index in Segments)
			{
				center += Main.npc[index].Center;
			}
			center /= Segments.Count;
			Vector2 delta = center - origin;
			float angle = MathF.Atan2(delta.Y, delta.X);
			return angle < 0f ? angle + MathHelper.TwoPi : angle;
		}
	}
}
