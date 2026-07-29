#nullable enable

using System.ComponentModel;
using Terraria.ModLoader.Config;
using Ariadne.Audio;

namespace Ariadne.Configs;

public sealed class AriadneClientConfig : ModConfig
{
	private const float DefaultSpatialAudioItdMilliseconds = 0.65f;

	/// <summary>
	/// Every cue is normalized to one reference loudness, so a percentage means the
	/// same loudness whichever cue it belongs to and every volume starts level. The
	/// default leaves a few decibels of room to raise a cue above the rest.
	/// </summary>
	private const int DefaultVolumePercent = 70;

	/// <summary>
	/// The distance at which a hostile mob falls silent. It sits past the horizontal
	/// edge of a common screen so that nothing visible is inaudible, while keeping the
	/// level of a mob a fixed number of tiles away independent of screen and zoom.
	/// </summary>
	private const int DefaultHostileMobToneRangeTiles = 80;

	public override ConfigScope Mode => ConfigScope.ClientSide;

	[DefaultValue(true)]
	public bool BiomeAnnouncementsEnabled { get; set; } = true;

	[DefaultValue(true)]
	public bool CursorEarconsEnabled { get; set; } = true;

	[DefaultValue(DefaultVolumePercent)]
	[Range(0, 100)]
	[Increment(5)]
	[Slider]
	public int CursorEarconVolumePercent { get; set; } = DefaultVolumePercent;

	[DefaultValue(true)]
	public bool CursorCoordinateAnnouncementsEnabled { get; set; } = true;

	[DefaultValue(true)]
	public bool RelativeCoordinateReadoutEnabled { get; set; } = true;

	[DefaultValue(true)]
	public bool LowHealthHeartbeatEnabled { get; set; } = true;

	[DefaultValue(DefaultVolumePercent)]
	[Range(0, 100)]
	[Increment(5)]
	[Slider]
	public int LowHealthHeartbeatVolumePercent { get; set; } = DefaultVolumePercent;

	[DefaultValue(true)]
	public bool LowHealthAnnouncementsEnabled { get; set; } = true;

	[DefaultValue(true)]
	public bool BreathAnnouncementsEnabled { get; set; } = true;

	[DefaultValue(true)]
	public bool HotbarAnnouncementsEnabled { get; set; } = true;

	[DefaultValue(true)]
	public bool ItemPickupAnnouncementsEnabled { get; set; } = true;

	[DefaultValue(true)]
	public bool MovementBumpTonesEnabled { get; set; } = true;

	[DefaultValue(DefaultVolumePercent)]
	[Range(0, 100)]
	[Increment(5)]
	[Slider]
	public int MovementBumpVolumePercent { get; set; } = DefaultVolumePercent;

	[DefaultValue(true)]
	public bool WallToneEnabled { get; set; } = true;

	[DefaultValue(DefaultVolumePercent)]
	[Range(0, 100)]
	[Increment(5)]
	[Slider]
	public int WallToneVolumePercent { get; set; } = DefaultVolumePercent;

	[DefaultValue(12)]
	[Range(4, 30)]
	[Increment(1)]
	[Slider]
	public int WallToneRangeTiles { get; set; } = 12;

	[DefaultValue(true)]
	public bool FallTonesEnabled { get; set; } = true;

	[DefaultValue(true)]
	public bool HostileMobTonesEnabled { get; set; } = true;

	[DefaultValue(DefaultVolumePercent)]
	[Range(0, 100)]
	[Increment(5)]
	[Slider]
	public int HostileMobToneVolumePercent { get; set; } = DefaultVolumePercent;

	[DefaultValue(DefaultHostileMobToneRangeTiles)]
	[Range(20, 120)]
	[Increment(5)]
	[Slider]
	public int HostileMobToneRangeTiles { get; set; } = DefaultHostileMobToneRangeTiles;

	[DefaultValue(3)]
	[Range(1, 4)]
	[Increment(1)]
	[Slider]
	public int HostileMobMaximumEmitters { get; set; } = 3;

	[DefaultValue(DefaultVolumePercent)]
	[Range(0, 100)]
	[Increment(5)]
	[Slider]
	public int FreecamBeaconVolumePercent { get; set; } = DefaultVolumePercent;

	[DefaultValue(true)]
	public bool FootstepSoundsEnabled { get; set; } = true;

	[DefaultValue(DefaultVolumePercent)]
	[Range(0, 100)]
	[Increment(5)]
	[Slider]
	public int FootstepVolumePercent { get; set; } = DefaultVolumePercent;

	[DefaultValue(true)]
	public bool SpatialAudioItdEnabled { get; set; } = true;

	[DefaultValue(DefaultSpatialAudioItdMilliseconds)]
	[Range(0f, 1f)]
	[Increment(0.05f)]
	[Slider]
	public float SpatialAudioItdStrengthMilliseconds { get; set; } = DefaultSpatialAudioItdMilliseconds;

	/// <summary>
	/// Rolls the treble off the ear the head is between, on top of the level
	/// difference the pan already applies. It is the cue the flat attenuation cannot
	/// express, but it also changes the character of a field whose width is tuned, so
	/// it stays off until a listener has compared the two.
	/// </summary>
	[DefaultValue(false)]
	public bool SpatialAudioHeadShadowEnabled { get; set; }

	internal SpatialAudioSettings ToSpatialAudioSettings()
	{
		return new(
			SpatialAudioItdEnabled,
			SpatialAudioItdStrengthMilliseconds,
			SpatialAudioHeadShadowEnabled);
	}
}
