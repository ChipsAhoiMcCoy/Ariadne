#nullable enable

using System.Threading;
using Terraria;
using Terraria.ModLoader;

namespace Terrarium.Ingame.Scanner;

[Autoload(Side = ModSide.Client)]
internal sealed class ScannerNpcIdentity : GlobalNPC
{
	private static long _nextIdentity;

	public override bool InstancePerEntity => true;

	internal long Identity { get; private set; }

	public override void SetDefaults(NPC entity)
	{
		Identity = Interlocked.Increment(ref _nextIdentity);
	}
}

[Autoload(Side = ModSide.Client)]
internal sealed class ScannerItemIdentity : GlobalItem
{
	private static long _nextIdentity;

	public override bool InstancePerEntity => true;

	internal long Identity { get; private set; }

	public override void SetDefaults(Item entity)
	{
		Identity = Interlocked.Increment(ref _nextIdentity);
	}
}
