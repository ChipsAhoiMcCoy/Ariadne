#nullable enable

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria.Audio;
using Terraria.ID;
using Terraria.Localization;
using Ariadne.Audio;
using Ariadne.Configs;
using Ariadne.Ingame;

namespace Ariadne.SoundGuide;

/// <summary>One thing Enter can play, and the localization key that names it.</summary>
internal sealed record SoundGuideVariant(
	string Key,
	Action<SoundGuidePlayer, AriadneClientConfig> Play);

/// <summary>
/// One cue in the guide: what it is called, what it means, the variants worth
/// hearing separately, and the Ariadne volume it answers to so a listener can be
/// told when a cue is silent because they turned it down rather than because it is
/// broken.
/// </summary>
internal sealed class SoundGuideEntry
{
	private readonly string _key;

	internal SoundGuideEntry(
		string key,
		Func<AriadneClientConfig, int>? volumePercent,
		params SoundGuideVariant[] variants)
	{
		_key = key;
		VolumePercent = volumePercent;
		Variants = variants;
	}

	internal IReadOnlyList<SoundGuideVariant> Variants { get; }

	/// <summary>The mod volume this cue is scaled by, or null when it has none.</summary>
	internal Func<AriadneClientConfig, int>? VolumePercent { get; }

	internal string Name => Text("Name");

	internal string Description => Text("Description");

	internal string VariantName(int index) => VariantText(index, "Name");

	internal string VariantDetail(int index) => VariantText(index, "Detail");

	private string Text(string field)
	{
		return Language.GetTextValue($"Mods.Ariadne.SoundGuide.Cues.{_key}.{field}");
	}

	private string VariantText(int index, string field)
	{
		string variantKey = Variants[Math.Clamp(index, 0, Variants.Count - 1)].Key;
		return Language.GetTextValue(
			$"Mods.Ariadne.SoundGuide.Cues.{_key}.Variants.{variantKey}.{field}");
	}
}

/// <summary>
/// Every sound Ariadne makes, in the order a listener meets them: the cues that
/// answer your own movement first, then the ones that describe what is around you.
/// </summary>
internal static class SoundGuideCatalog
{
	private static readonly Vector2 Leftward = new(-1f, 0f);
	private static readonly Vector2 Rightward = new(1f, 0f);
	private static readonly Vector2 Upward = new(0f, -1f);
	private static readonly Vector2 Downward = new(0f, 1f);

	/// <summary>
	/// The tile and liquid sounds the cursor-earcon entry demonstrates. They are
	/// Terraria's own, chosen to span what the cursor answers with rather than to be
	/// exhaustive: every tile in the game reaches one of these families.
	/// </summary>
	private static readonly SoundStyle[] CursorEarconStyles =
	[
		SoundID.Dig,
		SoundID.Grass,
		SoundID.Tink,
		SoundID.Shatter,
		SoundID.Splash,
		SoundID.Shimmer1,
		SoundID.Lavafall,
	];

	/// <summary>
	/// Every resolved asset path the cursor entry can reach, so the cache can decode
	/// them while the listener is still reading the menu.
	/// </summary>
	internal static List<string> CursorEarconSoundPaths()
	{
		List<string> paths = [];
		foreach (SoundStyle style in CursorEarconStyles)
		{
			ReadOnlySpan<int> variants = style.Variants;
			if (variants.IsEmpty)
			{
				paths.Add(style.SoundPath);
				continue;
			}

			foreach (int variant in variants)
			{
				paths.Add(style.SoundPath + variant);
			}
		}
		return paths;
	}

