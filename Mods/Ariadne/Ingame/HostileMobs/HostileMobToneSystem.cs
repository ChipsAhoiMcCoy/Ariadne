#nullable enable

using System.Collections.Generic;
using Terraria;
using Terraria.ModLoader;
using Ariadne.Audio;
using Ariadne.Configs;
using Ariadne.Ingame.Controls;

namespace Ariadne.Ingame.HostileMobs;

/// <summary>
/// Chooses the one enemy the hostile-enemy tone follows.
///
/// One at a time. A bed of several enemies at once was a wall of tone in any crowd, and
/// a listener cannot pull four positions out of it anyway. Which one is answered by the
/// lock the player already has in their hands: while a target is held the tone stays on
/// it however many others close in, and with nothing held it follows whichever enemy is
/// nearest.
///
/// Nearest, not nearest reachable. Requiring line of sight was tried and is wrong: what
/// the tone answers is "what is closing on me", and something tunnelling toward you
/// through dirt is the case where that question matters most. Whether it can be hit yet
/// is the targeting key's business, and the key says so itself.
/// </summary>
[Autoload(Side = ModSide.Client)]
internal sealed class HostileMobToneSystem : ModSystem
{
	private const float TileSize = 16f;

	private readonly HostileMobTracker _tracker = new();
	private HostileMobToneAudioStream? _audio;
	private HostileMobIdentity? _current;
	private uint _observerRevision;
	private bool _hasObserverRevision;
	private bool _isReset = true;

	public override void Load()
	{
		_audio = HostileMobToneAudioStream.TryCreate(Mod);
	}

	public override void OnWorldLoad()
	{
		_hasObserverRevision = false;
		_observerRevision = 0;
		ResetAwareness();
		_audio?.StopAndReset();
	}

	public override void OnWorldUnload()
	{
		_hasObserverRevision = false;
		_observerRevision = 0;
		ResetAwareness();
		_audio?.StopAndReset();
	}

	public override void PostUpdatePlayers()
	{
		AriadneClientConfig config = ModContent.GetInstance<AriadneClientConfig>();
		SynchronizeObserverRevision();
		if (!ShouldRun(config))
		{
			ResetAwareness();
			return;
		}

		bool hasHeld = CombatTargetContext.TryGetSegment(out int heldSegment);
		IReadOnlyList<HostileMobCandidate> candidates = _tracker.Capture(
			SpatialObserverContext.Current,
			config.HostileMobToneRangeTiles * TileSize,
			hasHeld ? heldSegment : -1);
		bool found = TrySelect(
			hasHeld,
			heldSegment,
			candidates,
			out HostileMobCandidate selected,
			out bool isLocked);
		HostileMobIdentity? chosen = found ? selected.Identity : null;
		if (chosen != _current)
		{
			// A different enemy, or none. The voice is faded off the old one rather than
			// dragged across to the new one, which would sound like something moving.
			_audio?.Handoff();
			_current = chosen;
		}

		_audio?.UpdateTarget(
			found
				? new(
					true,
					isLocked,
					selected.NormalizedPosition.X,
					selected.NormalizedPosition.Y,
					selected.Proximity)
				: default,
			config);
		_isReset = false;
	}

	public override void PostUpdateInput()
	{
		AriadneClientConfig config = ModContent.GetInstance<AriadneClientConfig>();
		SynchronizeObserverRevision();
		if (!ShouldRun(config))
		{
			_audio?.StopAndReset();
			ResetAwareness();
		}
	}

	public override void Unload()
	{
		_audio?.Dispose();
		_audio = null;
		_observerRevision = 0;
		_hasObserverRevision = false;
		ResetAwareness();
	}

	/// <summary>
	/// Which enemy sounds this frame, and whether it is the one the player holds.
	///
	/// A held enemy is taken whatever its distance, because the lock outranks proximity
	/// for as long as the player keeps it. It still fades out past the configured range,
	/// since level is what carries distance and at that range there is no level left, and
	/// the tone falls back to the nearest enemy if the held one leaves the field
	/// entirely, which is also the point at which the lock ends.
	/// </summary>
	private bool TrySelect(
		bool hasHeld,
		int heldSegment,
		IReadOnlyList<HostileMobCandidate> candidates,
		out HostileMobCandidate selected,
		out bool isLocked)
	{
		if (hasHeld && _tracker.TryFindContaining(heldSegment, out selected))
		{
			isLocked = true;
			return true;
		}

		isLocked = false;
		return TryFindNearest(candidates, out selected);
	}

	/// <summary>
	/// The enemy nearest the listener. Distance is already measured from the point of
	/// each enemy nearest the listener rather than from its middle, so this is the
	/// nearest surface on the field and not the nearest centre.
	/// </summary>
	private static bool TryFindNearest(
		IReadOnlyList<HostileMobCandidate> candidates,
		out HostileMobCandidate selected)
	{
		selected = default;
		bool found = false;
		foreach (HostileMobCandidate candidate in candidates)
		{
			if (!found || candidate.DistanceSquared < selected.DistanceSquared)
			{
				selected = candidate;
				found = true;
			}
		}

		return found;
	}

	private static bool ShouldRun(AriadneClientConfig config)
	{
		return config.HostileMobTonesEnabled &&
			config.HostileMobToneVolumePercent > 0 &&
			Main.soundVolume > 0f &&
			GameplayAudioGate.CanListen();
	}

	private void ResetAwareness()
	{
		if (_isReset)
		{
			return;
		}

		_current = null;
		_tracker.Reset();
		_isReset = true;
	}

	private void SynchronizeObserverRevision()
	{
		uint revision = SpatialObserverContext.Revision;
		if (_hasObserverRevision && revision != _observerRevision)
		{
			_audio?.StopAndReset();
			ResetAwareness();
		}

		_observerRevision = revision;
		_hasObserverRevision = true;
	}
}
