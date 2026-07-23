#nullable enable

using System.ComponentModel;
using Terraria.ModLoader.Config;

namespace Terrarium.Configs;

public sealed class TerrariumClientConfig : ModConfig
{
	private const float DefaultSpatialAudioItdMilliseconds = 0.65f;

	public override ConfigScope Mode => ConfigScope.ClientSide;

	[DefaultValue(true)]
	public bool BiomeAnnouncementsEnabled { get; set; } = true;

	[DefaultValue(true)]
	public bool CursorEarconsEnabled { get; set; } = true;

	[DefaultValue(35)]
	[Range(0, 100)]
	[Increment(5)]
	[Slider]
	public int CursorEarconVolumePercent { get; set; } = 35;

	[DefaultValue(true)]
	public bool CursorCoordinateAnnouncementsEnabled { get; set; } = true;

	[DefaultValue(true)]
	public bool WallToneEnabled { get; set; } = true;

	[DefaultValue(35)]
	[Range(0, 100)]
	[Increment(5)]
	[Slider]
	public int WallToneVolumePercent { get; set; } = 35;

	[DefaultValue(12)]
	[Range(4, 30)]
	[Increment(1)]
	[Slider]
	public int WallToneRangeTiles { get; set; } = 12;

	[DefaultValue(true)]
	public bool HostileMobTonesEnabled { get; set; } = true;

	[DefaultValue(30)]
	[Range(0, 100)]
	[Increment(5)]
	[Slider]
	public int HostileMobToneVolumePercent { get; set; } = 30;

	[DefaultValue(3)]
	[Range(1, 4)]
	[Increment(1)]
	[Slider]
	public int HostileMobMaximumEmitters { get; set; } = 3;

	[DefaultValue(true)]
	public bool SpatialAudioItdEnabled { get; set; } = true;

	[DefaultValue(DefaultSpatialAudioItdMilliseconds)]
	[Range(0f, 1f)]
	[Increment(0.05f)]
	[Slider]
	public float SpatialAudioItdStrengthMilliseconds { get; set; } = DefaultSpatialAudioItdMilliseconds;
}
