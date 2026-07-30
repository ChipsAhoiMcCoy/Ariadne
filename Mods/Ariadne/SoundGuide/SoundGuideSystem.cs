#nullable enable

using Terraria.ModLoader;
using Ariadne.Configs;

namespace Ariadne.SoundGuide;

/// <summary>
/// Owns the guide's audio while a guide screen is open, and for a moment after it
/// closes.
///
/// The player is not kept for the session because its three beds would then render
/// alongside the real ones for the whole of a play session, which is the mod's
/// hottest path. It is not disposed the instant the screen closes either: removing a
/// sounding bed from the mix outright is a step in the waveform, so disposal waits
/// out the release the screen just handed it. Reopening inside that window, which is
/// what returning from the guide's own help page does, keeps the decoded native clips.
/// </summary>
[Autoload(Side = ModSide.Client)]
internal sealed class SoundGuideSystem : ModSystem
{
	private static readonly int RetirementTicks = SoundGuidePlayer.BedReleaseTicks + 4;

	private static SoundGuidePlayer? _player;
	private static int _retirementTicks;

	internal static SoundGuidePlayer? Open()
	{
		_retirementTicks = 0;
		_player ??= SoundGuidePlayer.Create(ModContent.GetInstance<AriadneMod>());
		return _player;
	}

	internal static void Close()
	{
		if (_player is null)
		{
			return;
		}

		_player.Stop(ModContent.GetInstance<AriadneClientConfig>());
		_retirementTicks = RetirementTicks;
	}

	/// <summary>
	/// Runs on the title screen as well as in the world, which is where most of the
	/// guide's life is spent.
	///
	/// The player is advanced from here rather than from the screen so that a release
	/// outlives the screen that started it. A bed handed its fade by
	/// <see cref="Close"/> needs most of a second of updates to finish getting quiet,
	/// and by then the screen is gone and would never have delivered them.
	/// </summary>
	public override void PostUpdateInput()
	{
		if (_player is null)
		{
			return;
		}

		_player.Update(ModContent.GetInstance<AriadneClientConfig>());
		if (_retirementTicks <= 0 || --_retirementTicks > 0)
		{
			return;
		}

		_player.Dispose();
		_player = null;
	}

	public override void Unload()
	{
		_player?.Dispose();
		_player = null;
		_retirementTicks = 0;
	}
}
