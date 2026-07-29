#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.Localization;

namespace Ariadne.Ingame.Controls;

internal readonly record struct CombatTargetIdentity(int RootNpcIndex, uint Generation, int RootNetId);

/// <summary>
/// A locked target crossing between hittable and not, and where it was when it crossed.
/// </summary>
internal readonly record struct CombatTargetVulnerabilityCue(Vector2 Position, bool CanBeHit);

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
	/// <summary>
	/// How long a lock waits between spoken vulnerability changes. Moon Lord's hands and
	/// head shut their eye several times a second, and a line of speech for each would
	/// bury every other announcement the fight needs. The cue below is not throttled, so
	/// the rhythm of the opening stays audible; this only limits the words.
	/// </summary>
	private const int VulnerabilitySpeechCooldownTicks = 300;

	private readonly bool[] _wasActive = new bool[Main.maxNPCs];
	private readonly int[] _lastNetIds = new int[Main.maxNPCs];
	private readonly uint[] _generations = new uint[Main.maxNPCs];
	private readonly Dictionary<CombatTargetIdentity, CombatTargetGroup> _groups = [];
	private readonly List<CombatTargetGroup> _candidates = [];
	private readonly bool[] _towerOnField = new bool[CombatTargetStatus.LunarShieldSlotCount];
	private readonly int[] _lastTowerShields = new int[CombatTargetStatus.LunarShieldSlotCount];

	private CombatTargetIdentity? _selectedIdentity;
	private int _selectedSegment = -1;
	private Vector2 _lastAimPoint;
	private Vector2? _pendingCuePosition;
	private Vector2? _pendingLossPosition;
	private CombatTargetVulnerabilityCue? _pendingVulnerabilityCue;
	private bool _selectedCanBeHit = true;
	private int _vulnerabilitySpeechCooldown;
	private CombatTargetIdentity? _reacquireIdentity;
	private Vector2 _reacquireAimPoint;

	internal CombatTargetTracker()
	{
		Array.Fill(_lastTowerShields, -1);
	}

	internal bool HasTarget => _selectedIdentity.HasValue && IsSelectedSegmentCurrent();

	/// <summary>
	/// Where the cursor is put while a target is held. This is the lock-on aim rather than
	/// the segment's centre, so a moving enemy is led and an arcing weapon is given its
	/// compensation, exactly as vanilla's own lock-on does.
	/// </summary>
	internal Vector2 AimPoint => HasTarget
		? CombatTargetAim.Resolve(Main.LocalPlayer, _selectedSegment)
		: _lastAimPoint;

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
	/// Where a target was when the lock ended, once. Every ending reports, whether the
	/// enemy left on its own, the lock was stepped past, or manual aim took the cursor
	/// back: the cue is what tells the player the lock is no longer theirs.
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

	/// <summary>
	/// A held target opening or shutting, once per change. This is the cue that carries a
	/// boss part whose window is too short to speak.
	/// </summary>
	internal bool TryTakeVulnerabilityCue(out CombatTargetVulnerabilityCue cue)
	{
		if (_pendingVulnerabilityCue is CombatTargetVulnerabilityCue pending)
		{
			cue = pending;
			_pendingVulnerabilityCue = null;
			return true;
		}

		cue = default;
		return false;
	}

	internal void Update(Player player)
	{
		CaptureCandidates(player);
		if (_selectedIdentity is not CombatTargetIdentity identity)
		{
			TryReacquire(player);
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
			UpdateVulnerability(Main.npc[_selectedSegment]);
			return;
		}

		bool groupStillActive = IsGroupStillActive(identity);
		Vector2 oldAimPoint = _lastAimPoint;
		ClearTarget();
		if (groupStillActive)
		{
			// Still alive, only out of sight. The player was part way through killing it,
			// so it stays the one to take back the moment it can be hit again.
			RememberForReacquire(identity, oldAimPoint);
			AriadneMod.ScreenReader.Output(Text("Cleared"));
			return;
		}

		CombatTargetGroup? replacement = _candidates
			.OrderBy(candidate => candidate.DistanceSquaredTo(oldAimPoint))
			.FirstOrDefault();
		if (replacement is null)
		{
			AriadneMod.ScreenReader.Output(Text("Lost"));
			return;
		}

		SelectGroup(replacement, player, oldAimPoint, Text("Replaced"));
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
			CombatTargetIdentity? held = _selectedIdentity;
			Vector2 oldAimPoint = _lastAimPoint;
			ClearTarget();
			if (held is CombatTargetIdentity abandoned)
			{
				RememberForReacquire(abandoned, oldAimPoint);
			}
			AriadneMod.ScreenReader.Output(Text("NoTargets"));
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
			ClearTarget();
			return CombatTargetCycleResult.Released;
		}

		SelectGroup(ordered[currentIndex + 1], player, currentAimPoint, null);
		return CombatTargetCycleResult.Selected;
	}

	internal void CancelForManualAim()
	{
		ClearTarget();
	}

	/// <summary>
	/// Sets the lock aside for the free camera. The target is kept as the one to take
	/// back, so returning to the body resumes the fight rather than sounding a loss the
	/// camera swap would mute before it could be heard.
	/// </summary>
	internal bool Clear()
	{
		CombatTargetIdentity? held = _selectedIdentity;
		Vector2 oldAimPoint = _lastAimPoint;
		ClearTarget();
		_pendingLossPosition = null;
		if (held is CombatTargetIdentity setAside)
		{
			RememberForReacquire(setAside, oldAimPoint);
		}
		return held.HasValue;
	}

	internal void Reset()
	{
		Array.Clear(_wasActive);
		Array.Clear(_lastNetIds);
		Array.Clear(_generations);
		Array.Clear(_towerOnField);
		Array.Fill(_lastTowerShields, -1);
		_groups.Clear();
		_candidates.Clear();
		ClearTarget();
		// No world left to sound into.
		_pendingLossPosition = null;
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
		// The announcement below already states how the new target stands, so the change
		// into that state is not also sounded.
		_pendingVulnerabilityCue = null;
		_selectedCanBeHit = CombatTargetStatus.CanBeHit(Main.npc[_selectedSegment]);
		_vulnerabilitySpeechCooldown = 0;
		// Whatever the player is fighting now is the target; an older one stops waiting.
		_reacquireIdentity = null;

		string announcement = DescribeSelection(player, group, _selectedSegment);
		AriadneMod.ScreenReader.Output(
			prefix is null ? announcement : Language.GetTextValue(Key("WithPrefix"), prefix, announcement));
	}

	/// <summary>
	/// Reports a held target crossing between hittable and not. Vanilla drops a lock the
	/// moment its target stops taking damage, which is why a Moon Lord hand or a shielded
	/// pillar could never be held here either; the lock now stays, and this is what tells
	/// the player which side of that line it is currently on.
	/// </summary>
	private void UpdateVulnerability(NPC segment)
	{
		if (_vulnerabilitySpeechCooldown > 0)
		{
			_vulnerabilitySpeechCooldown--;
		}

		bool canBeHit = CombatTargetStatus.CanBeHit(segment);
		if (canBeHit == _selectedCanBeHit)
		{
			return;
		}

		_selectedCanBeHit = canBeHit;
		_pendingVulnerabilityCue = new(segment.Center, canBeHit);
		if (_vulnerabilitySpeechCooldown > 0)
		{
			return;
		}

		_vulnerabilitySpeechCooldown = VulnerabilitySpeechCooldownTicks;
		AriadneMod.ScreenReader.Output(Text(canBeHit ? "BecameVulnerable" : "BecameShielded"));
	}

	/// <summary>
	/// Takes back a target the player never gave up. An enemy that leaves the view and
	/// comes back is the one already part way to dead, so it outranks anything else on
	/// screen and the lock resumes without another key press.
	/// </summary>
	private void TryReacquire(Player player)
	{
		if (_reacquireIdentity is not CombatTargetIdentity wanted)
		{
			return;
		}

		if (!IsGroupStillActive(wanted))
		{
			_reacquireIdentity = null;
			return;
		}

		CombatTargetGroup? group = _candidates.FirstOrDefault(candidate => candidate.Identity == wanted);
		if (group is not null)
		{
			SelectGroup(group, player, _reacquireAimPoint, Text("Reacquired"));
		}
	}

	private void RememberForReacquire(CombatTargetIdentity identity, Vector2 aimPoint)
	{
		_reacquireIdentity = identity;
		_reacquireAimPoint = aimPoint;
	}

	private void CaptureCandidates(Player player)
	{
		UpdateSlotGenerations();
		_groups.Clear();
		_candidates.Clear();
		Array.Clear(_towerOnField);

		// The field, not the camera rectangle. The camera is the resolution divided by the
		// game zoom, so it decided reach by display: Moon Lord's head floats 400 pixels
		// above its core and left the camera entirely at 720p or at any zoom above one,
		// taking a part of the boss out of reach for reasons the player has no way to
		// hear. This is the same fixed field the spatial cues are measured against, so
		// anything lockable is also audible.
		Rectangle field = Utils.CenteredRectangle(player.Center, SpatialObserverSnapshot.FieldSize);
		for (int index = 0; index < Main.maxNPCs; index++)
		{
			NPC npc = Main.npc[index];
			if (!npc.active || npc.life <= 0 || !field.Intersects(npc.Hitbox))
			{
				continue;
			}

			// Noted before eligibility rather than after it, because a shield running out
			// is worth reporting from behind the terrain that would deny the lock itself.
			int towerSlot = CombatTargetStatus.LunarShieldSlot(npc.type);
			if (towerSlot >= 0)
			{
				_towerOnField[towerSlot] = true;
			}

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

		AnnounceLunarShieldDrops();
	}

	/// <summary>
	/// Says when a pillar's shield runs out. A shield spends itself on kills made anywhere
	/// nearby rather than on the pillar, so the moment it becomes hittable arrives with no
	/// sound of its own and while the player is almost certainly locked onto something
	/// else. It is therefore reported for any pillar on the field, not only a held one.
	/// </summary>
	private void AnnounceLunarShieldDrops()
	{
		for (int slot = 0; slot < _towerOnField.Length; slot++)
		{
			if (!_towerOnField[slot])
			{
				// Out of range again: forget the count so returning cannot replay a drop.
				_lastTowerShields[slot] = -1;
				continue;
			}

			int strength = CurrentTowerShield(slot);
			int previous = _lastTowerShields[slot];
			_lastTowerShields[slot] = strength;
			if (previous > 0 && strength == 0)
			{
				AriadneMod.ScreenReader.Output(
					Language.GetTextValue(Key("TowerShieldDown"), Lang.GetNPCNameValue(TowerNpcType(slot))));
			}
		}
	}

	private static int CurrentTowerShield(int slot) => slot switch
	{
		0 => NPC.ShieldStrengthTowerSolar,
		1 => NPC.ShieldStrengthTowerVortex,
		2 => NPC.ShieldStrengthTowerNebula,
		_ => NPC.ShieldStrengthTowerStardust,
	};

	private static int TowerNpcType(int slot) => slot switch
	{
		0 => NPCID.LunarTowerSolar,
		1 => NPCID.LunarTowerVortex,
		2 => NPCID.LunarTowerNebula,
		_ => NPCID.LunarTowerStardust,
	};

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

	/// <summary>
	/// Whether a living NPC already known to be on the field can be locked.
	/// </summary>
	private static bool IsEligible(Player player, NPC npc)
	{
		if (npc.friendly ||
			npc.isLikeATownNPC ||
			npc.immortal ||
			npc.CountsAsACritter)
		{
			return false;
		}

		// Deliberately no test for whether the thing can be hurt right now. Vanilla's
		// lock-on refuses anything holding dontTakeDamage, and that single flag is how
		// vanilla builds most boss structure: a Lunar pillar holds it until its shield is
		// spent, Moon Lord's core holds it until all three eyes are dead, and Moon Lord's
		// hands and head raise and drop it every time their eye shuts. Copying that rule
		// made those parts unreachable, which for a sighted player is a small annoyance
		// and here is the whole fight missing. They are offered instead, and the state is
		// spoken and sounded.
		//
		// What that rule did usefully was keep scenery out, so scenery is excluded
		// directly: something nothing can hurt and nothing will chase is not a target.
		if (npc.dontTakeDamage && !npc.chaseable)
		{
			return false;
		}

		// Vanilla's own lock-on rule, and worth keeping: a mimic still shaped like a chest
		// has not revealed itself yet.
		if (npc.aiStyle == NPCAIStyleID.Mimic && npc.ai[0] == 0f)
		{
			return false;
		}

		// Vanilla also requires the target to be lit. That is a sighted affordance, and
		// the hostile-mob tones already deliberately omit it, so a mob heard in the dark
		// would otherwise be one that cannot be locked. Line of sight below is the test
		// that actually answers whether it can be hit.
		Item item = player.HeldItem;
		if ((uint)item.type < ItemID.Sets.LockOnIgnoresCollision.Length &&
			ItemID.Sets.LockOnIgnoresCollision[item.type])
		{
			return true;
		}

		Vector2 predicted = CombatTargetAim.Resolve(player, npc.whoAmI);
		return Collision.CanHit(player.Center, 0, 0, predicted, 0, 0) ||
			Collision.CanHitLine(player.Center, 0, 0, predicted, 0, 0) ||
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
		string name = display.FullName;
		if (CombatTargetStatus.DescribePart(segment) is string part)
		{
			name = Language.GetTextValue(Key("PartOf"), name, part);
		}

		Vector2 delta = segment.Center - player.Center;
		int distance = Math.Max(0, (int)MathF.Round(delta.Length() / 16f));
		int life = Math.Max(0, display.life);
		int lifeMax = Math.Max(1, display.lifeMax);
		string direction = DescribeDirection(delta);
		return DescribeCondition(segment) is string condition
			? Language.GetTextValue(
				Key("SummaryWithCondition"), name, life, lifeMax, direction, distance, condition)
			: Language.GetTextValue(Key("Summary"), name, life, lifeMax, direction, distance);
	}

	/// <summary>
	/// Why a target cannot be hurt, when it cannot be. A pillar reports its shield as a
	/// count because the count is the fight: it falls with every enemy killed nearby, and
	/// nothing else on screen says how much of it is left.
	/// </summary>
	private static string? DescribeCondition(NPC segment)
	{
		if (CombatTargetStatus.TryGetLunarShield(segment, out int strength, out int maximum))
		{
			return strength > 0
				? Language.GetTextValue(Key("LunarShield"), strength, maximum)
				: Text("LunarShieldDown");
		}

		return CombatTargetStatus.CanBeHit(segment) ? null : Text("Shielded");
	}

	private static string DescribeDirection(Vector2 delta)
	{
		if (delta.LengthSquared() < 16f)
		{
			return Text("AtPlayer");
		}

		float angle = MathF.Atan2(delta.Y, delta.X);
		int octant = (int)MathF.Round(angle / (MathF.PI / 4f));
		return Text((((octant % 8) + 8) % 8) switch
		{
			0 => "East",
			1 => "Southeast",
			2 => "South",
			3 => "Southwest",
			4 => "West",
			5 => "Northwest",
			6 => "North",
			_ => "Northeast",
		});
	}

	private static Vector2 SafeDirection(Vector2 origin, Vector2 target)
	{
		Vector2 direction = target - origin;
		return direction.LengthSquared() < 0.001f ? Vector2.UnitX : Vector2.Normalize(direction);
	}

	/// <summary>
	/// Ends the lock. A lock that was actually held always queues the loss cue, because
	/// the sound answers "am I still on something?" and that question does not care how
	/// the lock ended. Callers that mean "set this aside" drop the cue afterwards.
	/// </summary>
	private void ClearTarget()
	{
		if (_selectedIdentity.HasValue)
		{
			_pendingLossPosition = _lastAimPoint;
		}
		else
		{
			_pendingLossPosition = null;
		}
		_selectedIdentity = null;
		_selectedSegment = -1;
		_pendingCuePosition = null;
		_pendingVulnerabilityCue = null;
		_selectedCanBeHit = true;
		_vulnerabilitySpeechCooldown = 0;
		_reacquireIdentity = null;
	}

	private static string Text(string key) => Language.GetTextValue(Key(key));

	private static string Key(string key) => $"Mods.Ariadne.CombatTarget.{key}";

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
