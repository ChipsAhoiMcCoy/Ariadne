#nullable enable

using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Ariadne.Menus;

namespace Ariadne.Ingame;

internal sealed class AccessibleSignMenuState : AccessibleMenuState
{
	private readonly Player _player;
	private readonly int _signIndex;
	private string _text;

	internal AccessibleSignMenuState(
		AccessibleMenuController controller,
		Player player,
		int signIndex,
		string text)
		: base(controller)
	{
		_player = player;
		_signIndex = signIndex;
		_text = text;
	}

	protected override string Title => "Sign";

	protected override bool RightArrowOpensSubmenu => false;

	public override void OnActivate()
	{
		_player.sign = _signIndex;
		Main.editSign = false;
		Main.playerInventory = false;
		Main.npcChatText = _text;
		base.OnActivate();
	}

	public override void Update(GameTime gameTime)
	{
		if (!_player.active || _player.dead ||
			_signIndex < 0 || _signIndex >= Main.sign.Length || Main.sign[_signIndex] is null || _player.sign != _signIndex)
		{
			CloseSign("Sign closed because it is no longer available.");
			return;
		}
		base.Update(gameTime);
	}

	protected override void BuildEntries(List<AccessibleMenuEntry> entries)
	{
		entries.Add(new AccessibleMenuEntry(
			() => string.IsNullOrWhiteSpace(_text) ? "Sign: blank" : $"Sign: {_text}",
			description: () => string.IsNullOrWhiteSpace(_text) ? "This sign is blank." : _text,
			role: "information"));
		entries.Add(new AccessibleMenuEntry(
			() => "Edit sign",
			OpenEditor,
			description: () => "Edit this sign's text."));
		entries.Add(new AccessibleMenuEntry(
			() => "Close sign",
			() => CloseSign(string.Empty),
			description: () => "Stop reading this sign."));
	}

	protected override void GoBack()
	{
		CloseSign(string.Empty);
	}

	private void OpenEditor()
	{
		Main.editSign = true;
		Controller.Navigate(new AccessibleTextInputState(
			Controller,
			"Sign text",
			_text,
			1_000,
			value =>
			{
				_text = value;
				Main.npcChatText = value;
				Main.SubmitSignText();
				Controller.Back();
			},
			cancel: () =>
			{
				Main.editSign = false;
				Main.npcChatText = _text;
				Controller.Back();
			}));
	}

	private void CloseSign(string announcement)
	{
		Main.CloseNPCChatOrSign();
		Controller.Close();
		Main.playerInventory = false;
		if (!string.IsNullOrWhiteSpace(announcement))
		{
			AriadneMod.ScreenReader.Output(announcement);
		}
	}
}
