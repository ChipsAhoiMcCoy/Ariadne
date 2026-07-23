#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.GameContent.UI;
using Terraria.GameInput;
using Terraria.Localization;
using Terraria.ModLoader;
using Terrarium.Audio;
using Terrarium.Configs;

namespace Terrarium.Ingame.Controls;

[Autoload(Side = ModSide.Client)]
internal sealed class WorldCursorSystem : ModSystem
{
	private readonly WorldCursorState _cursorState = new();
	private readonly VirtualCursorCoordinates _coordinates = new();
	private readonly PrecisionCursorRepeater _precisionRepeater = new();
	private readonly CombatTargetTracker _combatTargets = new();

	private CombatTargetCueSound? _targetCue;
	private KeyboardState _previousKeyboard;
	private bool _gameplayOwnedThisUpdate;
	private bool _smartModeInitialized;
	private bool _wasSmartEnabled;
	private Point _lastNativeSmartTarget;
	private bool _hasLastNativeSmartTarget;
	private string _lastSmartSemanticKey = string.Empty;
	private bool _targetLeftWasHeld;
	private bool _targetRightWasHeld;
	private bool _targetDownWasHeld;
	private TriggerSnapshot _nativePrimaryTrigger;
	private TriggerSnapshot _nativeSecondaryTrigger;

	public override void Load()
	{
		_targetCue = CombatTargetCueSound.Create(Mod);
	}

	public override void PostUpdateInput()
	{
		_targetCue?.Update();
		KeyboardState keyboard = Keyboard.GetState();
		_gameplayOwnedThisUpdate = false;
		if (!WorldInputContext.CanOwnWorldCursor())
		{
			_precisionRepeater.Reset();
			ResetTargetChordLatches();
			_coordinates.RestorePhysicalPointerIfOwned();
			_previousKeyboard = keyboard;
			return;
		}

		Player player = Main.LocalPlayer;
		// A virtual cursor can overlap the hotbar or minimap even though no UI has
		// focus. Discard hover state produced by that virtual position so it cannot
		// block the next player-control copy.
		player.mouseInterface = false;
		player.lastMouseInterface = false;
		Main.HoveringOverAnNPC = false;
		if (!_cursorState.IsInitialized)
		{
			_cursorState.Initialize(player);
		}

		bool smartEnabled = PredictSmartCursorState();
		bool priorSmartEnabled = _smartModeInitialized ? _wasSmartEnabled : smartEnabled;
		Vector2 priorAimPoint = _combatTargets.HasTarget
			? _combatTargets.AimPoint
			: priorSmartEnabled
				? ProjectToViewportEdge(player.Center, _cursorState.AimDirection)
				: _cursorState.PrecisionWorld;
		bool cursorModeChanged = HandleSmartModeTransition(smartEnabled, player, priorAimPoint);

		Point manualDirection = GetManualAimDirection(current: true);
		bool manualAimActive = manualDirection != Point.Zero;
		bool manualJustPressed = GetManualAimDirection(current: false) != Point.Zero;
		if (manualAimActive)
		{
			_combatTargets.CancelForManualAim();
		}

		Vector2 movementDirection = PlayerInput.Triggers.Current.DirectionsRaw;
		bool targetModifierHeld = TerrariumMod.CombatTargetModifierKeybind?.Current == true;
		bool targetLeftHeld = targetModifierHeld && PlayerInput.Triggers.Current.Left;
		bool targetRightHeld = targetModifierHeld && PlayerInput.Triggers.Current.Right;
		bool targetDownHeld = targetModifierHeld && PlayerInput.Triggers.Current.Down;
		bool targetCommandHandled = !manualAimActive && HandleCombatTargetCommands(
			player,
			priorAimPoint,
			leftPressed: targetLeftHeld && !_targetLeftWasHeld,
			rightPressed: targetRightHeld && !_targetRightWasHeld,
			downPressed: targetDownHeld && !_targetDownWasHeld);
		bool targetMovementChordHeld = !manualAimActive &&
			(targetLeftHeld || targetRightHeld || targetDownHeld);
		_targetLeftWasHeld = targetLeftHeld;
		_targetRightWasHeld = targetRightHeld;
		_targetDownWasHeld = targetDownHeld;
		if (targetCommandHandled || targetMovementChordHeld)
		{
			ConsumeTargetMovementChord();
		}
		if (!targetCommandHandled)
		{
			_combatTargets.Update(player);
		}
		if (_combatTargets.TryTakeSelectionCue(out Vector2 targetCuePosition))
		{
			TerrariumClientConfig config = ModContent.GetInstance<TerrariumClientConfig>();
			_targetCue?.Play(targetCuePosition, config);
		}

		if (smartEnabled)
		{
			_precisionRepeater.Reset();
			if (manualAimActive)
			{
				_cursorState.SetAimDirection(manualDirection.ToVector2());
			}
			else if (!_combatTargets.HasTarget &&
				TerrariumMod.CombatTargetModifierKeybind?.Current != true &&
				movementDirection.LengthSquared() > 0f)
			{
				_cursorState.SetAimDirection(movementDirection);
			}
		}
		else if (_combatTargets.HasTarget)
		{
			_precisionRepeater.Reset();
		}
		else
		{
			_precisionRepeater.Update(
				_cursorState,
				player,
				manualDirection,
				manualJustPressed,
				interruptInitialAnnouncement: !cursorModeChanged);
			_cursorState.RecoverIntoViewport();
		}

		Vector2 aimPoint = _combatTargets.HasTarget
			? _combatTargets.AimPoint
			: smartEnabled
				? ProjectToViewportEdge(player.Center, _cursorState.AimDirection)
				: _cursorState.PrecisionWorld;
		_coordinates.ApplyWorldPosition(ClampWorldPosition(aimPoint), player);
		MirrorActionKeybinds();
		DisableNativeGamepadLockOn();

		if (Pressed(keyboard, Keys.F1))
		{
			AnnounceGameplayHelp();
		}

		_gameplayOwnedThisUpdate = true;
		_wasSmartEnabled = smartEnabled;
		_smartModeInitialized = true;
		_previousKeyboard = keyboard;
	}

