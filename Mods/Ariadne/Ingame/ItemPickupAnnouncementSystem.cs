#nullable enable

using System;
using System.Collections.Generic;
using Terraria;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;
using Ariadne.Audio;
using Ariadne.Configs;

namespace Ariadne.Ingame;

/// <summary>
/// Speaks each item that reaches the player's inventory, which the game otherwise
/// shows only as floating pickup text. An item is announced on the update it arrives
/// and then stays silent for as long as it keeps arriving, so mining a vein reports
/// the block once instead of once per swing. Collecting anything else releases the
/// hold, and the earlier item speaks again the next time it turns up.
/// </summary>
[Autoload(Side = ModSide.Client)]
internal sealed class ItemPickupAnnouncementSystem : ModSystem
{
	private const int MaximumTrackedItems = 16;

	/// <summary>The value ceiling Terraria itself applies to a merged coin total.</summary>
	private const long MaximumCoinValue = 999999999L;

	private const string CoinKey = "coins";

	/// <summary>
	/// Items collected since the last update, in the order they arrived. Repeat
	/// arrivals of one item merge into its existing entry so a single update that
	/// delivers several of the same block reports one total.
	/// </summary>
	private readonly List<PendingPickup> _pending = [];

	private string? _lastAnnouncedKey;

	public override void OnWorldLoad() => Reset();

	public override void OnWorldUnload() => Reset();

	internal void Report(Item item, int stack)
	{
		if (stack <= 0 || item.IsAir)
		{
			return;
		}

		// Hearts, mana stars, and nebula boosters are consumed on contact and never
		// occupy a slot, so the game reports them as an effect rather than a pickup.
		if (ItemID.Sets.IsAPickup[item.type] || ItemID.Sets.NebulaPickup[item.type])
		{
			return;
		}

		// Standing on an item with no room still runs the pickup hook, so confirm the
		// item has somewhere to go before claiming the player received it.
		if (!Main.LocalPlayer.ItemSpace(item).CanTakeItem)
		{
			return;
		}

		if (item.IsACoin)
		{
			long value = CoinValue(item.type) * stack;
			if (value <= 0)
			{
				return;
			}

			PendingPickup? coins = Track(CoinKey, string.Empty, isCoin: true);
			if (coins is not null)
			{
				coins.Amount = Math.Min(coins.Amount + value, MaximumCoinValue);
			}
			return;
		}

		string name = item.AffixName();
		PendingPickup? entry = Track(name, name, isCoin: false);
		if (entry is not null)
		{
			entry.Amount += stack;
		}
	}

	public override void PostUpdatePlayers()
	{
		if (!ModContent.GetInstance<AriadneClientConfig>().ItemPickupAnnouncementsEnabled)
		{
			Reset();
			return;
		}

		Player player = Main.LocalPlayer;
		if (!player.active || player.dead || player.ghost)
		{
			Reset();
			return;
		}

		// Menus, chat, and the inventory narrate themselves. Hold everything collected
		// so far rather than dropping it, and release it once gameplay resumes.
		if (!GameplayAudioGate.CanListen())
		{
			return;
		}

		foreach (PendingPickup entry in _pending)
		{
			if (entry.Amount <= 0 || entry.Key == _lastAnnouncedKey)
			{
				continue;
			}

			Announce(entry);
			_lastAnnouncedKey = entry.Key;
		}

		_pending.Clear();
	}

	private void Reset()
	{
		_pending.Clear();
		_lastAnnouncedKey = null;
	}

	private PendingPickup? Track(string key, string name, bool isCoin)
	{
		foreach (PendingPickup existing in _pending)
		{
			if (existing.Key == key)
			{
				return existing;
			}
		}

		if (_pending.Count >= MaximumTrackedItems)
		{
			return null;
		}

		PendingPickup created = new(key, name, isCoin);
		_pending.Add(created);
		return created;
	}

	private static void Announce(PendingPickup entry)
	{
		string description;
		if (entry.IsCoin)
		{
			description = Main.ValueToCoins(entry.Amount);
		}
		else
		{
			description = entry.Amount > 1
				? $"{entry.Name}, {entry.Amount}"
				: entry.Name;
		}

		AriadneMod.ScreenReader.Output(
			Language.GetTextValue("Mods.Ariadne.Announcements.ItemPickedUp", description),
			interrupt: false);
	}

	private static long CoinValue(int type) => type switch
	{
		ItemID.CopperCoin => 1L,
		ItemID.SilverCoin => 100L,
		ItemID.GoldCoin => 10000L,
		ItemID.PlatinumCoin => 1000000L,
		_ => 0L,
	};

	private sealed class PendingPickup(string key, string name, bool isCoin)
	{
		internal string Key { get; } = key;

		internal string Name { get; } = name;

		internal bool IsCoin { get; } = isCoin;

		/// <summary>Stack size, or merged copper value while <see cref="IsCoin"/>.</summary>
		internal long Amount { get; set; }
	}
}

/// <summary>
/// Routes the two supported ways an item reaches the local player's inventory into
/// the shared announcer. Fishing hands its catch straight to the inventory without
/// ever creating a world item, so it never reaches the pickup hook.
/// </summary>
[Autoload(Side = ModSide.Client)]
internal sealed class ItemPickupAnnouncementPlayer : ModPlayer
{
	public override bool OnPickup(Item item)
	{
		if (Player.whoAmI == Main.myPlayer)
		{
			ModContent.GetInstance<ItemPickupAnnouncementSystem>().Report(item, item.stack);
		}
		return true;
	}

	public override void ModifyCaughtFish(Item fish)
	{
		ModContent.GetInstance<ItemPickupAnnouncementSystem>().Report(fish, fish.stack);
	}
}
