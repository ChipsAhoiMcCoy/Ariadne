#nullable enable

using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.Enums;
using Terraria.ModLoader;
using Ariadne.Audio;
using Ariadne.Configs;
using Ariadne.Ingame.Controls;
using Ariadne.Logic;

namespace Ariadne.Ingame.Housing;

internal enum HousingRoomState
{
	Suitable,
	Occupied,
	Unsuitable,
}

internal sealed record HousingRoom(
	Point Seed,
	Rectangle TileBounds,
	HousingRoomState State,
	string Detail)
{
	internal Vector2 WorldCenter => new(
		(TileBounds.Left + TileBounds.Width * 0.5f) * 16f,
		(TileBounds.Top + TileBounds.Height * 0.5f) * 16f);
}

internal static class HousingRoomNavigator
{
	internal static int FindNearest(IReadOnlyList<HousingRoom> rooms, Vector2 playerTile)
	{
		return SemanticCursorLogic.FindNearest(ToBounds(rooms), playerTile.X, playerTile.Y);
	}

	internal static int FindDirectional(
		IReadOnlyList<HousingRoom> rooms,
		int currentIndex,
		Point direction)
	{
		return SemanticCursorLogic.FindDirectional(
			ToBounds(rooms),
			currentIndex,
			direction.X,
			direction.Y);
	}

	private static List<SemanticBounds> ToBounds(IReadOnlyList<HousingRoom> rooms)
	{
		List<SemanticBounds> bounds = new(rooms.Count);
		foreach (HousingRoom room in rooms)
		{
			Rectangle item = room.TileBounds;
			bounds.Add(new(item.Left, item.Top, item.Right, item.Bottom));
		}
		return bounds;
	}
}

[Autoload(Side = ModSide.Client)]
internal sealed class HousingQuerySystem : ModSystem
{
	private readonly List<HousingRoom> _rooms = [];
	private NavigationCueSoundBank? _sounds;
	private KeyboardState _previousKeyboard;
	private int _focus;

	internal static bool IsActive { get; private set; }

	public override void Load() => _sounds = NavigationCueSoundBank.Create(Mod);

	public override void OnWorldLoad() => Close(announce: false);

	public override void OnWorldUnload() => Close(announce: false);

	internal static void RequestOpen()
	{
		if (Main.gameMenu)
		{
			return;
		}

		Main.playerInventory = false;
		Main.LocalPlayer.releaseInventory = false;
		ModContent.GetInstance<HousingQuerySystem>().RefreshAndOpen();
	}

	public override void PostUpdateInput()
	{
		if (Main.gameMenu)
		{
			Close(announce: false);
			return;
		}

		KeyboardState keyboard = Keyboard.GetState();
		if (!IsActive)
		{
			if (AriadneMod.HousingQueryKeybind?.JustPressed == true &&
				WorldInputContext.IsUnobstructedGameplay())
			{
				RefreshAndOpen();
			}
			_previousKeyboard = keyboard;
			return;
		}

		bool refreshPressed = AriadneMod.HousingQueryKeybind?.JustPressed == true;
		AccessibleInputSuppression.ConsumeAllGameplayTriggers();
		if (Pressed(keyboard, Keys.Escape))
		{
			Close(announce: true);
		}
		else if (refreshPressed)
		{
			RefreshAndOpen();
		}
		else if (Pressed(keyboard, Keys.Enter))
		{
			QueryFocusedRoom();
		}
		else if (Pressed(keyboard, Keys.Left))
		{
			MoveFocus(new(-1, 0));
		}
		else if (Pressed(keyboard, Keys.Right))
		{
			MoveFocus(new(1, 0));
		}
		else if (Pressed(keyboard, Keys.Up))
		{
			MoveFocus(new(0, -1));
		}
		else if (Pressed(keyboard, Keys.Down))
		{
			MoveFocus(new(0, 1));
		}

		_previousKeyboard = keyboard;
	}

	public override void PreUpdatePlayers()
	{
		if (IsActive)
		{
			AccessibleInputSuppression.ConsumeAllGameplayTriggers();
		}
	}

	public override void Unload()
	{
		_sounds?.Dispose();
		_sounds = null;
		Close(announce: false);
	}

