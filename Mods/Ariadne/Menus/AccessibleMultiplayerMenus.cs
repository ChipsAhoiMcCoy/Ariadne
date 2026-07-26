#nullable enable

using System;
using System.Collections.Generic;
using System.Reflection;
using Terraria;
using Terraria.Localization;
using Terraria.ModLoader;
using Terraria.Net;
using Terraria.Social;
using Terraria.UI;

namespace Ariadne.Menus;

internal sealed class AccessibleMultiplayerMenuState : AccessibleMenuState
{
	internal AccessibleMultiplayerMenuState(AccessibleMenuController controller)
		: base(controller)
	{
	}

	protected override string Title => Lang.menu[13].Value;

	protected override void BuildEntries(List<AccessibleMenuEntry> entries)
	{
		entries.Add(new(
			() => Lang.menu[SocialAPI.Network is not null ? 146 : 87].Value,
			() => Controller.Navigate(new AccessiblePlayerSelectMenuState(Controller, MultiplayerIntent.JoinByIp))));
		if (SocialAPI.Network is not null)
		{
			entries.Add(new(
				() => Lang.menu[145].Value,
				OpenPlatformJoin,
				description: () => "Opens the platform friends and invitations interface."));
		}
		entries.Add(new(
			() => Lang.menu[88].Value,
			() => Controller.Navigate(new AccessiblePlayerSelectMenuState(Controller, MultiplayerIntent.HostAndPlay))));
	}

	private static void OpenPlatformJoin()
	{
		SocialAPI.Friends?.OpenJoinInterface();
	}
}

internal sealed class AccessibleServerAddressMenuState : AccessibleMenuState
{
	private string _address = Main.getIP ?? string.Empty;
	private int _port = Netplay.ListenPort is > 0 and <= 65535 ? Netplay.ListenPort : 7777;

	internal AccessibleServerAddressMenuState(AccessibleMenuController controller)
		: base(controller)
	{
	}

	protected override string Title => "Join via IP";

	protected override void BuildEntries(List<AccessibleMenuEntry> entries)
	{
		entries.Add(new(
			() => $"Server address: {(string.IsNullOrWhiteSpace(_address) ? "not set" : _address)}",
			EditAddress,
			role: "edit field"));
		entries.Add(new(
			() => $"Port: {_port}",
			EditPort,
			role: "edit field"));
		entries.Add(new(
			() => Lang.menu[4].Value,
			Connect,
			description: () => "Connect to the entered server address and port."));

		for (int index = 0; index < Main.recentWorld.Length && index < Main.recentIP.Length && index < Main.recentPort.Length; index++)
		{
			if (string.IsNullOrWhiteSpace(Main.recentIP[index]))
			{
				continue;
			}
			int capturedIndex = index;
			entries.Add(new(
				() => $"Recent: {RecentName(capturedIndex)}",
				() => ConnectRecent(capturedIndex),
				description: () => $"{Main.recentIP[capturedIndex]}, port {Main.recentPort[capturedIndex]}."));
		}
	}

	private void EditAddress()
	{
		Controller.Navigate(new AccessibleTextInputState(
			Controller,
			Lang.menu[89].Value.Replace(":", string.Empty),
			_address,
			256,
			value => { _address = value; Controller.Back(); }));
	}

	private void EditPort()
	{
		Controller.Navigate(new AccessibleTextInputState(
			Controller,
			Lang.menu[90].Value,
			_port.ToString(),
			5,
			value =>
			{
				if (ushort.TryParse(value, out ushort port) && port > 0)
				{
					_port = port;
					Controller.Back();
				}
				else
				{
					Announce("Enter a port from 1 through 65535.");
				}
			}));
	}

	private void ConnectRecent(int index)
	{
		_address = Main.recentIP[index];
		_port = Main.recentPort[index] is > 0 and <= 65535 ? Main.recentPort[index] : 7777;
		Connect();
	}

