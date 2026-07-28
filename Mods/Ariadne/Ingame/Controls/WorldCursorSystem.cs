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
using Ariadne.Accessibility;
using Ariadne.Audio;
using Ariadne.Configs;
using Ariadne.Ingame.Freecam;

namespace Ariadne.Ingame.Controls;

/// <summary>
/// Which combat-targeting command a modifier chord issued this tick. Naming the
/// command lets the handler swallow only the direction it consumed, so the other
/// walk directions survive the chord.
/// </summary>
internal enum CombatTargetCommand
{
	None,
	Clear,
	SelectPrevious,
	SelectNext,
}

[Autoload(Side = ModSide.Client)]
internal sealed class WorldCursorSystem : ModSystem
{
	private readonly WorldCursorState _cursorState = new();
	private readonly VirtualCursorCoordinates _coordinates = new();
	private readonly PrecisionCursorRepeater _precisionRepeater = new();
	private readonly CombatTargetTracker _combatTargets = new();

	private CombatTargetCueSound? _targetCue;
	private CursorEarconSound? _cursorEarcon;
	private KeyboardState _previousKeyboard;
	private bool _gameplayOwnedThisUpdate;
	private bool _smartModeInitialized;
	private bool _wasSmartEnabled;
	private Point _lastSmartTarget;
	private bool _hasLastSmartTarget;
	private string _lastSmartSemanticKey = string.Empty;
	private string _spokenSmartSemanticKey = string.Empty;
	private bool _targetLeftWasHeld;
	private bool _targetRightWasHeld;
	private bool _targetDownWasHeld;
	private TriggerSnapshot _nativePrimaryTrigger;
	private TriggerSnapshot _nativeSecondaryTrigger;

	public override void Load()
	{
		_targetCue = CombatTargetCueSound.Create(Mod);
		_cursorEarcon = CursorEarconSound.Create(Mod);
	}