	private void RefreshAndOpen()
	{
		_rooms.Clear();
		_rooms.AddRange(HousingRoomDiscovery.CaptureVisibleRooms());
		if (_rooms.Count == 0)
		{
			IsActive = false;
			AriadneMod.ScreenReader.Output("No enclosed rooms are visible.");
			return;
		}

		Vector2 playerTile = Main.LocalPlayer.Center / 16f;
		_focus = HousingRoomNavigator.FindNearest(_rooms, playerTile);
		IsActive = true;
		_previousKeyboard = Keyboard.GetState();
		AnnounceFocus("Housing query mode. Use Arrow keys to move between rooms, Enter to run Terraria's live query, U to refresh visible rooms, and Escape to exit. ");
	}

	private void MoveFocus(Point direction)
	{
		int next = HousingRoomNavigator.FindDirectional(_rooms, _focus, direction);
		if (next == _focus)
		{
			AriadneMod.ScreenReader.Output("No room in that direction.");
			return;
		}
		_focus = next;
		AnnounceFocus(string.Empty);
	}

	private void AnnounceFocus(string prefix)
	{
		HousingRoom room = _rooms[_focus];
		AriadneClientConfig config = ModContent.GetInstance<AriadneClientConfig>();
		_sounds?.PlaySpatial(
			CueKind(room.State),
			room.WorldCenter,
			config.CursorEarconVolumePercent / 100f,
			config);
		AriadneMod.ScreenReader.Output(
			$"{prefix}{DescribeState(room)}, {WorldPositionFormatter.DescribeDirection(room.WorldCenter)}. Room {_focus + 1} of {_rooms.Count}.");
	}

	private void QueryFocusedRoom()
	{
		HousingRoom room = _rooms[_focus];
		bool suitable = WorldGen.MoveTownNPC(room.Seed.X, room.Seed.Y, -1);
		if (suitable)
		{
			// Query mode is silent on success, so provide the answer directly. Failures
			// are Terraria's own localized Main.NewText result and are captured by the
			// shared chat monitor, preserving the native wording.
			AriadneMod.ScreenReader.Output("This housing is suitable.");
		}
	}

	private void Close(bool announce)
	{
		bool wasActive = IsActive;
		IsActive = false;
		_rooms.Clear();
		_focus = 0;
		_previousKeyboard = default;
		if (announce && wasActive)
		{
			AriadneMod.ScreenReader.Output("Housing query mode closed.");
		}
	}

	private bool Pressed(KeyboardState keyboard, Keys key) =>
		keyboard.IsKeyDown(key) && _previousKeyboard.IsKeyUp(key);

	private static string DescribeState(HousingRoom room) => room.State switch
	{
		HousingRoomState.Suitable => "Suitable room",
		HousingRoomState.Occupied => $"Occupied room, {room.Detail}",
		_ => $"Unsuitable room, {room.Detail}",
	};

	private static NavigationCueKind CueKind(HousingRoomState state) => state switch
	{
		HousingRoomState.Suitable => NavigationCueKind.HousingSuitable,
		HousingRoomState.Occupied => NavigationCueKind.HousingOccupied,
		_ => NavigationCueKind.HousingUnsuitable,
	};
}

internal static class HousingRoomDiscovery
{
	private static readonly FieldInfo? RoomChairField = RoomField("roomChair");
	private static readonly FieldInfo? RoomDoorField = RoomField("roomDoor");
	private static readonly FieldInfo? RoomTableField = RoomField("roomTable");
	private static readonly FieldInfo? RoomTorchField = RoomField("roomTorch");
	private static readonly FieldInfo? RoomEvilField = RoomField("roomEvil");