	internal static List<SoundGuideEntry> Build()
	{
		return
		[
			new SoundGuideEntry(
				"Footsteps",
				config => config.FootstepVolumePercent,
				new("Ground", (player, config) => player.PlayFootsteps(0f, config)),
				new("Platform", (player, config) => player.PlayFootsteps(
					FootstepSoundSystem.PlatformPitch,
					config))),

			new SoundGuideEntry(
				"MovementBumps",
				config => config.MovementBumpVolumePercent,
				new("Wall", (player, config) => player.PlayMovementBump(
					MovementBumpSurface.Terrain,
					0f,
					config)),
				new("Door", (player, config) => player.PlayMovementBump(
					MovementBumpSurface.Door,
					0f,
					config)),
				new("Landing", (player, config) => player.PlayMovementBump(
					MovementBumpSurface.Terrain,
					-MovementBumpSystem.VerticalPitch,
					config)),
				new("Ceiling", (player, config) => player.PlayMovementBump(
					MovementBumpSurface.Terrain,
					MovementBumpSystem.VerticalPitch,
					config))),

			new SoundGuideEntry(
				"ElevationMovement",
				config => config.ElevationMovementCueVolumePercent,
				new("Ascending", (player, config) => player.PlayNavigationCue(
					NavigationCueKind.Ascending,
					config)),
				new("Descending", (player, config) => player.PlayNavigationCue(
					NavigationCueKind.Descending,
					config))),

			new SoundGuideEntry(
				"DropWarnings",
				config => config.DropWarningVolumePercent,
				new("Safe", (player, config) => player.PlayNavigationCue(
					NavigationCueKind.SafeDrop,
					config)),
				new("Unsafe", (player, config) => player.PlayNavigationCue(
					NavigationCueKind.UnsafeDrop,
					config))),

			new SoundGuideEntry(
				"TraversalLandmarks",
				config => config.TraversalLandmarkCueVolumePercent,
				new("Platform", (player, config) => player.PlayNavigationCue(NavigationCueKind.Platform, config)),
				new("MinecartTrack", (player, config) => player.PlayNavigationCue(NavigationCueKind.MinecartTrack, config)),
				new("Rope", (player, config) => player.PlayNavigationCue(NavigationCueKind.Rope, config))),

			new SoundGuideEntry(
				"HousingRooms",
				config => config.CursorEarconVolumePercent,
				new("Suitable", (player, config) => player.PlayNavigationCue(NavigationCueKind.HousingSuitable, config)),
				new("Occupied", (player, config) => player.PlayNavigationCue(NavigationCueKind.HousingOccupied, config)),
				new("Unsuitable", (player, config) => player.PlayNavigationCue(NavigationCueKind.HousingUnsuitable, config))),

			new SoundGuideEntry(
				"Heartbeat",
				config => config.LowHealthHeartbeatVolumePercent,
				new("Half", (player, config) => player.PlayHeartbeat(0, config)),
				new("Forty", (player, config) => player.PlayHeartbeat(1, config)),
				new("Thirty", (player, config) => player.PlayHeartbeat(2, config)),
				new("Twenty", (player, config) => player.PlayHeartbeat(3, config)),
				new("Ten", (player, config) => player.PlayHeartbeat(4, config)),
				new("Five", (player, config) => player.PlayHeartbeat(5, config))),

			new SoundGuideEntry(
				"CursorEarcons",
				config => config.CursorEarconVolumePercent,
				new("Stone", (player, config) => player.PlayCursorEarcon(SoundID.Dig, config)),
				new("Grass", (player, config) => player.PlayCursorEarcon(SoundID.Grass, config)),
				new("Metal", (player, config) => player.PlayCursorEarcon(SoundID.Tink, config)),
				new("Glass", (player, config) => player.PlayCursorEarcon(SoundID.Shatter, config)),
				new("Water", (player, config) => player.PlayCursorEarcon(SoundID.Splash, config)),
				new("Shimmer", (player, config) => player.PlayCursorEarcon(SoundID.Shimmer1, config)),
				new("Lava", (player, config) => player.PlayCursorEarcon(SoundID.Lavafall, config))),

			new SoundGuideEntry(
				"CombatTarget",
				config => config.HostileMobToneVolumePercent,
				new("Acquired", (player, config) => player.PlayCombatTargetCue(
					CombatTargetCue.Acquired,
					config)),
				new("Lost", (player, config) => player.PlayCombatTargetCue(
					CombatTargetCue.Lost,
					config)),
				new("Opened", (player, config) => player.PlayCombatTargetCue(
					CombatTargetCue.Opened,
					config)),
				new("Shielded", (player, config) => player.PlayCombatTargetCue(
					CombatTargetCue.Shielded,
					config))),

			new SoundGuideEntry(
				"Radar",
				config => config.RadarVolumePercent,
				new("Ore", (player, config) => player.PlayRadarPing(
					RadarPing.ForPipCount(1),
					Vector2.Zero,
					proximity: 1f,
					config)),
				new("Container", (player, config) => player.PlayRadarPing(
					RadarPing.ForPipCount(2),
					Vector2.Zero,
					proximity: 1f,
					config)),
				new("Creature", (player, config) => player.PlayRadarPing(
					RadarPing.ForPipCount(3),
					Vector2.Zero,
					proximity: 1f,
					config)),
				new("Other", (player, config) => player.PlayRadarPing(
					RadarPing.ForPipCount(4),
					Vector2.Zero,
					proximity: 1f,
					config)),
				new("Distant", (player, config) => player.PlayRadarPing(
					RadarPing.ForPipCount(1),
					Rightward * 0.8f,
					proximity: 0.25f,
					config))),

			new SoundGuideEntry(
				"HostileMobTones",
				config => config.HostileMobToneVolumePercent,
				new("CloseRight", (player, config) => player.SustainHostileMobTone(
					new(Rightward, 0.12f),
					isLocked: false,
					config)),
				new("HalfwayRight", (player, config) => player.SustainHostileMobTone(
					new(Rightward, 0.5f),
					isLocked: false,
					config)),
				new("EdgeRight", (player, config) => player.SustainHostileMobTone(
					new(Rightward, 0.9f),
					isLocked: false,
					config)),
				new("CloseLeft", (player, config) => player.SustainHostileMobTone(
					new(Leftward, 0.12f),
					isLocked: false,
					config)),
				new("Above", (player, config) => player.SustainHostileMobTone(
					new(Upward, 0.12f),
					isLocked: false,
					config)),
				new("Below", (player, config) => player.SustainHostileMobTone(
					new(Downward, 0.12f),
					isLocked: false,
					config)),
				new("Held", (player, config) => player.SustainHostileMobTone(
					new(Rightward, 0.12f),
					isLocked: true,
					config))),

			new SoundGuideEntry(
				"WallTones",
				config => config.WallToneVolumePercent,
				new("LeftClose", (player, config) => player.SustainWallTones(
					[new(WallToneRegion.Left, Leftward, 0.15f)],
					config)),
				new("LeftFar", (player, config) => player.SustainWallTones(
					[new(WallToneRegion.Left, Leftward, 0.85f)],
					config)),
				new("RightClose", (player, config) => player.SustainWallTones(
					[new(WallToneRegion.Right, Rightward, 0.15f)],
					config)),
				new("Corridor", (player, config) => player.SustainWallTones(
					[
						new(WallToneRegion.Left, Leftward, 0.2f),
						new(WallToneRegion.Right, Rightward, 0.2f),
					],
					config)),
				new("Ceiling", (player, config) => player.SustainWallTones(
					[new(WallToneRegion.Ceiling, Upward, 0.2f)],
					config))),

			new SoundGuideEntry(
				"BodyBeacon",
				config => config.FreecamBeaconVolumePercent,
				new("Left", (player, config) => player.SustainBodyBeacon(Leftward, config)),
				new("Right", (player, config) => player.SustainBodyBeacon(Rightward, config)),
				new("Above", (player, config) => player.SustainBodyBeacon(Upward, config)),
				new("Below", (player, config) => player.SustainBodyBeacon(Downward, config)),
				new("AtBody", (player, config) => player.SustainBodyBeacon(Vector2.Zero, config))),

			new SoundGuideEntry(
				"BreathCues",
				volumePercent: null,
				new("Submerged", (player, config) => player.PlayNativeCue(SoundID.Splash, config)),
				new("Surfaced", (player, config) => player.PlayNativeCue(SoundID.SplashWeak, config))),
		];
	}
}
