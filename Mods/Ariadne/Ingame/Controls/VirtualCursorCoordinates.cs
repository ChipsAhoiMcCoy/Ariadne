#nullable enable

using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.GameInput;

namespace Ariadne.Ingame.Controls;

internal sealed class VirtualCursorCoordinates
{
	private Point _lastRawPosition;
	private bool _hasLastRawPosition;
	private bool _ownsCoordinates;

	internal void ApplyWorldPosition(Vector2 worldPosition, Player player)
	{
		float zoom = MathF.Max(0.01f, Main.GameViewMatrix.Zoom.X);
		Vector2 screenCenter = new(Main.screenWidth * 0.5f, Main.screenHeight * 0.5f);
		Vector2 transformedScreen = worldPosition - Main.screenPosition;
		if (player.gravDir < 0f)
		{
			transformedScreen.Y = Main.screenHeight - transformedScreen.Y;
		}

		Vector2 rawScreen = screenCenter + (transformedScreen - screenCenter) * zoom;
		Point rawPosition = new(
			(int)MathF.Round(rawScreen.X),
			(int)MathF.Round(rawScreen.Y));
		SetCoordinates(rawPosition, preserveLastPosition: true);
		_ownsCoordinates = true;
	}

	internal void RestorePhysicalPointer()
	{
		MouseState mouse = Mouse.GetState();
		Point physicalPosition = new(
			(int)MathF.Round(mouse.X * PlayerInput.RawMouseScale.X),
			(int)MathF.Round(mouse.Y * PlayerInput.RawMouseScale.Y));
		SetCoordinates(physicalPosition, preserveLastPosition: false);
		_ownsCoordinates = false;
	}

	internal void RestorePhysicalPointerIfOwned()
	{
		if (_ownsCoordinates)
		{
			RestorePhysicalPointer();
		}
	}

	internal void Reset()
	{
		_hasLastRawPosition = false;
		_ownsCoordinates = false;
		_lastRawPosition = Point.Zero;
	}

	private void SetCoordinates(Point position, bool preserveLastPosition)
	{
		Point previous = preserveLastPosition && _hasLastRawPosition
			? _lastRawPosition
			: position;
		Main.lastMouseX = previous.X;
		Main.lastMouseY = previous.Y;
		Main.mouseX = PlayerInput.MouseX = position.X;
		Main.mouseY = PlayerInput.MouseY = position.Y;

		// UpdateInput cached the physical position immediately before PostUpdateInput.
		// Refresh the public cache so the following unscaled and world-zoom passes
		// transform the virtual position instead of restoring the physical one.
		PlayerInput.CacheZoomableValues();
		_lastRawPosition = position;
		_hasLastRawPosition = true;
	}
}