	public override void PostUpdateInput()
	{
		AriadneClientConfig config = ModContent.GetInstance<AriadneClientConfig>();
		_targetCue?.Update(config);
		_cursorEarcon?.Update(config);
		KeyboardState keyboard = Keyboard.GetState();
		_gameplayOwnedThisUpdate = false;
		if (!WorldInputContext.CanOwnWorldCursor())
		{
			if (FreecamSystem.IsActive)
			{
				_combatTargets.Clear();
			}
			_precisionRepeater.Reset();
			ClearSmartFeedbackTarget();
			_cursorEarcon?.StopAndReset();
			_targetCue?.StopAndReset();
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
		bool cursorModeChanged = HandleSmartModeTransition(smartEnabled, player);

		Point manualDirection = GetManualAimDirection(current: true);
		bool manualAimActive = manualDirection != Point.Zero;
		bool manualJustPressed = GetManualAimDirection(current: false) != Point.Zero;
		if (manualAimActive)
		{
			_combatTargets.CancelForManualAim();
		}

		Vector2 movementDirection = PlayerInput.Triggers.Current.DirectionsRaw;
		bool targetModifierHeld = AriadneMod.CombatTargetModifierKeybind?.Current == true;
		bool targetLeftHeld = targetModifierHeld && PlayerInput.Triggers.Current.Left;
		bool targetRightHeld = targetModifierHeld && PlayerInput.Triggers.Current.Right;
		bool targetDownHeld = targetModifierHeld && PlayerInput.Triggers.Current.Down;
		CombatTargetCommand targetCommand = manualAimActive
			? CombatTargetCommand.None
			: HandleCombatTargetCommands(
				player,
				priorAimPoint,
				leftPressed: targetLeftHeld && !_targetLeftWasHeld,
				rightPressed: targetRightHeld && !_targetRightWasHeld,
				downPressed: targetDownHeld && !_targetDownWasHeld);
		bool targetCommandHandled = targetCommand != CombatTargetCommand.None;
		_targetLeftWasHeld = targetLeftHeld;
		_targetRightWasHeld = targetRightHeld;
		_targetDownWasHeld = targetDownHeld;
		// Only the direction that actually issued a command is swallowed, and only
		// on its press edge. Consuming the whole hold stopped the player dead for as
		// long as the chord was down, which is fatal during a boss fight.
		ConsumeTargetMovementChord(targetCommand);
		if (!targetCommandHandled)
		{
			_combatTargets.Update(player);
		}
		if (_combatTargets.TryTakeSelectionCue(out Vector2 targetCuePosition))
		{
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
				AriadneMod.CombatTargetModifierKeybind?.Current != true &&
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
				manualDirection,
				manualJustPressed,
				interruptInitialAnnouncement: !cursorModeChanged,
				HandlePrecisionStep);
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

		if (ContextHelpChord.Pressed(keyboard, _previousKeyboard))
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
		if (!FreecamSystem.IsActive)
		{
			RestoreNativeActionTriggers();
		}
		_coordinates.RestorePhysicalPointerIfOwned();
		_precisionRepeater.Reset();
		ClearSmartFeedbackTarget();
		_cursorEarcon?.StopAndReset();
		_targetCue?.StopAndReset();
		_gameplayOwnedThisUpdate = false;
	}

	public override void PostUpdatePlayers()
	{
		if (!_gameplayOwnedThisUpdate)
		{
			ClearSmartFeedbackTarget();
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
			ClearSmartFeedbackTarget();
			return;
		}

		if (!Main.SmartCursorShowing)
		{
			// Smart Cursor drops its target for a frame between some blocks. Keep the
			// spoken material so that gap cannot restart the name mid-dig.
			ClearSmartFeedbackTarget(keepSpokenTarget: true);
			_cursorEarcon?.StopAndReset();
			return;
		}

		Point target = new(Main.SmartCursorX, Main.SmartCursorY);
		if (!WorldGen.InWorld(target.X, target.Y, 1))
		{
			ClearSmartFeedbackTarget();
			_cursorEarcon?.StopAndReset();
			return;
		}

		WorldTargetDescription description = WorldTargetDescriber.Describe(target, Main.LocalPlayer);
		if (_hasLastSmartTarget &&
			target == _lastSmartTarget &&
			description.SemanticKey.Equals(_lastSmartSemanticKey, StringComparison.Ordinal))
		{
			return;
		}

		_lastSmartTarget = target;
		_hasLastSmartTarget = true;
		_lastSmartSemanticKey = description.SemanticKey;
		if (description.IsEmptySpace)
		{
			_cursorEarcon?.StopAndReset();
			return;
		}

		AriadneClientConfig config = ModContent.GetInstance<AriadneClientConfig>();
		_cursorEarcon?.Play(
			target,
			config);

		// Smart Cursor walks itself from block to block, so the target coordinate
		// changes on every break while the player is still working one material. The
		// earcon above marks each block; the name is only worth speaking when the
		// material itself changes, and the emptied tiles left behind deliberately do
		// not clear it, so a long dig through one material stays quiet after the first.
		if (description.SemanticKey.Equals(_spokenSmartSemanticKey, StringComparison.Ordinal))
		{
			return;
		}

		_spokenSmartSemanticKey = description.SemanticKey;
		AriadneMod.ScreenReader.Output(description.TargetText, interrupt: false);
	}

	public override void OnWorldLoad()
	{
		ResetState();
		// Smart Cursor is the mode that resolves a target without a physical pointer, so it
		// is the useful starting mode here even though vanilla always begins unlocked. Both
		// device fields are set because Main.SmartCursorWanted picks between them by the
		// cursor mode currently reported for the UI.
		Main.SmartCursorWanted_Mouse = true;
		Main.SmartCursorWanted_GamePad = true;
	}

	public override void OnWorldUnload() => ResetState();

	public override void Unload()
	{
		ResetState();
		_targetCue?.Dispose();
		_targetCue = null;
		_cursorEarcon?.Dispose();
		_cursorEarcon = null;
	}

	private bool HandleSmartModeTransition(bool smartEnabled, Player player)
	{
		if (!_smartModeInitialized)
		{
			_wasSmartEnabled = smartEnabled;
			_smartModeInitialized = true;
			return false;
		}

		if (smartEnabled == _wasSmartEnabled)
		{
			return false;
		}

		_precisionRepeater.Reset();
		ClearSmartFeedbackTarget();
		_cursorEarcon?.StopAndReset();
		string announcementKey = smartEnabled
			? "Mods.Ariadne.Announcements.SmartCursor"
			: "Mods.Ariadne.Announcements.UnlockedCursor";
		AriadneMod.ScreenReader.Output(Language.GetTextValue(announcementKey));
		if (smartEnabled)
		{
			_cursorState.UpdateDirectionFromPrecision(player);
			return true;
		}

		_cursorState.Recenter(player);
		_cursorState.RecoverIntoViewport();
		return true;
	}

	private void HandlePrecisionStep(Point tilePosition, bool interrupt)
	{
		Player player = Main.LocalPlayer;
		WorldTargetDescription description = WorldTargetDescriber.Describe(tilePosition, player);
		AriadneClientConfig config = ModContent.GetInstance<AriadneClientConfig>();
		_cursorEarcon?.Play(
			tilePosition,
			config);
		string announcement = config.CursorCoordinateAnnouncementsEnabled
			? description.CoordinateDetailedText
			: description.DetailedText;
		AriadneMod.ScreenReader.Output(announcement, interrupt);
	}

	private CombatTargetCommand HandleCombatTargetCommands(
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
			AriadneMod.ScreenReader.Output($"Combat target cleared. Cursor recentered. {description.DetailedText}");
			return CombatTargetCommand.Clear;
		}
		if (leftPressed && !rightPressed)
		{
			_combatTargets.SelectPrevious(player, currentAimPoint);
			return CombatTargetCommand.SelectPrevious;
		}
		if (rightPressed && !leftPressed)
		{
			_combatTargets.SelectNext(player, currentAimPoint);
			return CombatTargetCommand.SelectNext;
		}
		return CombatTargetCommand.None;
	}

