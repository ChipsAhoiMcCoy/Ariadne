#nullable enable

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Localization;
using Terraria.ModLoader;
using Ariadne.Audio;
using Ariadne.Configs;
using Ariadne.Ingame.Controls;
using Ariadne.Ingame.Freecam;
using Ariadne.Ingame.Scanner;
using Ariadne.Logic;

namespace Ariadne.Ingame.Radar;

/// <summary>
/// Says when there is something nearby worth opening the scanner for.
///
/// The scanner answers what and where, but only when asked, and nothing ever told the
/// listener that asking was worth it. A player could walk past a chest, a copper vein and
/// a bound NPC without a single hint that a key press would have found all three.
///
/// It reports change rather than state. A sonar that pinged on a timer would be one more
/// thing sounding continuously alongside the wall tones and the hostile bed, and a sound
/// that is always there is a sound that stops being heard. This one is silent while the
/// listener stands still and speaks up when something arrives, which is the sentence
/// "there is something here" and nothing more.
/// </summary>
[Autoload(Side = ModSide.Client)]
internal sealed class RadarSystem : ModSystem
{
	/// <summary>
	/// How often the world is swept. Twice a second is far slower than the cues that
	/// track motion, because a discovery is not a moving target, and the sweep reuses the
	/// scanner's full tile pass, which is not something to run every frame.
	/// </summary>
	private const int SweepIntervalTicks = 30;

	/// <summary>
	/// How far apart two contacts sound. The longest ping is four strikes and runs 260
	/// milliseconds, so this leaves the previous one finished before the next begins;
	/// overlapping them would make the strikes uncountable, and the count is the only
	/// thing saying what was found. Kept just past that length rather than comfortably
	/// past it, because a sweep of several contacts pays this gap for every one of them.
	/// </summary>
	private const int PingSpacingTicks = 17;

	/// <summary>
	/// How many discoveries one sweep may sound, and how deep the queue behind them may
	/// get. Walking into a lit cavern can turn up a dozen contacts at once; capping the
	/// pair turns that into a trickle over the next few seconds instead of a burst, and
	/// anything held back simply stays undiscovered until a later sweep reaches it.
	/// </summary>
	private const int MaxDiscoveryPingsPerSweep = 2;

	private const int MaxQueuedPings = 4;

	private const ulong ManualSnapshotResetTicks = 4UL * 60UL;

	private readonly RadarContactTracker _tracker = new();
	private readonly Queue<PendingPing> _pending = new();
	private readonly List<RadarContact> _manualSnapshot = [];
	private readonly FixedSnapshotCursor _manualCursor = new();
	private RadarPingSound? _sound;
	private int _ticksUntilSweep;
	private int _ticksUntilNextPing;
	private bool _passiveEnabled = true;
	private uint _observerRevision;
	private bool _hasObserverRevision;

	public override void Load()
	{
		_sound = RadarPingSound.Create(Mod);
	}

	public override void OnWorldLoad() => ResetTracking(resetPassiveToggle: true);

	public override void OnWorldUnload() => ResetTracking(resetPassiveToggle: true);

	public override void PostUpdatePlayers()
	{
		AriadneClientConfig config = ModContent.GetInstance<AriadneClientConfig>();
		SynchronizeObserverRevision();
		if (!IsAvailable(config))
		{
			ResetTracking(resetPassiveToggle: false);
			return;
		}

		// The queue is pumped before the passive test, because a manual sweep fills it
		// from this same tick's input pass and is answered whether or not passive
		// discovery is switched on.
		bool canSound = CanSound(config);
		DrainPending(config, canSound);
		if (!_passiveEnabled)
		{
			return;
		}

		if (_ticksUntilSweep > 0)
		{
			_ticksUntilSweep--;
			return;
		}

		_ticksUntilSweep = SweepIntervalTicks;
		Sweep(config, canSound);
	}

