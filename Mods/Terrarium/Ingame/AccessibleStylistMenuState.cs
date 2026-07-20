#nullable enable

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;
using Terrarium.Menus;

namespace Terrarium.Ingame;

internal sealed class AccessibleStylistMenuState : AccessibleMenuState
{
	private readonly Player _player;
	private readonly int _originalHair;
	private readonly Color _originalHairColor;
	private readonly List<int> _availableHairstyles = [];
	private int _candidateIndex;

	internal AccessibleStylistMenuState(AccessibleMenuController controller, Player player)
		: base(controller)
	{
		_player = player;
		_originalHair = player.hair;
		_originalHairColor = player.hairColor;
	}

	protected override string Title => "Stylist";

	public override void OnActivate()
	{
		RefreshAvailableHairstyles();
		base.OnActivate();
	}

	protected override void BuildEntries(List<AccessibleMenuEntry> entries)
	{
		entries.Add(new AccessibleMenuEntry(
			DescribeCurrentHairstyle,
			NextHairstyle,
			PreviousHairstyle,
			NextHairstyle,
			description: DescribeCurrentHairstyleDetails,
			enabled: () => _availableHairstyles.Count > 0,
			role: "choice",
			adjustmentAnnouncement: () => $"{DescribeCurrentHairstyle()}. {DescribeCurrentHairstyleDetails()}"));

		entries.Add(new AccessibleMenuEntry(
			() => $"Hair color: red {_player.hairColor.R}, green {_player.hairColor.G}, blue {_player.hairColor.B}",
			() => Controller.Navigate(new AccessibleColorEditorMenuState(
				Controller,
				"Hair color",
				() => _player.hairColor,
				color => _player.hairColor = color)),
			description: () => "Change the red, green, and blue channels. A color change costs 2 gold before happiness adjustment.",
			role: "submenu"));

		entries.Add(new AccessibleMenuEntry(
			DescribePurchase,
			Purchase,
			description: DescribePurchaseDetails,
			enabled: () => _player.CanAfford(GetPrice())));

		entries.Add(new AccessibleMenuEntry(
			() => "Cancel changes",
			Cancel,
			description: () => "Restore the original hairstyle and hair color without paying."));
	}

	protected override void GoBack()
	{
		Cancel();
	}

	private void RefreshAvailableHairstyles()
	{
		bool oldHairWindow = Main.hairWindow;
		try
		{
			// Vanilla and mod hair availability conditions use this flag to distinguish
			// the Stylist catalog from character creation.
			Main.hairWindow = true;
			Main.Hairstyles.UpdateUnlocks();
			_availableHairstyles.Clear();
			_availableHairstyles.AddRange(Main.Hairstyles.AvailableHairstyles);
		}
		finally
		{
			Main.hairWindow = oldHairWindow;
		}

		_candidateIndex = Math.Max(0, _availableHairstyles.IndexOf(_player.hair));
		if (_availableHairstyles.Count > 0 && !_availableHairstyles.Contains(_player.hair))
		{
			_player.hair = _availableHairstyles[_candidateIndex];
		}
	}

	private void NextHairstyle()
	{
		MoveHairstyle(1);
	}

	private void PreviousHairstyle()
	{
		MoveHairstyle(-1);
	}

	private void MoveHairstyle(int offset)
	{
		if (_availableHairstyles.Count == 0)
		{
			return;
		}
		_candidateIndex = (_candidateIndex + offset + _availableHairstyles.Count) % _availableHairstyles.Count;
		_player.hair = _availableHairstyles[_candidateIndex];
	}

	private string DescribeCurrentHairstyle()
	{
		if (_availableHairstyles.Count == 0)
		{
			return "Hairstyle, none available";
		}

		int hairId = _availableHairstyles[_candidateIndex];
		ModHair? modHair = HairLoader.GetHair(hairId);
		string name = modHair is null
			? $"Hairstyle {hairId + 1}"
			: $"{modHair.PrettyPrintName()} from {modHair.Mod.DisplayName}";
		string changed = hairId == _originalHair ? "original" : "changed";
		return $"{name}, {changed}, {_candidateIndex + 1} of {_availableHairstyles.Count}";
	}

	private string DescribeCurrentHairstyleDetails()
	{
		if (_availableHairstyles.Count == 0)
		{
			return "No hairstyles are available from the Stylist in the current world state.";
		}

		int hairId = _availableHairstyles[_candidateIndex];
		string? description = CharacterAppearanceDescriptions.GetHairstyle(hairId);
		string cost = hairId == _originalHair
			? "This is the original hairstyle."
			: "Changing hairstyle costs 10 gold before happiness adjustment.";
		return string.IsNullOrWhiteSpace(description) ? cost : $"{description} {cost}";
	}

	private string DescribePurchase()
	{
		int price = GetPrice();
		return price == 0 ? "Keep appearance, free" : $"Purchase changes for {Main.ValueToCoins(price)}";
	}

	private string DescribePurchaseDetails()
	{
		int price = GetPrice();
		if (price == 0)
		{
			return "No paid changes are selected. Close the Stylist and keep the current appearance.";
		}
		return _player.CanAfford(price)
			? "Pay the happiness-adjusted Stylist price and apply these changes."
			: $"You cannot afford {Main.ValueToCoins(price)}.";
	}

	private int GetPrice()
	{
		int price = 0;
		if (_player.hair != _originalHair)
		{
			price += 100_000;
		}
		if (_player.hairColor != _originalHairColor)
		{
			price += 20_000;
		}
		price = (int)(price * _player.currentShoppingSettings.PriceAdjustment);
		return Math.Max(0, (int)Math.Round(price / 10_000f) * 10_000);
	}

	private void Purchase()
	{
		int price = GetPrice();
		if (!_player.BuyItem(price))
		{
			Announce($"You cannot afford the Stylist cost of {Main.ValueToCoins(price)}.");
			return;
		}

		Main.BuyHairWindow();
		CloseStylist();
	}

	private void Cancel()
	{
		_player.hair = _originalHair;
		_player.hairColor = _originalHairColor;
		CloseStylist();
	}

	private void CloseStylist()
	{
		_player.SetTalkNPC(-1);
		Main.npcChatText = string.Empty;
		Main.npcChatCornerItem = 0;
		Controller.Close();
		Main.playerInventory = false;
	}
}
