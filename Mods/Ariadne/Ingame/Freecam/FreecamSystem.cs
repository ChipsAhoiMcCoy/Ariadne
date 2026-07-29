#nullable enable

using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.GameInput;
using Terraria.Localization;
using Terraria.ModLoader;
using Ariadne.Audio;
using Ariadne.Configs;
using Ariadne.Ingame.Controls;

namespace Ariadne.Ingame.Freecam;

[Autoload(Side = ModSide.Client)]
internal sealed class FreecamSystem : ModSystem
{
	private const float MinimumTeleportThresholdPixels = 96f;

	private static FreecamSystem? _instance;
	private static uint _observerRevision;

	private readonly IFreecamContactFeedback _contactFeedback =
		new AudibleFreecamContactFeedback();
	private FreecamBodyBeaconAudioStream? _beaconAudio;
	private Vector2 _position;
	private Vector2 _previousLivePlayerCenter;
	private int _width;
	private int _height;
	private int _gravityDirection = 1;
	private bool _active;
	private bool _modifierReleaseRequired;
	private bool _forceBodyCameraFrame;
	private bool _hasPreviousLivePlayerCenter;
	private bool _movementUpHeld;
	private bool _movementDownHeld;
	private bool _movementLeftHeld;
	private bool _movementRightHeld;

	internal static bool IsActive => _instance?._active == true;

	internal static uint ObserverRevision => _observerRevision;

	public override void Load()
	{
		_instance = this;
		_beaconAudio = FreecamBodyBeaconAudioStream.TryCreate(Mod);
	}

	public override void OnWorldLoad()
	{
		ResetWithoutAnnouncement();
	}

	public override void OnWorldUnload()
	{
		ResetWithoutAnnouncement();
	}

	public override void PostUpdateInput()
	{
		// Freecam only applies in world, and reading a mod keybind before
		// PlayerInput has taken the mod's triggers into its key status throws, which
		// costs the whole update that this hook runs inside.
		if (Main.gameMenu)
		{
			return;
		}

		bool modifierHeld = AriadneMod.FreecamModifierKeybind?.Current == true;
		if (!_active &&
			_modifierReleaseRequired &&
			!modifierHeld &&
			CanActivate())
		{
			_modifierReleaseRequired = false;
		}

		if (_active)
		{
			if (!CanRemainActive() || HasUiTransitionCommand() || HasMajorLivePlayerDiscontinuity())
			{
				Deactivate(requireModifierRelease: true, announce: true);
				return;
			}

			if (!modifierHeld)
			{
				Deactivate(requireModifierRelease: false, announce: true);
				return;
			}

			UpdateMovement();
			ConsumeOwnedTriggers();
			UpdateBeacon();
			RememberLivePlayerPosition();
			return;
		}

		_beaconAudio?.StopAndReset();
		if (!modifierHeld ||
			_modifierReleaseRequired ||
			!CanActivate() ||
			HasUiTransitionCommand())
		{
			return;
		}

		Activate(Main.LocalPlayer);
		UpdateMovement();
		ConsumeOwnedTriggers();
		UpdateBeacon();
		RememberLivePlayerPosition();
	}

	public override void PreUpdatePlayers()
	{
		if (_active)
		{
			// Reapply at the final supported pre-player boundary in case another
			// input owner restored a native action after PostUpdateInput.
			ConsumeOwnedTriggers();
		}
	}

	public override void PostUpdatePlayers()
	{
		if (!_active)
		{
			return;
		}

		if (!CanRemainActive() || HasMajorLivePlayerDiscontinuity())
		{
			Deactivate(
				requireModifierRelease: AriadneMod.FreecamModifierKeybind?.Current == true,
				announce: true);
			return;
		}

		Player player = Main.LocalPlayer;
		_position = FreecamCollisionMover.ConstrainCenterToRange(
			_position,
			_width,
			_height,
			player.Center);
		RememberLivePlayerPosition();
	}