	public override void PreUpdatePlayers()
	{
		if (!_gameplayOwnedThisUpdate || WorldInputContext.CanOwnWorldCursor())
		{
			return;
		}

		// Another PostUpdateInput system may have opened a scanner or dialog after
		// this system ran. Yield before the player-control copy in that same tick.
		RestoreNativeActionTriggers();
		_coordinates.RestorePhysicalPointerIfOwned();
		_precisionRepeater.Reset();
		_gameplayOwnedThisUpdate = false;
	}

	public override void PostUpdatePlayers()
	{
		if (!_gameplayOwnedThisUpdate)
		{
			return;
		}

		// Player.Update has already copied and processed the native trigger state.
		// Mask the buttons for later interface drawing so a virtual cursor over the
		// hotbar cannot turn an intended world action into a UI click.
		Main.mouseLeft = false;
		if (!WiresUI.Settings.DrawToolModeUI)
		{
			Main.mouseRight = false;
		}

		if (!_wasSmartEnabled)
		{
			return;
		}

		if (!Main.SmartCursorShowing)
		{
			return;
		}

		Point target = new(Main.SmartCursorX, Main.SmartCursorY);
		if (!WorldGen.InWorld(target.X, target.Y, 1))
		{
			return;
		}

		_lastNativeSmartTarget = target;
		_hasLastNativeSmartTarget = true;
		WorldTargetDescription description = WorldTargetDescriber.Describe(target, Main.LocalPlayer);
		if (description.SemanticKey.Equals(_lastSmartSemanticKey, StringComparison.Ordinal))
		{
			return;
		}

		_lastSmartSemanticKey = description.SemanticKey;
		if (description.IsEmptySpace)
		{
			return;
		}
		TerrariumMod.ScreenReader.Output(description.TargetText, interrupt: false);
	}

	public override void OnWorldLoad() => ResetState();

	public override void OnWorldUnload() => ResetState();

	public override void Unload()
	{
		ResetState();
		_targetCue?.Dispose();
		_targetCue = null;
	}

