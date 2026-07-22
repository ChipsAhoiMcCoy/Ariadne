#nullable enable

using System.ComponentModel;
using Terraria.ModLoader.Config;

namespace Terrarium.Configs;

public enum WallToneSpatializationMode
{
	Binaural,
	StereoPan,
}

public sealed class TerrariumClientConfig : ModConfig
{
	public override ConfigScope Mode => ConfigScope.ClientSide;

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

	[DefaultValue(WallToneSpatializationMode.Binaural)]
	public WallToneSpatializationMode WallToneSpatialization { get; set; } = WallToneSpatializationMode.Binaural;

	[DefaultValue(0.65f)]
	[Range(0f, 1f)]
	[Increment(0.05f)]
	[Slider]
	public float WallToneItdMilliseconds { get; set; } = 0.65f;

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

	[DefaultValue(WallToneSpatializationMode.Binaural)]
	public WallToneSpatializationMode HostileMobToneSpatialization { get; set; } = WallToneSpatializationMode.Binaural;

	[DefaultValue(0.65f)]
	[Range(0f, 1f)]
	[Increment(0.05f)]
	[Slider]
	public float HostileMobToneItdMilliseconds { get; set; } = 0.65f;
}
