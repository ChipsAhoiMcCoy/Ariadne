#nullable enable

using System.Collections.Generic;
using Terraria;
using Terraria.UI;

namespace Terrarium.Menus;

internal sealed class AccessibleMenuController
{
	private readonly Stack<UIState> _history = [];
	private readonly bool _inGame;
	private AccessibleMainMenuState? _root;
	private UIState? _currentState;

	internal AccessibleMenuController(bool inGame = false)
	{
		_inGame = inGame;
	}

	internal bool IsInGame => _inGame;
	internal bool IsActive => _currentState is not null && IsShowing(_currentState);

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
		ShowState(state);
	}

	internal void Replace(UIState state)
	{
		ShowState(state);
	}

	internal void Back()
	{
		if (_history.TryPop(out UIState? previous))
		{
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