	private bool HandleSmartModeTransition(bool smartEnabled, Player player, Vector2 currentAimPoint)
	{
		if (!_smartModeInitialized)
		{
			if (smartEnabled)
			{
				_cursorState.UpdateDirectionFromPrecision(player);
				_hasLastNativeSmartTarget = false;
			}
			_wasSmartEnabled = smartEnabled;
			_smartModeInitialized = true;
			return false;
		}

		if (smartEnabled == _wasSmartEnabled)
		{
			return false;
		}

		_precisionRepeater.Reset();
		_lastSmartSemanticKey = string.Empty;
		string announcementKey = smartEnabled
			? "Mods.Terrarium.Announcements.SmartCursor"
			: "Mods.Terrarium.Announcements.UnlockedCursor";
		TerrariumMod.ScreenReader.Output(Language.GetTextValue(announcementKey));
		if (smartEnabled)
		{
			_cursorState.UpdateDirectionFromPrecision(player);
			_hasLastNativeSmartTarget = false;
			return true;
		}

		if (_hasLastNativeSmartTarget)
		{
			_cursorState.SetPrecision(_lastNativeSmartTarget, player);
		}
		else
		{
			_cursorState.SetPrecision(currentAimPoint, player);
		}
		_cursorState.RecoverIntoViewport();
		_hasLastNativeSmartTarget = false;
		return true;
	}

	private bool HandleCombatTargetCommands(
		Player player,
		Vector2 currentAimPoint,
		bool leftPressed,
		bool rightPressed,
		bool downPressed)
	{
		if (downPressed)
		{
			_combatTargets.Clear();
			_cursorState.Recenter(player);
			WorldTargetDescription description = WorldTargetDescriber.Describe(_cursorState.PrecisionTile, player);
			TerrariumMod.ScreenReader.Output($"Combat target cleared. Cursor recentered. {description.DetailedText}");
			return true;
		}
		if (leftPressed && !rightPressed)
		{
			_combatTargets.SelectPrevious(player, currentAimPoint);
			return true;
		}
		if (rightPressed && !leftPressed)
		{
			_combatTargets.SelectNext(player, currentAimPoint);
			return true;
		}
		return false;
	}

	private static void ConsumeTargetMovementChord()
	{
		if (PlayerInput.Triggers.Current.Left)
		{
			PlayerInput.Triggers.Current.Left = false;
			PlayerInput.Triggers.JustPressed.Left = false;
			PlayerInput.Triggers.JustReleased.Left = false;
		}
		if (PlayerInput.Triggers.Current.Right)
		{
			PlayerInput.Triggers.Current.Right = false;
			PlayerInput.Triggers.JustPressed.Right = false;
			PlayerInput.Triggers.JustReleased.Right = false;
		}
		if (PlayerInput.Triggers.Current.Down)
		{
			PlayerInput.Triggers.Current.Down = false;
			PlayerInput.Triggers.JustPressed.Down = false;
			PlayerInput.Triggers.JustReleased.Down = false;
		}
	}

	private static Point GetManualAimDirection(bool current)
	{
		int horizontal = Read(TerrariumMod.AimRightKeybind, current) - Read(TerrariumMod.AimLeftKeybind, current);
		int vertical = Read(TerrariumMod.AimDownKeybind, current) - Read(TerrariumMod.AimUpKeybind, current);
		return new Point(horizontal, vertical);
	}

	private static int Read(ModKeybind? keybind, bool current)
	{
		if (keybind is null)
		{
			return 0;
		}
		return (current ? keybind.Current : keybind.JustPressed) ? 1 : 0;
	}

	private static bool PredictSmartCursorState()
	{
		bool current = Main.SmartCursorIsUsed;
		if (Main.cSmartCursorModeIsToggleAndNotHold)
		{
			return PlayerInput.Triggers.JustPressed.SmartCursor ? !current : current;
		}

		if (Player.SmartCursorSettings.SmartCursorHoldCanReleaseMidUse)
		{
			return PlayerInput.Triggers.Current.SmartCursor;
		}

		if (!current)
		{
			return PlayerInput.Triggers.Current.SmartCursor;
		}

		bool primaryUse = PlayerInput.Triggers.Current.MouseLeft ||
			TerrariumMod.UseHeldItemKeybind?.Current == true;
		return PlayerInput.Triggers.Current.SmartCursor || primaryUse;
	}