	private static void ConsumeTargetMovementChord(CombatTargetCommand command)
	{
		switch (command)
		{
			case CombatTargetCommand.SelectPrevious:
				PlayerInput.Triggers.Current.Left = false;
				PlayerInput.Triggers.JustPressed.Left = false;
				PlayerInput.Triggers.JustReleased.Left = false;
				break;
			case CombatTargetCommand.SelectNext:
				PlayerInput.Triggers.Current.Right = false;
				PlayerInput.Triggers.JustPressed.Right = false;
				PlayerInput.Triggers.JustReleased.Right = false;
				break;
			case CombatTargetCommand.Clear:
				PlayerInput.Triggers.Current.Down = false;
				PlayerInput.Triggers.JustPressed.Down = false;
				PlayerInput.Triggers.JustReleased.Down = false;
				break;
		}
	}

	private static Point GetManualAimDirection(bool current)
	{
		int horizontal = Read(AriadneMod.AimRightKeybind, current) - Read(AriadneMod.AimLeftKeybind, current);
		int vertical = Read(AriadneMod.AimDownKeybind, current) - Read(AriadneMod.AimUpKeybind, current);
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
		// Hook dispatch order between systems is not guaranteed, so this cannot rely on the
		// menu system having already cleared the trigger on the frame a menu closes.
		if (AccessibleInputSuppression.IsCursorModeSuppressed)
		{
			return current;
		}

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
			AriadneMod.UseHeldItemKeybind?.Current == true;
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
		MergeAction(AriadneMod.UseHeldItemKeybind, primary: true);
		MergeAction(AriadneMod.SecondaryUseKeybind, primary: false);
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
			AriadneMod.AimUpKeybind,
			AriadneMod.AimLeftKeybind,
			AriadneMod.AimDownKeybind,
			AriadneMod.AimRightKeybind);
		string use = DescribeModBinding(AriadneMod.UseHeldItemKeybind);
		string secondary = DescribeModBinding(AriadneMod.SecondaryUseKeybind);
		string targetModifier = DescribeModBinding(AriadneMod.CombatTargetModifierKeybind);
		string status = DescribeModBinding(AriadneMod.PlayerStatusKeybind);
		string scanner = DescribeModBinding(AriadneMod.OpenScannerKeybind);
		string wallTones = DescribeModBinding(AriadneMod.ToggleWallTonesKeybind);
		string waypoints = DescribeModBinding(AriadneMod.OpenWaypointsKeybind);
		string freecam = DescribeModBinding(AriadneMod.FreecamModifierKeybind);
		AriadneMod.ScreenReader.Output(
			$"Gameplay controls. Move with {movement}. Aim with {aiming}. " +
			$"Use the held item with {use}, and secondary use or interact with {secondary}. " +
			$"Terraria's Smart Cursor binding keeps its configured toggle or hold behavior. " +
			$"Hold {targetModifier} with move left or move right to cycle combat targets; " +
			$"hold {targetModifier} with move down to clear the target and recenter. " +
			$"Manual aim always cancels combat lock. " +
			$"Press {status} for a character status readout, {scanner} to scan the visible surroundings, and {wallTones} to toggle wall tones. " +
			$"Hold {targetModifier} with {waypoints} to open waypoints. Hold {freecam} to move a free camera, and release it to return to your body. " +
			$"Press {ContextHelpChord.Name} to repeat this help.");
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
		_cursorEarcon?.StopAndReset();
		_previousKeyboard = default;
		_gameplayOwnedThisUpdate = false;
		_smartModeInitialized = false;
		_wasSmartEnabled = false;
		ClearSmartFeedbackTarget();
		ResetTargetChordLatches();
		_nativePrimaryTrigger = default;
		_nativeSecondaryTrigger = default;
	}

	/// <param name="keepSpokenTarget">
	/// Retains the last spoken material so a momentary gap in Smart Cursor targeting
	/// does not restart the name of a material the player is still working through.
	/// </param>
	private void ClearSmartFeedbackTarget(bool keepSpokenTarget = false)
	{
		_lastSmartTarget = Point.Zero;
		_hasLastSmartTarget = false;
		_lastSmartSemanticKey = string.Empty;
		if (!keepSpokenTarget)
		{
			_spokenSmartSemanticKey = string.Empty;
		}
	}

	private void ResetTargetChordLatches()
	{
		_targetLeftWasHeld = false;
		_targetRightWasHeld = false;
		_targetDownWasHeld = false;
	}

	private readonly record struct TriggerSnapshot(bool Current, bool JustPressed, bool JustReleased);
}
