#nullable enable

using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Ariadne.Menus;

namespace Ariadne.Ingame;

internal sealed class AccessibleDresserMenuState : AccessibleMenuState
{
	private readonly Player _player;
	private readonly AppearanceSnapshot _original;

	internal AccessibleDresserMenuState(AccessibleMenuController controller, Player player)
		: base(controller)
	{
		_player = player;
		_original = AppearanceSnapshot.Capture(player);
	}

	protected override string Title => "Dresser";

	protected override void BuildEntries(List<AccessibleMenuEntry> entries)
	{
		entries.Add(new AccessibleMenuEntry(
			() => $"Clothing style {_player.skinVariant + 1} of {PlayerVariantID.Count}",
			NextClothingStyle,
			PreviousClothingStyle,
			NextClothingStyle,
			description: () => CharacterAppearanceDescriptions.GetClothingStyle(_player.skinVariant),
			role: "choice",
			adjustmentAnnouncement: () => $"Clothing style {_player.skinVariant + 1} of {PlayerVariantID.Count}. {CharacterAppearanceDescriptions.GetClothingStyle(_player.skinVariant)}"));

		entries.Add(ColorEntry("Shirt color", () => _player.shirtColor, value => _player.shirtColor = value));
		entries.Add(ColorEntry("Undershirt color", () => _player.underShirtColor, value => _player.underShirtColor = value));
		entries.Add(ColorEntry("Pants color", () => _player.pantsColor, value => _player.pantsColor = value));
		entries.Add(ColorEntry("Shoe color", () => _player.shoeColor, value => _player.shoeColor = value));
		entries.Add(ColorEntry("Eye color", () => _player.eyeColor, value => _player.eyeColor = value));
		entries.Add(ColorEntry("Skin color", () => _player.skinColor, value => _player.skinColor = value));

		entries.Add(new AccessibleMenuEntry(
			() => "Apply appearance",
			Apply,
			description: () => "Save the selected clothing style and colors."));
		entries.Add(new AccessibleMenuEntry(
			() => "Cancel changes",
			Cancel,
			description: () => "Restore the appearance used before opening the dresser."));
	}

	protected override void GoBack()
	{
		Cancel();
	}

	private AccessibleMenuEntry ColorEntry(string name, System.Func<Color> get, System.Action<Color> set)
	{
		return new AccessibleMenuEntry(
			() => $"{name}: red {get().R}, green {get().G}, blue {get().B}",
			() => Controller.Navigate(new AccessibleColorEditorMenuState(Controller, name, get, set)),
			description: () => "Open red, green, and blue channel controls.",
			role: "submenu");
	}

	private void NextClothingStyle()
	{
		_player.skinVariant = (_player.skinVariant + 1) % PlayerVariantID.Count;
	}

	private void PreviousClothingStyle()
	{
		_player.skinVariant = (_player.skinVariant + PlayerVariantID.Count - 1) % PlayerVariantID.Count;
	}

	private void Apply()
	{
		Main.SaveClothesWindow();
		Controller.Close();
		Main.playerInventory = false;
	}

	private void Cancel()
	{
		_original.Restore(_player);
		Controller.Close();
		Main.playerInventory = false;
	}

	private readonly record struct AppearanceSnapshot(
		int ClothingStyle,
		Color Shirt,
		Color Undershirt,
		Color Pants,
		Color Shoes,
		Color Eyes,
		Color Skin)
	{
		internal static AppearanceSnapshot Capture(Player player)
		{
			return new AppearanceSnapshot(
				player.skinVariant,
				player.shirtColor,
				player.underShirtColor,
				player.pantsColor,
				player.shoeColor,
				player.eyeColor,
				player.skinColor);
		}

		internal void Restore(Player player)
		{
			player.skinVariant = ClothingStyle;
			player.shirtColor = Shirt;
			player.underShirtColor = Undershirt;
			player.pantsColor = Pants;
			player.shoeColor = Shoes;
			player.eyeColor = Eyes;
			player.skinColor = Skin;
		}
	}
}