	private static Vector2 ProjectToViewportEdge(Vector2 origin, Vector2 direction)
	{
		if (direction.LengthSquared() < 0.001f)
		{
			direction = Vector2.UnitX;
		}
		else
		{
			direction.Normalize();
		}

		Vector2 viewportPosition = Main.Camera.ScaledPosition + new Vector2(8f);
		Vector2 viewportEnd = Main.Camera.ScaledPosition + Main.Camera.ScaledSize - new Vector2(8f);
		float scale = float.PositiveInfinity;
		if (direction.X > 0f)
		{
			scale = MathF.Min(scale, (viewportEnd.X - origin.X) / direction.X);
		}
		else if (direction.X < 0f)
		{
			scale = MathF.Min(scale, (viewportPosition.X - origin.X) / direction.X);
		}
		if (direction.Y > 0f)
		{
			scale = MathF.Min(scale, (viewportEnd.Y - origin.Y) / direction.Y);
		}
		else if (direction.Y < 0f)
		{
			scale = MathF.Min(scale, (viewportPosition.Y - origin.Y) / direction.Y);
		}

		if (!float.IsFinite(scale) || scale < 16f)
		{
			scale = 16f;
		}
		return origin + direction * scale;
	}

	private static Vector2 ClampWorldPosition(Vector2 worldPosition)
	{
		return new Vector2(
			MathHelper.Clamp(worldPosition.X, 16f, Math.Max(16f, Main.maxTilesX * 16f - 17f)),
			MathHelper.Clamp(worldPosition.Y, 16f, Math.Max(16f, Main.maxTilesY * 16f - 17f)));
	}

	private void MirrorActionKeybinds()
	{
		_nativePrimaryTrigger = CaptureTrigger(primary: true);
		_nativeSecondaryTrigger = CaptureTrigger(primary: false);
		MergeAction(TerrariumMod.UseHeldItemKeybind, primary: true);
		MergeAction(TerrariumMod.SecondaryUseKeybind, primary: false);
		Main.mouseLeft = PlayerInput.Triggers.Current.MouseLeft;
		Main.mouseRight = PlayerInput.Triggers.Current.MouseRight;
	}

	private void RestoreNativeActionTriggers()
	{
		RestoreTrigger(_nativePrimaryTrigger, primary: true);
		RestoreTrigger(_nativeSecondaryTrigger, primary: false);
		Main.mouseLeft = PlayerInput.Triggers.Current.MouseLeft;
		Main.mouseRight = PlayerInput.Triggers.Current.MouseRight;
	}

	private static TriggerSnapshot CaptureTrigger(bool primary)
	{
		return primary
			? new(
				PlayerInput.Triggers.Current.MouseLeft,
				PlayerInput.Triggers.JustPressed.MouseLeft,
				PlayerInput.Triggers.JustReleased.MouseLeft)
			: new(
				PlayerInput.Triggers.Current.MouseRight,
				PlayerInput.Triggers.JustPressed.MouseRight,
				PlayerInput.Triggers.JustReleased.MouseRight);
	}

	private static void RestoreTrigger(TriggerSnapshot snapshot, bool primary)
	{
		if (primary)
		{
			PlayerInput.Triggers.Current.MouseLeft = snapshot.Current;
			PlayerInput.Triggers.JustPressed.MouseLeft = snapshot.JustPressed;
			PlayerInput.Triggers.JustReleased.MouseLeft = snapshot.JustReleased;
		}
		else
		{
			PlayerInput.Triggers.Current.MouseRight = snapshot.Current;
			PlayerInput.Triggers.JustPressed.MouseRight = snapshot.JustPressed;
			PlayerInput.Triggers.JustReleased.MouseRight = snapshot.JustReleased;
		}
	}

