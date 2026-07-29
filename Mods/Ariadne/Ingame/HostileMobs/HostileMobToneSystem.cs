#nullable enable

using System;
using System.Collections.Generic;
using Terraria;
using Terraria.ModLoader;
using Ariadne.Audio;
using Ariadne.Configs;

namespace Ariadne.Ingame.HostileMobs;

[Autoload(Side = ModSide.Client)]
internal sealed class HostileMobToneSystem : ModSystem
{
	private const int MaximumEmitterCount = 4;
	private const float ReplacementDistanceRatio = 0.8f;
	private const float TileSize = 16f;

	private readonly HostileMobTracker _tracker = new();
	private readonly EmitterAssignment[] _assignments = [new(), new(), new(), new()];
	private readonly HostileMobToneTarget[] _audioTargets = new HostileMobToneTarget[MaximumEmitterCount];
	private readonly Dictionary<HostileMobIdentity, HostileMobCandidate> _candidatesByIdentity = [];
	private readonly List<HostileMobCandidate> _orderedCandidates = [];
	private HostileMobToneAudioStream? _audio;
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

		IReadOnlyList<HostileMobCandidate> candidates = _tracker.Capture(
			SpatialObserverContext.Current,
			config.HostileMobToneRangeTiles * TileSize);
		int maximumEmitters = Math.Clamp(config.HostileMobMaximumEmitters, 1, MaximumEmitterCount);
		ReconcileAssignments(candidates, maximumEmitters);
		for (int index = 0; index < MaximumEmitterCount; index++)
		{
			if (index < maximumEmitters && _assignments[index].HasCandidate)
			{
				HostileMobCandidate candidate = _assignments[index].Candidate;
				_audioTargets[index] = new(
					true,
					candidate.NormalizedPosition.X,
					candidate.NormalizedPosition.Y,
					candidate.Proximity);
			}
			else
			{
				_audioTargets[index] = default;
			}
		}
		_audio?.UpdateTargets(_audioTargets, config);
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

	private void ReconcileAssignments(IReadOnlyList<HostileMobCandidate> candidates, int maximumEmitters)
	{
		_candidatesByIdentity.Clear();
		for (int index = 0; index < candidates.Count; index++)
		{
			_candidatesByIdentity[candidates[index].Identity] = candidates[index];
		}

		for (int index = 0; index < MaximumEmitterCount; index++)
		{
			if (index >= maximumEmitters)
			{
				ClearAssignment(index);
			}
			else if (_assignments[index].HasCandidate &&
				_candidatesByIdentity.TryGetValue(_assignments[index].Candidate.Identity, out HostileMobCandidate updated))
			{
				_assignments[index].Candidate = updated;
			}
			else if (_assignments[index].HasCandidate)
			{
				ClearAssignment(index);
			}
		}

		_orderedCandidates.Clear();
		for (int index = 0; index < candidates.Count; index++)
		{
			_orderedCandidates.Add(candidates[index]);
		}
		_orderedCandidates.Sort(static (left, right) =>
		{
			int bossComparison = right.IsBoss.CompareTo(left.IsBoss);
			return bossComparison != 0
				? bossComparison
				: left.DistanceSquared.CompareTo(right.DistanceSquared);
		});

		foreach (HostileMobCandidate challenger in _orderedCandidates)
		{
			if (FindAssignment(challenger.Identity, maximumEmitters) >= 0)
			{
				continue;
			}

			int openIndex = FindOpenAssignment(maximumEmitters);
			if (openIndex >= 0)
			{
				Assign(openIndex, challenger);
				continue;
			}

			int replacementIndex = FindReplacement(challenger, maximumEmitters);
			if (replacementIndex >= 0)
			{
				Assign(replacementIndex, challenger);
			}
		}
	}

	private int FindReplacement(HostileMobCandidate challenger, int maximumEmitters)
	{
		int farthestMatchingPriority = -1;
		int farthestNonBoss = -1;
		for (int index = 0; index < maximumEmitters; index++)
		{
			HostileMobCandidate incumbent = _assignments[index].Candidate;
			if (!incumbent.IsBoss &&
				(farthestNonBoss < 0 || incumbent.DistanceSquared > _assignments[farthestNonBoss].Candidate.DistanceSquared))
			{
				farthestNonBoss = index;
			}
			if (incumbent.IsBoss == challenger.IsBoss &&
				(farthestMatchingPriority < 0 || incumbent.DistanceSquared > _assignments[farthestMatchingPriority].Candidate.DistanceSquared))
			{
				farthestMatchingPriority = index;
			}
		}

		if (challenger.IsBoss && farthestNonBoss >= 0)
		{
			return farthestNonBoss;
		}
		if (farthestMatchingPriority < 0)
		{
			return -1;
		}

		float incumbentDistance = _assignments[farthestMatchingPriority].Candidate.DistanceSquared;
		if (challenger.IsBoss)
		{
			return challenger.DistanceSquared < incumbentDistance ? farthestMatchingPriority : -1;
		}
		float replacementThreshold = incumbentDistance * ReplacementDistanceRatio * ReplacementDistanceRatio;
		return challenger.DistanceSquared <= replacementThreshold ? farthestMatchingPriority : -1;
	}

	private void Assign(int index, HostileMobCandidate candidate)
	{
		_audio?.RetireEmitter(index);
		_assignments[index].HasCandidate = true;
		_assignments[index].Candidate = candidate;
	}

	private void ClearAssignment(int index)
	{
		if (_assignments[index].HasCandidate)
		{
			_audio?.RetireEmitter(index);
		}
		_assignments[index].HasCandidate = false;
		_assignments[index].Candidate = default;
	}

	private int FindAssignment(HostileMobIdentity identity, int maximumEmitters)
	{
		for (int index = 0; index < maximumEmitters; index++)
		{
			if (_assignments[index].HasCandidate && _assignments[index].Candidate.Identity == identity)
			{
				return index;
			}
		}
		return -1;
	}

	private int FindOpenAssignment(int maximumEmitters)
	{
		for (int index = 0; index < maximumEmitters; index++)
		{
			if (!_assignments[index].HasCandidate)
			{
				return index;
			}
		}
		return -1;
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

		for (int index = 0; index < MaximumEmitterCount; index++)
		{
			_assignments[index].HasCandidate = false;
			_assignments[index].Candidate = default;
			_audioTargets[index] = default;
		}
		_tracker.Reset();
		_candidatesByIdentity.Clear();
		_orderedCandidates.Clear();
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

	private sealed class EmitterAssignment
	{
		internal bool HasCandidate;
		internal HostileMobCandidate Candidate;
	}
}