	public override void ModifyScreenPosition()
	{
		if (_active)
		{
			CenterCameraOn(VirtualCenter);
			return;
		}

		if (_forceBodyCameraFrame &&
			!Main.gameMenu &&
			Main.LocalPlayer is { active: true } player)
		{
			CenterCameraOn(player.Center);
			_forceBodyCameraFrame = false;
		}
	}

	public override void Unload()
	{
		ResetWithoutAnnouncement();
		_beaconAudio?.Dispose();
		_beaconAudio = null;
		if (ReferenceEquals(_instance, this))
		{
			_instance = null;
		}
	}

	internal static bool TryGetVirtualBody(
		out Vector2 center,
		out int width,
		out int height,
		out float gravityDirection)
	{
		if (_instance is { _active: true } system)
		{
			center = system.VirtualCenter;
			width = system._width;
			height = system._height;
			gravityDirection = system._gravityDirection;
			return true;
		}

		center = Vector2.Zero;
		width = 0;
		height = 0;
		gravityDirection = 1f;
		return false;
	}

	private Vector2 VirtualCenter =>
		_position + new Vector2(_width * 0.5f, _height * 0.5f);

	private void Activate(Player player)
	{
		_width = Math.Max(1, player.width);
		_height = Math.Max(1, player.height);
		_gravityDirection = player.gravDir < 0f ? -1 : 1;
		_position = player.Center - new Vector2(_width * 0.5f, _height * 0.5f);
		_previousLivePlayerCenter = player.Center;
		_hasPreviousLivePlayerCenter = true;
		_active = true;
		_forceBodyCameraFrame = false;
		_contactFeedback.Reset();
		IncrementObserverRevision();
		AriadneMod.ScreenReader.Output(
			Language.GetTextValue("Mods.Ariadne.Announcements.FreecamActivated"));
	}

	private void Deactivate(bool requireModifierRelease, bool announce)
	{
		if (!_active)
		{
			return;
		}

		_active = false;
		_modifierReleaseRequired |= requireModifierRelease;
		_forceBodyCameraFrame = true;
		_contactFeedback.Reset();
		_beaconAudio?.StopAndReset();
		_hasPreviousLivePlayerCenter = false;
		ResetMovementInput();
		IncrementObserverRevision();
		if (announce)
		{
			AriadneMod.ScreenReader.Output(
				Language.GetTextValue("Mods.Ariadne.Announcements.FreecamReturned"));
		}
	}

	private void ResetWithoutAnnouncement()
	{
		if (_active)
		{
			_active = false;
			IncrementObserverRevision();
		}
		_position = Vector2.Zero;
		_previousLivePlayerCenter = Vector2.Zero;
		_width = 0;
		_height = 0;
		_gravityDirection = 1;
		_modifierReleaseRequired = false;
		_forceBodyCameraFrame = false;
		_hasPreviousLivePlayerCenter = false;
		ResetMovementInput();
		_contactFeedback.Reset();
		_beaconAudio?.StopAndReset();
	}

	private void UpdateMovement()
	{
		Player player = Main.LocalPlayer;
		Vector2 inputDirection = ReadMovementDirection();
		if (inputDirection.LengthSquared() > 1f)
		{
			inputDirection.Normalize();
		}

		if (inputDirection.LengthSquared() <= 0f)
		{
			_position = FreecamCollisionMover.ConstrainCenterToRange(
				_position,
				_width,
				_height,
				player.Center);
			_contactFeedback.Update(
				Vector2.Zero,
				new(_position, false, false, false, false, false));
			return;
		}

		float speed = MathF.Max(0f, player.maxRunSpeed * 4f);
		Vector2 displacement = inputDirection * speed;
		bool fallThroughPlatforms = inputDirection.Y * _gravityDirection > 0f;
		FreecamMovementResult movement = FreecamCollisionMover.Move(
			_position,
			_width,
			_height,
			_gravityDirection,
			displacement,
			player.Center,
			fallThroughPlatforms);
		_position = movement.Position;
		_contactFeedback.Update(inputDirection, movement);
	}