	private static void MergeAction(ModKeybind? keybind, bool primary)
	{
		if (keybind is null)
		{
			return;
		}

		if (primary)
		{
			bool combined = PlayerInput.Triggers.Current.MouseLeft || keybind.Current;
			bool oldCombined = PlayerInput.Triggers.Old.MouseLeft;
			PlayerInput.Triggers.Current.MouseLeft = combined;
			PlayerInput.Triggers.JustPressed.MouseLeft = combined && !oldCombined;
			PlayerInput.Triggers.JustReleased.MouseLeft = !combined && oldCombined;
		}
		else
		{
			bool combined = PlayerInput.Triggers.Current.MouseRight || keybind.Current;
			bool oldCombined = PlayerInput.Triggers.Old.MouseRight;
			PlayerInput.Triggers.Current.MouseRight = combined;
			PlayerInput.Triggers.JustPressed.MouseRight = combined && !oldCombined;
			PlayerInput.Triggers.JustReleased.MouseRight = !combined && oldCombined;
		}
	}

	private static void DisableNativeGamepadLockOn()
	{
		PlayerInput.Triggers.Current.LockOn = false;
		PlayerInput.Triggers.JustPressed.LockOn = false;
		if (LockOnHelper.Enabled)
		{
			LockOnHelper.Toggle(forceOff: true);
		}
	}

	private void AnnounceGameplayHelp()
	{
		string movement = DescribeNativeBindings("Up", "Left", "Down", "Right");
		string aiming = DescribeModBindings(
			TerrariumMod.AimUpKeybind,
			TerrariumMod.AimLeftKeybind,
			TerrariumMod.AimDownKeybind,
			TerrariumMod.AimRightKeybind);
		string use = DescribeModBinding(TerrariumMod.UseHeldItemKeybind);
		string secondary = DescribeModBinding(TerrariumMod.SecondaryUseKeybind);
		string targetModifier = DescribeModBinding(TerrariumMod.CombatTargetModifierKeybind);
		TerrariumMod.ScreenReader.Output(
			$"Gameplay controls. Move with {movement}. Aim with {aiming}. " +
			$"Use the held item with {use}, and secondary use or interact with {secondary}. " +
			$"Terraria's Smart Cursor binding keeps its configured toggle or hold behavior. " +
			$"Hold {targetModifier} with move left or move right to cycle combat targets; " +
			$"hold {targetModifier} with move down to clear the target and recenter. " +
			"Manual aim always cancels combat lock. Press F1 to repeat this help.");
	}

	private static string DescribeNativeBindings(params string[] triggers)
	{
		if (!PlayerInput.CurrentProfile.InputModes.TryGetValue(InputMode.Keyboard, out KeyConfiguration? configuration))
		{
			return string.Join(", ", triggers);
		}

		return string.Join(", ", triggers.Select(trigger =>
			configuration.KeyStatus.TryGetValue(trigger, out List<string>? bindings) && bindings.Count > 0
				? string.Join(" or ", bindings)
				: $"{trigger} unbound"));
	}

	private static string DescribeModBindings(params ModKeybind?[] keybinds)
	{
		return string.Join(", ", keybinds.Select(DescribeModBinding));
	}

	private static string DescribeModBinding(ModKeybind? keybind)
	{
		if (keybind is null)
		{
			return "unbound";
		}
		List<string> bindings = keybind.GetAssignedKeys(InputMode.Keyboard);
		return bindings.Count == 0 ? "unbound" : string.Join(" or ", bindings);
	}

	private bool Pressed(KeyboardState keyboard, Keys key)
	{
		return keyboard.IsKeyDown(key) && _previousKeyboard.IsKeyUp(key);
	}

	private void ResetState()
	{
		_coordinates.RestorePhysicalPointerIfOwned();
		_cursorState.Reset();
		_coordinates.Reset();
		_precisionRepeater.Reset();
		_combatTargets.Reset();
		_targetCue?.StopAndReset();
		_previousKeyboard = default;
		_gameplayOwnedThisUpdate = false;
		_smartModeInitialized = false;
		_wasSmartEnabled = false;
		_lastNativeSmartTarget = Point.Zero;
		_hasLastNativeSmartTarget = false;
		_lastSmartSemanticKey = string.Empty;
		ResetTargetChordLatches();
		_nativePrimaryTrigger = default;
		_nativeSecondaryTrigger = default;
	}

	private void ResetTargetChordLatches()
	{
		_targetLeftWasHeld = false;
		_targetRightWasHeld = false;
		_targetDownWasHeld = false;
	}

	private readonly record struct TriggerSnapshot(bool Current, bool JustPressed, bool JustReleased);
}
