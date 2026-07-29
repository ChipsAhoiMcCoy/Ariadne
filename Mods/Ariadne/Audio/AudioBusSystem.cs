#nullable enable

using Terraria;
using Terraria.ModLoader;

namespace Ariadne.Audio;

/// <summary>
/// Owns the shared bus and drives it once per frame.
///
/// The pump runs from <see cref="ModSystem.PostUpdateEverything"/> because that is
/// the last of the three update hooks in a frame: Terraria calls input handling
/// first, then the world pass, and every system has therefore already set its
/// targets for the tick by the time the mix is rendered.
/// </summary>
[Autoload(Side = ModSide.Client)]
internal sealed class AudioBusSystem : ModSystem
{
	private static AriadneAudioBus? _bus;
	private static bool _creationAttempted;

	/// <summary>
	/// Created on first use rather than in <see cref="Load"/>, because tModLoader does
	/// not promise an order between systems and every cue owner wants the bus from its
	/// own load. Null once creation has been tried and failed.
	/// </summary>
	internal static AriadneAudioBus? Bus
	{
		get
		{
			if (!_creationAttempted)
			{
				_creationAttempted = true;
				_bus = AriadneAudioBus.TryCreate(ModContent.GetInstance<AriadneMod>());
			}
			return _bus;
		}
	}

	public override void PostUpdateEverything()
	{
		if (Main.dedServ)
		{
			return;
		}
		Bus?.Pump();
	}

	public override void Unload()
	{
		_bus?.Dispose();
		_bus = null;
		_creationAttempted = false;
	}
}
