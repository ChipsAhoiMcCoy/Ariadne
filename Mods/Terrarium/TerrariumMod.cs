using Terraria.ModLoader;
using Terrarium.Accessibility;

namespace Terrarium;

public sealed class TerrariumMod : Mod
{
	internal static ScreenReaderService ScreenReader { get; private set; } = new();

	public override void Load()
	{
		ScreenReader.Initialize(this);
	}

	public override void Unload()
	{
		ScreenReader.Dispose();
		ScreenReader = new ScreenReaderService();
	}
}
