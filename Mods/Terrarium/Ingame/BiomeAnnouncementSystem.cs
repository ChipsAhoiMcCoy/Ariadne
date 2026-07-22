#nullable enable

using Terraria;
using Terraria.ModLoader;
using Terrarium.Audio;
using Terrarium.Configs;

namespace Terrarium.Ingame;

[Autoload(Side = ModSide.Client)]
internal sealed class BiomeAnnouncementSystem : ModSystem
{
	private const int StableUpdateCount = 30;

	private string _announcedIdentity = string.Empty;
	private string _pendingIdentity = string.Empty;
	private string _pendingDescription = string.Empty;
	private int _pendingUpdates;

	public override void PostUpdatePlayers()
	{
		if (!ModContent.GetInstance<TerrariumClientConfig>().BiomeAnnouncementsEnabled)
		{
			ClearPending();
			return;
		}

		Player player = Main.LocalPlayer;
		if (!player.active || player.dead || player.ghost)
		{
			ResetTracking();
			return;
		}
		if (!GameplayAudioGate.CanListen())
		{
			ClearPending();
			return;
		}

		BiomeStatusSnapshot current = BiomeStatusFormatter.Capture(player);
		if (current.Identity == _announcedIdentity)
		{
			ClearPending();
			return;
		}

		if (current.Identity != _pendingIdentity)
		{
			_pendingIdentity = current.Identity;
			_pendingDescription = current.Description;
			_pendingUpdates = 1;
			return;
		}

		_pendingUpdates++;
		if (_pendingUpdates < StableUpdateCount)
		{
			return;
		}

		string prefix = _announcedIdentity.Length == 0 ? "Current biome" : "Entered";
		TerrariumMod.ScreenReader.Output($"{prefix}: {_pendingDescription}.");
		_announcedIdentity = _pendingIdentity;
		ClearPending();
	}

	public override void OnWorldLoad() => ResetTracking();

	public override void OnWorldUnload() => ResetTracking();

	private void ClearPending()
	{
		_pendingIdentity = string.Empty;
		_pendingDescription = string.Empty;
		_pendingUpdates = 0;
	}

	private void ResetTracking()
	{
		_announcedIdentity = string.Empty;
		ClearPending();
	}
}