	private void Connect()
	{
		if (string.IsNullOrWhiteSpace(_address))
		{
			Announce("Enter a server address first.");
			return;
		}

		Main.getIP = _address.Trim();
		Netplay.ListenPort = _port;
		Main.autoPass = false;
		Main.statusText = Language.GetTextValue("Net.ConnectingTo", Main.getIP);
		AccessibleConnectionStatusState status = new(Controller);
		Controller.Navigate(status);
		Netplay.SetRemoteIPAsync(Main.getIP, () =>
		{
			Main.StartClientGameplay();
			if (Main.gameMenu)
			{
				Main.menuMode = 888;
				Main.MenuUI.SetState(status);
			}
		});
	}

	private static string RecentName(int index)
	{
		return string.IsNullOrWhiteSpace(Main.recentWorld[index])
			? $"{Main.recentIP[index]}:{Main.recentPort[index]}"
			: $"{Main.recentWorld[index]} ({Main.recentIP[index]}:{Main.recentPort[index]})";
	}
}

internal sealed class AccessibleHostConfigurationMenuState : AccessibleMenuState
{
	private static readonly MethodInfo? LaunchServer = typeof(Main).GetMethod(
		"OnSubmitServerPassword",
		BindingFlags.NonPublic | BindingFlags.Instance,
		binder: null,
		types: [typeof(string)],
		modifiers: null);

	private string _password = string.Empty;

	internal AccessibleHostConfigurationMenuState(AccessibleMenuController controller)
		: base(controller)
	{
	}

	protected override string Title => "Host and Play Options";

	protected override void BuildEntries(List<AccessibleMenuEntry> entries)
	{
		if (SocialAPI.Network is not null)
		{
			entries.Add(ToggleFlag(
				() => Main.MenuServerMode.HasFlag(ServerMode.Lobby) ? Lang.menu[137].Value : Lang.menu[136].Value,
				ServerMode.Lobby,
				"Controls whether the server uses a platform lobby."));
			entries.Add(ToggleFlag(
				() => Main.MenuServerMode.HasFlag(ServerMode.FriendsCanJoin) ? Lang.menu[139].Value : Lang.menu[138].Value,
				ServerMode.FriendsCanJoin,
				"Controls whether friends can join through the platform lobby.",
				() => Main.MenuServerMode.HasFlag(ServerMode.Lobby)));
			entries.Add(ToggleFlag(
				() => Main.MenuServerMode.HasFlag(ServerMode.FriendsOfFriends) ? Lang.menu[143].Value : Lang.menu[142].Value,
				ServerMode.FriendsOfFriends,
				"Controls whether friends of friends can join.",
				() => Main.MenuServerMode.HasFlag(ServerMode.Lobby)));
		}

		entries.Add(new(
			() => $"{Lang.menu[144].Value}: {(_password.Length == 0 ? "none" : $"{_password.Length} characters")}",
			EditPassword,
			role: "edit field"));
		entries.Add(new(
			() => Language.GetTextValue(Main.showServerConsole ? "tModLoader.MPShowServerConsoleYes" : "tModLoader.MPShowServerConsoleNo"),
			ToggleConsole,
			previousValue: ToggleConsole,
			nextValue: ToggleConsole,
			role: "toggle",
			adjustmentAnnouncement: () => ToggleState(Main.showServerConsole)));
		entries.Add(new(
			() => "Start server",
			StartServer,
			description: () => $"Host {Main.ActiveWorldFileData?.Name ?? "the selected world"} and join it."));
	}

	private AccessibleMenuEntry ToggleFlag(
		Func<string> label,
		ServerMode flag,
		string description,
		Func<bool>? enabled = null)
	{
		void Toggle()
		{
			Main.MenuServerMode ^= flag;
			if (!Main.MenuServerMode.HasFlag(ServerMode.Lobby))
			{
				Main.MenuServerMode = ServerMode.None;
			}
		}

		return new AccessibleMenuEntry(
			label,
			Toggle,
			Toggle,
			Toggle,
			() => description,
			enabled,
			"toggle",
			() => ToggleState(Main.MenuServerMode.HasFlag(flag)));
	}

