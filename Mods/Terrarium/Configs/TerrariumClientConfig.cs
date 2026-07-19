#nullable enable

using System.ComponentModel;
using Terraria.ModLoader.Config;

namespace Terrarium.Configs;

public enum TerrariumTestMode
{
	Standard,
	Brief,
	Detailed,
}

public sealed class TerrariumClientConfig : ModConfig
{
	public override ConfigScope Mode => ConfigScope.ClientSide;

	[DefaultValue(true)]
	public bool TestToggle { get; set; } = true;

	[DefaultValue(5)]
	[Range(0, 10)]
	[Increment(1)]
	[Slider]
	public int TestLevel { get; set; } = 5;

	[DefaultValue(TerrariumTestMode.Standard)]
	public TerrariumTestMode TestMode { get; set; } = TerrariumTestMode.Standard;
}
