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

	/// <summary>
	/// Keeps the mix moving when the world pass will not run. Terraria returns out of
	/// its update before <see cref="ModSystem.PostUpdateEverything"/> on the title
	/// screen and while the game is paused, so a bus pumped only from there stops
	/// mid-queue: whatever was still sounding ends at a buffer boundary rather than
	/// through the gate's fade, and the sound guide would have no mix at all.
	///
	/// The pause flag is last frame's decision, so a frame can fall between the two
	/// hooks. The queue is deep enough to cover it, and a frame both hooks reach only
	/// tops the queue up twice.
	/// </summary>
	public override void PostUpdateInput()
	{
		if (Main.dedServ || (!Main.gameMenu && !Main.gamePaused))
		{
			return;
		}
		Bus?.Pump();
	}

	public override void Unload()
	{
		PrepareForModReload();
		_creationAttempted = false;
	}

	/// <summary>
	/// Closes FNA resources before tModLoader transfers mod unloading to its loader
	/// worker. Keeping creation marked as attempted prevents a later menu update in the
	/// same frame from recreating the bus while a reload is already committed.
	/// </summary>
	internal static void PrepareForModReload()
	{
		_creationAttempted = true;
		_bus?.Dispose();
		_bus = null;
	}
}