	public override void PostUpdateInput()
	{
		// Reading a mod keybind before PlayerInput has taken the mod's triggers into its
		// key status throws, and the exception costs every system that runs after this one
		// in the same update. The world gate below would catch it, but only after the read.
		if (Main.gameMenu)
		{
			return;
		}

		AriadneClientConfig config = ModContent.GetInstance<AriadneClientConfig>();
		SynchronizeObserverRevision();
		if (!config.RadarEnabled)
		{
			ResetTracking(resetPassiveToggle: false);
			return;
		}

		if (AriadneMod.RadarSweepKeybind?.JustPressed != true ||
			FreecamSystem.IsActive ||
			!WorldInputContext.IsUnobstructedGameplay())
		{
			return;
		}

		if (AriadneMod.CombatTargetModifierKeybind?.Current == true)
		{
			TogglePassiveRadar();
			return;
		}

		PerformManualSweep(config);
	}

	public override void Unload()
	{
		_sound?.Dispose();
		_sound = null;
		ResetTracking(resetPassiveToggle: true);
	}

	/// <summary>
	/// One pass of discovery. Contacts arrive nearest first, so a cap on how many may
	/// sound spends what it has on the ones closest to the listener.
	/// </summary>
	private void Sweep(AriadneClientConfig config, bool canSound)
	{
		IReadOnlyList<RadarContact> contacts =
			_tracker.Capture(SpatialObserverContext.Current, config);
		int sounded = 0;
		foreach (RadarContact contact in contacts)
		{
			if (!_tracker.IsNew(contact))
			{
				continue;
			}

			// While a menu or the chat line holds the listener, the sweep keeps its
			// record current but sounds nothing, so closing that screen does not empty a
			// backlog of everything walked past while it was open.
			if (!canSound)
			{
				_tracker.MarkSeen(contact);
				continue;
			}

			if (sounded >= MaxDiscoveryPingsPerSweep || _pending.Count >= MaxQueuedPings)
			{
				// Deliberately left unrecorded, so a later sweep still finds it.
				break;
			}

			_tracker.MarkSeen(contact);
			Enqueue(contact);
			sounded++;
		}
	}

	/// <summary>
	/// Captures a nearest-first snapshot on the first press, then advances exactly one
	/// frozen contact per press. Four idle seconds starts a fresh snapshot.
	/// </summary>
	private void PerformManualSweep(AriadneClientConfig config)
	{
		_pending.Clear();
		_ticksUntilNextPing = 0;
		ulong now = Main.GameUpdateCount;
		bool expired = _manualCursor.ShouldRefresh(
			_manualSnapshot.Count,
			now,
			ManualSnapshotResetTicks);
		if (expired)
		{
			_manualSnapshot.Clear();
			_manualSnapshot.AddRange(_tracker.Capture(
				SpatialObserverContext.Current,
				config,
				includeDroppedItems: true));
			_manualCursor.Refresh(_manualSnapshot.Count, now);
			foreach (RadarContact contact in _manualSnapshot)
			{
				_tracker.MarkSeen(contact);
			}
		}
		if (_manualSnapshot.Count == 0)
		{
			// Spoken whatever the speech setting, because silence would be the same
			// answer as a key that did nothing.
			AriadneMod.ScreenReader.Output(Language.GetTextValue("Mods.Ariadne.Radar.SweepEmpty"));
			return;
		}

		RadarContact selected = _manualSnapshot[_manualCursor.Advance(now)];
		_sound?.Play(
			selected.WorldPosition,
			RadarPing.ForPipCount(selected.PipCount),
			selected.Proximity,
			config);
		Announce(config, DescribeContact(selected));
	}

	/// <summary>
	/// Names the nearest contacts and says which way each one lies.
	///
	/// Direction only, without the straight-line total the cursor leads with. Four
	/// contacts are named at once here, so anything said about one of them is said four
	/// times over, and the level of the ping already answered how far away it is.
	/// </summary>
	private static string DescribeContact(in RadarContact contact)
	{
		string name = contact.Target.Kind == ScannerTargetKind.DroppedItem && contact.Target.Stack > 1
			? $"{contact.Name}, stack of {contact.Target.Stack}"
			: contact.Name;
		return Language.GetTextValue(
			"Mods.Ariadne.Radar.SweepContact",
			name,
			WorldPositionFormatter.DescribeDirection(contact.WorldPosition));
	}