	private Vector2 ReadMovementDirection()
	{
		TriggersSet current = PlayerInput.Triggers.Current;
		_movementUpHeld = TrackMovementTrigger(
			_movementUpHeld,
			current.Up,
			"Up");
		_movementDownHeld = TrackMovementTrigger(
			_movementDownHeld,
			current.Down,
			"Down");
		_movementLeftHeld = TrackMovementTrigger(
			_movementLeftHeld,
			current.Left,
			"Left");
		_movementRightHeld = TrackMovementTrigger(
			_movementRightHeld,
			current.Right,
			"Right");
		return new Vector2(
			_movementRightHeld.ToInt() - _movementLeftHeld.ToInt(),
			_movementDownHeld.ToInt() - _movementUpHeld.ToInt());
	}

	private static bool TrackMovementTrigger(
		bool wasHeld,
		bool current,
		string triggerName)
	{
		if (current)
		{
			return true;
		}

		// PlayerInput carries a held keyboard binding forward from Triggers.Old.
		// Freecam deliberately clears Triggers.Current so the live player cannot
		// move, which also clears that carried state on the next update. Retain a
		// direction only after Terraria observed it and while its physical binding
		// is still down.
		return wasHeld && IsPhysicalKeyboardBindingHeld(triggerName);
	}

	private static bool IsPhysicalKeyboardBindingHeld(string triggerName)
	{
		if (!PlayerInput.CurrentProfile.InputModes.TryGetValue(
				InputMode.Keyboard,
				out KeyConfiguration? configuration) ||
			!configuration.KeyStatus.TryGetValue(triggerName, out var bindings))
		{
			return false;
		}

		foreach (string binding in bindings)
		{
			if (Enum.TryParse(binding, out Keys key) &&
				Main.keyState.IsKeyDown(key))
			{
				return true;
			}

			if (PlayerInput.MouseKeys.Contains(binding))
			{
				return true;
			}
		}

		return false;
	}

	private void ResetMovementInput()
	{
		_movementUpHeld = false;
		_movementDownHeld = false;
		_movementLeftHeld = false;
		_movementRightHeld = false;
	}

	private void UpdateBeacon()
	{
		AriadneClientConfig config = ModContent.GetInstance<AriadneClientConfig>();
		if (config.FreecamBeaconVolumePercent <= 0 ||
			Main.soundVolume <= 0f ||
			!GameplayAudioGate.CanListen())
		{
			_beaconAudio?.StopAndReset();
			return;
		}

		SpatialObserverSnapshot observer = SpatialObserverContext.Current;
		Vector2 beaconPosition = ProjectToFieldEdge(
			Main.LocalPlayer.Center,
			observer.FieldPosition,
			SpatialObserverSnapshot.FieldSize);
		_beaconAudio?.UpdateTarget(observer.NormalizeToField(beaconPosition), config);
	}

	private static Vector2 ProjectToFieldEdge(
		Vector2 worldPosition,
		Vector2 fieldPosition,
		Vector2 fieldSize)
	{
		float left = fieldPosition.X;
		float top = fieldPosition.Y;
		float right = left + fieldSize.X;
		float bottom = top + fieldSize.Y;
		if (worldPosition.X >= left &&
			worldPosition.X <= right &&
			worldPosition.Y >= top &&
			worldPosition.Y <= bottom)
		{
			return worldPosition;
		}

		Vector2 origin = fieldPosition + fieldSize * 0.5f;
		Vector2 delta = worldPosition - origin;
		if (delta.LengthSquared() <= 0.0001f)
		{
			return origin;
		}

		float scale = float.PositiveInfinity;
		if (delta.X > 0f)
		{
			scale = MathF.Min(scale, (right - origin.X) / delta.X);
		}
		else if (delta.X < 0f)
		{
			scale = MathF.Min(scale, (left - origin.X) / delta.X);
		}
		if (delta.Y > 0f)
		{
			scale = MathF.Min(scale, (bottom - origin.Y) / delta.Y);
		}
		else if (delta.Y < 0f)
		{
			scale = MathF.Min(scale, (top - origin.Y) / delta.Y);
		}

		return float.IsFinite(scale) && scale >= 0f
			? origin + delta * scale
			: origin;
	}

