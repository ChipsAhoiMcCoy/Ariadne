#nullable enable

using System.Collections.Generic;
using Ariadne.Logic;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent.UI.States;
using Terraria.ID;
using Terraria.UI;

namespace Ariadne.Menus;

internal sealed class AccessibleMenuController
{
	private readonly Stack<UIState> _history = [];
	private readonly bool _inGame;
	private AccessibleMainMenuState? _root;
	private UIState? _currentState;
	private MultiplayerIntent? _pendingWorldSelectionIntent;
	private int _navigationVersion;

	internal AccessibleMenuController(bool inGame = false)
	{
		_inGame = inGame;
	}

	internal bool IsInGame => _inGame;
	internal bool IsActive => _currentState is not null && IsShowing(_currentState);
	internal int NavigationVersion => _navigationVersion;

	internal bool IsShowing(UIState state)
	{
		return (_inGame ? Main.InGameUI.CurrentState : Main.MenuUI.CurrentState) == state;
	}

	internal AccessibleMainMenuState Root => _root ??= new AccessibleMainMenuState(this);

	internal void ShowRoot()
	{
		_history.Clear();
		ShowState(Root);
	}

	internal void ShowRoot(UIState state)
	{
		_history.Clear();
		ShowState(state);
	}

	internal void Navigate(UIState state)
	{
		UIState? current = _inGame ? Main.InGameUI.CurrentState : Main.MenuUI.CurrentState;
		if (current is not null && current != state)
		{
			_history.Push(current);
		}
		_navigationVersion++;
		SoundEngine.PlaySound(SoundID.MenuOpen);
		ShowState(state);
	}

	internal void Replace(UIState state)
	{
		ShowState(state);
	}

	internal void Back(bool playSound = true)
	{
		if (_history.TryPop(out UIState? previous))
		{
			_navigationVersion++;
			if (playSound)
			{
				SoundEngine.PlaySound(SoundID.MenuClose);
			}
			ShowState(previous);
			return;
		}

		if (_inGame)
		{
			Close();
		}
	}

	internal void Close()
	{
		_navigationVersion++;
		_history.Clear();
		_currentState = null;
		if (_inGame)
		{
			IngameFancyUI.Close();
		}
	}

	internal void ClearHistory()
	{
		_history.Clear();
	}

	internal void BeginWorldTransition(MultiplayerIntent returnIntent)
	{
		_navigationVersion++;
		_history.Clear();
		_currentState = null;
		_pendingWorldSelectionIntent = returnIntent;
		Main.MenuUI.SetState(null);
	}

	internal void CancelWorldTransition()
	{
		_pendingWorldSelectionIntent = null;
	}

	internal bool HandleWorldTransition()
	{
		WorldTransitionMenuAction action = WorldTransitionLogic.MenuAction(
			_pendingWorldSelectionIntent is not null,
			Main.gameMenu,
			WorldGen.generatingWorld,
			Main.menuMode,
			Main.MenuUI.CurrentState is UIWorldLoad);

		switch (action)
		{
			case WorldTransitionMenuAction.Hold:
				return true;
			case WorldTransitionMenuAction.RestoreWorldSelection:
				MultiplayerIntent intent = _pendingWorldSelectionIntent!.Value;
				_pendingWorldSelectionIntent = null;
				ShowRoot(new AccessibleWorldSelectMenuState(this, intent));
				return true;
			case WorldTransitionMenuAction.Clear:
				_pendingWorldSelectionIntent = null;
				return false;
			default:
				return false;
		}
	}

	internal void ShowConnectionStatus()
	{
		if (Main.MenuUI.CurrentState is not AccessibleConnectionStatusState)
		{
			Replace(new AccessibleConnectionStatusState(this));
		}
	}

	internal void ShowServerPasswordRequest()
	{
		if (Main.MenuUI.CurrentState is not AccessibleServerPasswordRequestState)
		{
			Replace(new AccessibleServerPasswordRequestState(this));
		}
	}

	private void ShowState(UIState state)
	{
		_currentState = state;
		if (_inGame)
		{
			IngameFancyUI.OpenUIState(state);
			return;
		}

		Main.menuMode = 888;
		Main.MenuUI.SetState(state);
	}
}