	internal static IReadOnlyList<HousingRoom> CaptureVisibleRooms()
	{
		List<HousingRoom> rooms = [];
		HashSet<long> visited = [];
		HashSet<string> roomKeys = [];
		Vector2 camera = Main.Camera.ScaledPosition;
		Vector2 size = Main.Camera.ScaledSize;
		int firstX = Math.Clamp((int)MathF.Floor(camera.X / 16f), 1, Main.maxTilesX - 2);
		int lastX = Math.Clamp((int)MathF.Ceiling((camera.X + size.X) / 16f), 1, Main.maxTilesX - 2);
		int firstY = Math.Clamp((int)MathF.Floor(camera.Y / 16f), 1, Main.maxTilesY - 2);
		int lastY = Math.Clamp((int)MathF.Ceiling((camera.Y + size.Y) / 16f), 1, Main.maxTilesY - 2);

		for (int y = firstY; y <= lastY; y++)
		{
			for (int x = firstX; x <= lastX; x++)
			{
				long packed = Pack(x, y);
				if (visited.Contains(packed) || WorldGen.SolidTile(x, y)) continue;
				bool enclosed = WorldGen.StartRoomCheck(x, y);
				visited.Add(packed);
				for (int tileIndex = 0; tileIndex < WorldGen.numRoomTiles; tileIndex++)
				{
					visited.Add(Pack(WorldGen.roomX[tileIndex], WorldGen.roomY[tileIndex]));
				}

				TownNPCRoomCheckFailureReason failure = WorldGen.roomCheckFailureReason;
				if ((!enclosed && failure is TownNPCRoomCheckFailureReason.HoleInWallIsTooBig or TownNPCRoomCheckFailureReason.RoomCheckStartedInASolidTile) ||
					WorldGen.numRoomTiles <= 0)
				{
					continue;
				}

				Rectangle bounds = new(
					WorldGen.roomX1,
					WorldGen.roomY1,
					Math.Max(1, WorldGen.roomX2 - WorldGen.roomX1 + 1),
					Math.Max(1, WorldGen.roomY2 - WorldGen.roomY1 + 1));
				if (!bounds.Intersects(new Rectangle(firstX, firstY, lastX - firstX + 1, lastY - firstY + 1))) continue;
				string key = $"{bounds.X}:{bounds.Y}:{bounds.Width}:{bounds.Height}";
				if (!roomKeys.Add(key)) continue;

				string? occupant = FindOccupant();
				bool suitable = enclosed && failure == TownNPCRoomCheckFailureReason.None &&
					RoomFlag(RoomChairField) && RoomFlag(RoomDoorField) && RoomFlag(RoomTableField) &&
					RoomFlag(RoomTorchField) && !RoomFlag(RoomEvilField);
				HousingRoomState state = occupant is not null
					? HousingRoomState.Occupied
					: suitable ? HousingRoomState.Suitable : HousingRoomState.Unsuitable;
				string detail = occupant ?? DescribeCurrentFailure();
				rooms.Add(new(new(x, y), bounds, state, detail));
			}
		}
		return rooms;
	}

	internal static string DescribeCurrentFailure()
	{
		List<string> failures = [];
		if (WorldGen.roomCheckFailureReason != TownNPCRoomCheckFailureReason.None)
		{
			failures.Add(Humanize(WorldGen.roomCheckFailureReason.ToString()));
		}
		if (RoomFlag(RoomEvilField)) failures.Add("the area is corrupted");
		if (!RoomFlag(RoomDoorField)) failures.Add("missing an entrance");
		if (!RoomFlag(RoomChairField)) failures.Add("missing a comfort item");
		if (!RoomFlag(RoomTableField)) failures.Add("missing a flat-surface item");
		if (!RoomFlag(RoomTorchField)) failures.Add("missing a light source");
		return failures.Count == 0 ? "This housing is not suitable." : string.Join(", ", failures) + ".";
	}

	private static string? FindOccupant()
	{
		foreach (NPC npc in Main.npc)
		{
			if (!npc.active || !npc.townNPC || npc.homeless) continue;
			for (int index = 0; index < WorldGen.numRoomTiles; index++)
			{
				if (WorldGen.roomX[index] == npc.homeTileX &&
					(WorldGen.roomY[index] == npc.homeTileY || WorldGen.roomY[index] == npc.homeTileY - 1))
				{
					return npc.GivenOrTypeName;
				}
			}
		}
		return null;
	}

	private static string Humanize(string text)
	{
		System.Text.StringBuilder builder = new();
		for (int index = 0; index < text.Length; index++)
		{
			if (index > 0 && char.IsUpper(text[index])) builder.Append(' ');
			builder.Append(char.ToLowerInvariant(text[index]));
		}
		return builder.ToString();
	}

	private static long Pack(int x, int y) => ((long)x << 32) | (uint)y;

	private static FieldInfo? RoomField(string name) => typeof(WorldGen).GetField(
		name,
		BindingFlags.NonPublic | BindingFlags.Static);

	private static bool RoomFlag(FieldInfo? field) => field?.GetValue(null) is true;
}