	private static bool CanActivate()
	{
		return WorldInputContext.IsUnobstructedGameplay();
	}

	private static bool CanRemainActive()
	{
		return WorldInputContext.IsUnobstructedGameplay();
	}

	private static bool HasUiTransitionCommand()
	{
		TriggersSet current = PlayerInput.Triggers.Current;
		TriggersSet justPressed = PlayerInput.Triggers.JustPressed;
		return current.Inventory ||
			justPressed.Inventory ||
			current.MapFull ||
			justPressed.MapFull ||
			current.MapStyle ||
			justPressed.MapStyle ||
			current.OpenCreativePowersMenu ||
			justPressed.OpenCreativePowersMenu;
	}

	private bool HasMajorLivePlayerDiscontinuity()
	{
		if (!_hasPreviousLivePlayerCenter)
		{
			return false;
		}

		Player player = Main.LocalPlayer;
		float threshold = MathF.Max(
			MinimumTeleportThresholdPixels,
			MathF.Max(player.width, player.height) * 4f);
		return Vector2.DistanceSquared(player.Center, _previousLivePlayerCenter) >
			threshold * threshold;
	}

	private void RememberLivePlayerPosition()
	{
		_previousLivePlayerCenter = Main.LocalPlayer.Center;
		_hasPreviousLivePlayerCenter = true;
	}

	/// <summary>
	/// Puts the drawn camera on the freecam body. This is the one place that still wants
	/// the real camera rectangle rather than the fixed spatial field: it is positioning
	/// what the screen shows, so it takes its size from the resolution and zoom in play
	/// and clamps to the same world margins Terraria itself clamps to.
	/// </summary>
	private static void CenterCameraOn(Vector2 center)
	{
		Vector2 size = Main.Camera.ScaledSize;
		if (size.X <= 0f || size.Y <= 0f)
		{
			size = new(Math.Max(1, Main.screenWidth), Math.Max(1, Main.screenHeight));
		}

		Vector2 desired = center - size * 0.5f;
		float minimumX = Main.leftWorld + 656f;
		float minimumY = Main.topWorld + 656f;
		float maximumX = Math.Max(Main.rightWorld - size.X - 672f, minimumX);
		float maximumY = Math.Max(Main.bottomWorld - size.Y - 672f, minimumY);
		Vector2 viewportPosition = new(
			MathHelper.Clamp(desired.X, minimumX, maximumX),
			MathHelper.Clamp(desired.Y, minimumY, maximumY));
		Main.screenPosition = viewportPosition - Main.GameViewMatrix.Translation;
	}

	private static void ConsumeOwnedTriggers()
	{
		ReadOnlySpan<string> consumedTriggers =
		[
			"Up",
			"Down",
			"Left",
			"Right",
			"Jump",
			"MouseLeft",
			"MouseRight",
			"Throw",
			"Grapple",
			"SmartSelect",
			"SmartCursor",
			"QuickMount",
			"QuickHeal",
			"QuickMana",
			"QuickBuff",
			"LockOn",
		];
		foreach (string triggerName in consumedTriggers)
		{
			ClearTrigger(PlayerInput.Triggers.Current, triggerName);
			ClearTrigger(PlayerInput.Triggers.JustPressed, triggerName);
			ClearTrigger(PlayerInput.Triggers.JustReleased, triggerName);
		}

		Main.mouseLeft = false;
		Main.mouseRight = false;
		if (LockOnHelper.Enabled)
		{
			LockOnHelper.Toggle(forceOff: true);
		}
	}

	private static void ClearTrigger(TriggersSet triggers, string triggerName)
	{
		if (triggers.KeyStatus.ContainsKey(triggerName))
		{
			triggers.KeyStatus[triggerName] = false;
		}
	}

	private static void IncrementObserverRevision()
	{
		unchecked
		{
			_observerRevision++;
			if (_observerRevision == 0)
			{
				_observerRevision = 1;
			}
		}
	}
}
