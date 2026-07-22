#nullable enable

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;
using Terrarium.Audio;
using Terrarium.Configs;

namespace Terrarium.Ingame.HostileMobs;

[Autoload(Side = ModSide.Client)]
internal sealed class HostileMobToneSystem : ModSystem
{
	private const int MaximumEmitterCount = 4;
	private const float ReplacementDistanceRatio = 0.8f;
	private const float FixedUpdatesPerSecond = 60f;
	private const float InterFlightPauseSeconds = 0.16f;
	private const float CompletedCyclePauseSeconds = 0.61f;

	private readonly HostileMobTracker _tracker = new();
	private readonly EmitterAssignment[] _assignments = [new(), new(), new(), new()];
	private readonly HostileMobFlightTarget[] _audioTargets = new HostileMobFlightTarget[MaximumEmitterCount];
	private readonly Dictionary<HostileMobIdentity, HostileMobCandidate> _candidatesByIdentity = [];
	private readonly List<HostileMobCandidate> _orderedCandidates = [];
	private readonly HashSet<HostileMobIdentity> _flownThisCycle = [];
	private HostileMobToneAudioStream? _audio;
	private long _schedulerTick;
	private long _nextGlobalFlightTick;
	private bool _isReset = true;

	public override void Load()
	{
		_audio = HostileMobToneAudioStream.TryCreate(Mod);
	}

	public override void OnWorldLoad()
	{
		ResetAwareness();
		_audio?.StopAndReset();
	}

	public override void OnWorldUnload()
	{
		ResetAwareness();
		_audio?.StopAndReset();
	}

	public override void PostUpdatePlayers()
	{
		TerrariumClientConfig config = ModContent.GetInstance<TerrariumClientConfig>();
		if (!ShouldRun(config))
		{
			ResetAwareness();
			return;
		}

		_schedulerTick++;
		IReadOnlyList<HostileMobCandidate> candidates = _tracker.Capture(Main.LocalPlayer);
		int maximumEmitters = Math.Clamp(config.HostileMobMaximumEmitters, 1, MaximumEmitterCount);
		ReconcileAssignments(candidates, maximumEmitters);
		for (int index = 0; index < MaximumEmitterCount; index++)
		{
			if (index < maximumEmitters && _assignments[index].HasCandidate)
			{
				HostileMobCandidate candidate = _assignments[index].Candidate;
				_audioTargets[index] = new(
					candidate.NormalizedPosition.X,
					candidate.NormalizedPosition.Y,
					candidate.ViewportEdgeFraction);
			}
			else
			{
				_audioTargets[index] = default;
			}
		}
		_audio?.UpdateTargets(
			_audioTargets,
			maximumEmitters,
			NormalizePlayerViewportY(Main.LocalPlayer),
			config);
		ScheduleFlight(maximumEmitters);
		_isReset = false;
	}

	public override void PostUpdateInput()
	{
		TerrariumClientConfig config = ModContent.GetInstance<TerrariumClientConfig>();
		if (!ShouldRun(config))
		{
			_audio?.StopAndReset();
			ResetAwareness();
			return;
		}

		_audio?.Pump();
	}

	public override void Unload()
	{
		_audio?.Dispose();
		_audio = null;
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

	private void ScheduleFlight(int maximumEmitters)
	{
		if (_schedulerTick < _nextGlobalFlightTick)
		{
			return;
		}

		int nextFlightIndex = -1;
		for (int index = 0; index < maximumEmitters; index++)
		{
			if (!_assignments[index].HasCandidate ||
				_flownThisCycle.Contains(_assignments[index].Candidate.Identity))
			{
				continue;
			}
			if (nextFlightIndex < 0 ||
				IsHigherPriority(
					_assignments[index].Candidate,
					_assignments[nextFlightIndex].Candidate))
			{
				nextFlightIndex = index;
			}
		}

		if (nextFlightIndex < 0)
		{
			_flownThisCycle.Clear();
			return;
		}

		HostileMobCandidate candidate = _assignments[nextFlightIndex].Candidate;
		_audio?.StartFlight(nextFlightIndex);
		_flownThisCycle.Add(candidate.Identity);
		bool completedCycle = true;
		for (int index = 0; index < maximumEmitters; index++)
		{
			if (_assignments[index].HasCandidate &&
				!_flownThisCycle.Contains(_assignments[index].Candidate.Identity))
			{
				completedCycle = false;
				break;
			}
		}

		float pauseSeconds = completedCycle
			? CompletedCyclePauseSeconds
			: InterFlightPauseSeconds;
		if (completedCycle)
		{
			_flownThisCycle.Clear();
		}
		float spacingSeconds =
			HostileMobToneAudioStream.FlightDurationSeconds(candidate.ViewportEdgeFraction) +
			pauseSeconds;
		_nextGlobalFlightTick = _schedulerTick +
			Math.Max(1L, (long)MathF.Ceiling(spacingSeconds * FixedUpdatesPerSecond));
	}

	private static bool IsHigherPriority(
		in HostileMobCandidate challenger,
		in HostileMobCandidate incumbent)
	{
		return challenger.IsBoss != incumbent.IsBoss
			? challenger.IsBoss
			: challenger.DistanceSquared < incumbent.DistanceSquared;
	}

	private void Assign(int index, HostileMobCandidate candidate)
	{
		if (_assignments[index].HasCandidate)
		{
			_flownThisCycle.Remove(_assignments[index].Candidate.Identity);
		}
		_audio?.ResetEmitter(index);
		_assignments[index].HasCandidate = true;
		_assignments[index].Candidate = candidate;
	}

	private void ClearAssignment(int index)
	{
		if (_assignments[index].HasCandidate)
		{
			_flownThisCycle.Remove(_assignments[index].Candidate.Identity);
			_audio?.ResetEmitter(index);
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

	private static float NormalizePlayerViewportY(Player player)
	{
		Vector2 viewportPosition = Main.Camera.ScaledPosition;
		Vector2 viewportSize = Main.Camera.ScaledSize;
		return viewportSize.Y > 0f
			? MathHelper.Clamp(
				(player.Center.Y - viewportPosition.Y) / viewportSize.Y * 2f - 1f,
				-1f,
				1f)
			: 0f;
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

	private static bool ShouldRun(TerrariumClientConfig config)
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
		_flownThisCycle.Clear();
		_schedulerTick = 0;
		_nextGlobalFlightTick = 0;
		_isReset = true;
	}

	private sealed class EmitterAssignment
	{
		internal bool HasCandidate;
		internal HostileMobCandidate Candidate;
	}
}