	private void TogglePassiveRadar()
	{
		_passiveEnabled = !_passiveEnabled;
		if (!_passiveEnabled)
		{
			_pending.Clear();
			_ticksUntilNextPing = 0;
		}

		AriadneMod.ScreenReader.Output(Language.GetTextValue(_passiveEnabled
			? "Mods.Ariadne.Radar.PassiveOn"
			: "Mods.Ariadne.Radar.PassiveOff"));
	}

	private static void Announce(AriadneClientConfig config, string text)
	{
		if (config.RadarSweepSpeechEnabled)
		{
			AriadneMod.ScreenReader.Output(text);
		}
	}

	private void Enqueue(in RadarContact contact)
	{
		_pending.Enqueue(new(contact.WorldPosition, contact.PipCount, contact.Proximity));
	}

	/// <summary>
	/// Lets one queued contact sound. A contact waiting its turn keeps the position it was
	/// found at rather than following a creature that has since moved, which is right for
	/// what this cue is: it marks a discovery, and a discovery happened somewhere.
	/// </summary>
	private void DrainPending(AriadneClientConfig config, bool canSound)
	{
		if (_pending.Count == 0)
		{
			_ticksUntilNextPing = 0;
			return;
		}

		if (!canSound)
		{
			_pending.Clear();
			_ticksUntilNextPing = 0;
			return;
		}

		if (_ticksUntilNextPing > 0)
		{
			_ticksUntilNextPing--;
			return;
		}

		PendingPing ping = _pending.Dequeue();
		_sound?.Play(
			ping.WorldPosition,
			RadarPing.ForPipCount(ping.PipCount),
			ping.Proximity,
			config);
		_ticksUntilNextPing = PingSpacingTicks;
	}

	/// <summary>
	/// Whether the radar has anything to do. Deliberately not the same test as whether it
	/// may sound: the record of what the listener already knows has to stay current
	/// through a menu, or every screen would be followed by a burst of stale discoveries.
	/// </summary>
	private static bool IsAvailable(AriadneClientConfig config)
	{
		if (!config.RadarEnabled || Main.dedServ || FreecamSystem.IsActive)
		{
			return false;
		}

		Player player = Main.LocalPlayer;
		return player.active && !player.dead && !player.ghost;
	}

	private static bool CanSound(AriadneClientConfig config)
	{
		return config.RadarVolumePercent > 0 &&
			Main.soundVolume > 0f &&
			GameplayAudioGate.CanListen();
	}

	private void ResetTracking(bool resetPassiveToggle)
	{
		_tracker.Reset();
		_pending.Clear();
		_manualSnapshot.Clear();
		_manualCursor.Reset();
		_ticksUntilSweep = 0;
		_ticksUntilNextPing = 0;
		if (resetPassiveToggle)
		{
			_passiveEnabled = true;
			_hasObserverRevision = false;
			_observerRevision = 0;
		}
	}

	/// <summary>
	/// Forgets everything when the listening body jumps. Freecam and teleports move the
	/// observer discontinuously, and without this the whole of the new surroundings would
	/// be suppressed as already known or announced as newly arrived, depending on which
	/// way the jump went.
	/// </summary>
	private void SynchronizeObserverRevision()
	{
		uint revision = SpatialObserverContext.Revision;
		if (_hasObserverRevision && revision != _observerRevision)
		{
			ResetTracking(resetPassiveToggle: false);
		}

		_observerRevision = revision;
		_hasObserverRevision = true;
	}

	private readonly record struct PendingPing(
		Vector2 WorldPosition,
		int PipCount,
		float Proximity);
}