	private static string ToggleState(bool enabled)
	{
		return enabled ? "On" : "Off";
	}

	private void EditPassword()
	{
		Controller.Navigate(new AccessibleTextInputState(
			Controller,
			Lang.menu[7].Value,
			_password,
			128,
			value => { _password = value; Controller.Back(); },
			hideContents: Main.HidePassword));
	}

	private static void ToggleConsole()
	{
		Main.showServerConsole = !Main.showServerConsole;
	}

	private void StartServer()
	{
		if (LaunchServer is null)
		{
			Announce("Hosting is unavailable because this tModLoader version changed its server launch API.");
			return;
		}

		try
		{
			Netplay.ServerPassword = _password;
			Controller.ClearHistory();
			LaunchServer.Invoke(Main.instance, [_password]);
			if (Main.gameMenu)
			{
				Controller.Replace(new AccessibleConnectionStatusState(Controller));
			}
		}
		catch (Exception exception)
		{
			Announce("The local server could not be started. See the tModLoader client log.");
			ModContent.GetInstance<AriadneMod>().Logger.Error("Custom Host and Play launch failed.", exception);
		}
	}
}

internal sealed class AccessibleConnectionStatusState : AccessibleMenuState
{
	private string _lastStatus = string.Empty;

	internal AccessibleConnectionStatusState(AccessibleMenuController controller)
		: base(controller)
	{
	}

	protected override string Title => "Multiplayer Connection";

	public override void Update(Microsoft.Xna.Framework.GameTime gameTime)
	{
		base.Update(gameTime);
		if (!string.Equals(_lastStatus, Main.statusText, StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(Main.statusText))
		{
			_lastStatus = Main.statusText;
			Announce(_lastStatus);
			RebuildEntries();
		}
	}

	protected override void BuildEntries(List<AccessibleMenuEntry> entries)
	{
		entries.Add(new(
			() => Lang.menu[6].Value,
			Cancel,
			description: () => string.IsNullOrWhiteSpace(Main.statusText) ? "Cancel the connection." : Main.statusText));
	}

	protected override void GoBack()
	{
		Cancel();
	}

	private void Cancel()
	{
		Netplay.InvalidateAllOngoingIPSetAttempts();
		Netplay.Disconnect = true;
		try
		{
			Netplay.Connection?.Socket?.Close();
		}
		catch
		{
			// The socket may be disposed concurrently by the network thread.
		}
		Main.netMode = 0;
		Main.menuMode = 0;
		Controller.ShowRoot();
	}
}

internal sealed class AccessibleServerPasswordRequestState : UIState
{
	private static readonly MethodInfo? SubmitPassword = typeof(Main).GetMethod(
		"OnSubmitServerPasswordFromRequest",
		BindingFlags.NonPublic | BindingFlags.Static,
		binder: null,
		types: [typeof(string)],
		modifiers: null);

	private readonly AccessibleMenuController _controller;
	private AccessibleTextInputState? _input;

	internal AccessibleServerPasswordRequestState(AccessibleMenuController controller)
	{
		_controller = controller;
	}

	public override void OnActivate()
	{
		_input ??= new AccessibleTextInputState(
			_controller,
			Lang.menu[3].Value,
			string.Empty,
			128,
			Submit,
			hideContents: Main.HidePassword,
			cancel: Cancel);
		Main.menuMode = 888;
		Main.MenuUI.SetState(_input);
	}

	private void Submit(string password)
	{
		if (SubmitPassword is null)
		{
			AriadneMod.ScreenReader.Output("The password could not be submitted because this tModLoader version changed its network API.");
			return;
		}

		Netplay.ServerPassword = password;
		SubmitPassword.Invoke(null, [password]);
		_controller.ShowConnectionStatus();
	}

	private void Cancel()
	{
		Netplay.Disconnect = true;
		Netplay.ServerPassword = string.Empty;
		Main.netMode = 0;
		Main.menuMode = 0;
		_controller.ShowRoot();
	}
}
