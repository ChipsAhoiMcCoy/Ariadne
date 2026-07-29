#nullable enable

namespace Ariadne.TestBenches.AudioAnalysis;

/// <summary>
/// A crowd of hostile mobs circling the listener at different radii and speeds, so
/// their distance ordering keeps changing. What matters for the audio is only where
/// each one sits in the field and how near it is, so the swarm works in those terms
/// directly rather than in world pixels.
/// </summary>
internal sealed class MobSwarm
{
	private readonly Mob[] _mobs;

	internal MobSwarm(int count)
	{
		_mobs = new Mob[count];
		for (int index = 0; index < count; index++)
		{
			// Spread deterministically rather than randomly, so a run is repeatable and
			// two mobs cross each other's radius often enough to churn the assignment.
			float spread = index / (float)Math.Max(1, count - 1);
			_mobs[index] = new Mob(
				BaseRadius: 0.18f + 0.72f * spread,
				RadiusSwing: 0.16f + 0.10f * ((index * 7) % 5) / 4f,
				RadiusRate: 0.35f + 0.55f * ((index * 3) % 7) / 6f,
				AngularRate: 0.20f + 0.45f * ((index * 5) % 9) / 8f,
				Phase: spread * MathF.Tau + index * 0.37f);
		}
	}

	internal int Count => _mobs.Length;

	internal void Sample(float seconds, Span<MobState> states)
	{
		for (int index = 0; index < _mobs.Length; index++)
		{
			Mob mob = _mobs[index];
			float radius = Math.Clamp(
				mob.BaseRadius + mob.RadiusSwing * MathF.Sin(MathF.Tau * mob.RadiusRate * seconds + mob.Phase),
				0.02f,
				1f);
			float angle = MathF.Tau * mob.AngularRate * seconds + mob.Phase;
			states[index] = new MobState(
				Identity: index,
				NormalizedX: Math.Clamp(radius * MathF.Cos(angle), -1f, 1f),
				NormalizedY: Math.Clamp(radius * MathF.Sin(angle), -1f, 1f),
				Proximity: Math.Clamp(1f - radius, 0f, 1f),
				DistanceSquared: radius * radius);
		}
	}

	private readonly record struct Mob(
		float BaseRadius,
		float RadiusSwing,
		float RadiusRate,
		float AngularRate,
		float Phase);
}

internal readonly record struct MobState(
	int Identity,
	float NormalizedX,
	float NormalizedY,
	float Proximity,
	float DistanceSquared);

/// <summary>
/// The slot policy from <c>HostileMobToneSystem.ReconcileAssignments</c>: keep an
/// incumbent while it is still present, fill anything open with the nearest unassigned
/// mob, and only displace an incumbent for a challenger meaningfully nearer than it.
/// The hysteresis ratio is the system's own, because how often a slot changes hands is
/// exactly what is being measured.
/// </summary>
internal sealed class SlotAssigner(int slotCount)
{
	private const float ReplacementDistanceRatio = 0.8f;

	private readonly int?[] _occupants = new int?[slotCount];
	private readonly float[] _distances = new float[slotCount];

	internal int Reassignments { get; private set; }

	/// <summary>
	/// Brings the slots up to date and reports which of them changed hands this tick.
	/// A slot that merely follows the mob it already holds is not a change.
	/// </summary>
	internal void Reconcile(ReadOnlySpan<MobState> mobs, Span<bool> changed, Span<MobState> assigned)
	{
		changed.Clear();

		for (int slot = 0; slot < slotCount; slot++)
		{
			if (_occupants[slot] is not int occupant)
			{
				continue;
			}

			int found = IndexOf(mobs, occupant);
			if (found < 0)
			{
				_occupants[slot] = null;
				changed[slot] = true;
				Reassignments++;
			}
			else
			{
				_distances[slot] = mobs[found].DistanceSquared;
			}
		}

		Span<int> order = stackalloc int[mobs.Length];
		for (int index = 0; index < mobs.Length; index++)
		{
			order[index] = index;
		}
		SortByDistance(mobs, order);

		foreach (int candidate in order)
		{
			MobState mob = mobs[candidate];
			if (SlotOf(mob.Identity) >= 0)
			{
				continue;
			}

			int open = FirstOpenSlot();
			if (open >= 0)
			{
				Occupy(open, mob, changed);
				continue;
			}

			int farthest = FarthestSlot();
			if (farthest >= 0 &&
				mob.DistanceSquared <= _distances[farthest] * ReplacementDistanceRatio * ReplacementDistanceRatio)
			{
				Occupy(farthest, mob, changed);
			}
		}

		for (int slot = 0; slot < slotCount; slot++)
		{
			int found = _occupants[slot] is int occupant ? IndexOf(mobs, occupant) : -1;
			assigned[slot] = found >= 0 ? mobs[found] : default;
		}
	}

	internal bool IsOccupied(int slot) => _occupants[slot] is not null;

	private void Occupy(int slot, in MobState mob, Span<bool> changed)
	{
		_occupants[slot] = mob.Identity;
		_distances[slot] = mob.DistanceSquared;
		changed[slot] = true;
		Reassignments++;
	}

	private int SlotOf(int identity)
	{
		for (int slot = 0; slot < slotCount; slot++)
		{
			if (_occupants[slot] == identity)
			{
				return slot;
			}
		}
		return -1;
	}

	private int FirstOpenSlot()
	{
		for (int slot = 0; slot < slotCount; slot++)
		{
			if (_occupants[slot] is null)
			{
				return slot;
			}
		}
		return -1;
	}

	private int FarthestSlot()
	{
		int farthest = -1;
		for (int slot = 0; slot < slotCount; slot++)
		{
			if (_occupants[slot] is not null &&
				(farthest < 0 || _distances[slot] > _distances[farthest]))
			{
				farthest = slot;
			}
		}
		return farthest;
	}

	private static int IndexOf(ReadOnlySpan<MobState> mobs, int identity)
	{
		for (int index = 0; index < mobs.Length; index++)
		{
			if (mobs[index].Identity == identity)
			{
				return index;
			}
		}
		return -1;
	}

	private static void SortByDistance(ReadOnlySpan<MobState> mobs, Span<int> order)
	{
		for (int outer = 1; outer < order.Length; outer++)
		{
			int key = order[outer];
			int inner = outer - 1;
			while (inner >= 0 && mobs[order[inner]].DistanceSquared > mobs[key].DistanceSquared)
			{
				order[inner + 1] = order[inner];
				inner--;
			}
			order[inner + 1] = key;
		}
	}
}
