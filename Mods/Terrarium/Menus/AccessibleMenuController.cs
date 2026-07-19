#nullable enable

using System.Collections.Generic;
using Terraria;
using Terraria.UI;

namespace Terrarium.Menus;

internal sealed class AccessibleMenuController
{
	private readonly Stack<UIState> _history = [];
	private AccessibleMainMenuState? _root;

	internal AccessibleMainMenuState Root => _root ??= new AccessibleMainMenuState(this);

	internal void ShowRoot()
	{
		_history.Clear();
		ShowState(Root);
	}

	internal void Navigate(UIState state)
	{
		if (Main.MenuUI.CurrentState is UIState current && current != state)
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

	private static void ShowState(UIState state)
	{
		Main.menuMode = 888;
		Main.MenuUI.SetState(state);
	}
}
