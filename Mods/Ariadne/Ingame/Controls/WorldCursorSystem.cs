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
		bool cyclePressed = !manualAimActive &&
			AriadneMod.CombatTargetCycleKeybind?.JustPressed == true;
		if (cyclePressed)
		{
			HandleCombatTargetCycle(player, priorAimPoint);
		}
		else
		{
			_combatTargets.Update(player);
		}
		if (_combatTargets.TryTakeSelectionCue(out Vector2 targetCuePosition))
		{
			_targetCue?.Play(targetCuePosition, config);
		}
		else if (_combatTargets.TryTakeLossCue(out Vector2 targetLossPosition))
		{
			_targetCue?.PlayLoss(targetLossPosition, config);
		}
		else if (_combatTargets.TryTakeVulnerabilityCue(out CombatTargetVulnerabilityCue vulnerability))
		{
			_targetCue?.PlayVulnerability(vulnerability.Position, vulnerability.CanBeHit, config);
		}

		if (smartEnabled)
		{
			_precisionRepeater.Reset();
			if (manualAimActive)
			{
				_cursorState.SetAimDirection(manualDirection.ToVector2());
			}
			else if (!_combatTargets.HasTarget &&
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
		bool secondaryInteractionHandled = HandleSecondaryInteraction(
			smartEnabled,
			player,
			_cursorState.PrecisionTile);
		MirrorActionKeybinds(secondaryInteractionHandled);
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
		WorldTargetDescription description = WorldTargetDescriber.Describe(
			tilePosition,
			player,
			includeInteractableEntities: true);
		AriadneClientConfig config = ModContent.GetInstance<AriadneClientConfig>();
		_cursorEarcon?.Play(
			tilePosition,
			config);
		string announcement = config.CursorCoordinateAnnouncementsEnabled
			? description.CoordinateDetailedText
			: description.DetailedText;
		AriadneMod.ScreenReader.Output(announcement, interrupt);
	}

	private void HandleCombatTargetCycle(Player player, Vector2 currentAimPoint)
	{
		if (_combatTargets.CycleNext(player, currentAimPoint) != CombatTargetCycleResult.Released)
		{
			return;
		}

		// Stepping past the farthest target ends the lock, so the precision cursor
		// comes back under the player rather than being left at the dead target.
		_cursorState.Recenter(player);
		WorldTargetDescription description = WorldTargetDescriber.Describe(_cursorState.PrecisionTile, player);
		AriadneMod.ScreenReader.Output(
			Language.GetTextValue("Mods.Ariadne.CombatTarget.Released", description.DetailedText));
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

	private static bool HandleSecondaryInteraction(
		bool smartEnabled,
		Player player,
		Point precisionTile)
	{
		bool held = PlayerInput.Triggers.Current.MouseRight ||
			AriadneMod.SecondaryUseKeybind?.Current == true;
		bool pressed = PlayerInput.Triggers.JustPressed.MouseRight ||
			AriadneMod.SecondaryUseKeybind?.JustPressed == true;
		if (!held || AriadneMod.CombatTargetModifierKeybind?.Current == true)
		{
			return false;
		}

		WorldInteractionTarget target = smartEnabled
			? WorldInteractionResolver.ResolveSmart(player)
			: WorldInteractionResolver.ResolvePrecision(precisionTile, player);
		if (!target.IsEntity)
		{
			return false;
		}

		// Keep the underlying tile suppressed for the entire hold, not just its first
		// frame. Otherwise an Old Shaking Chest without a key would announce the
		// requirement on press and let an overlapping track receive the held button on
		// the following update.
		if (!pressed)
		{
			return true;
		}

		bool handled = WorldInteractionResolver.TryActivate(
			target,
			player,
			out string? failureAnnouncement);
		if (!string.IsNullOrWhiteSpace(failureAnnouncement))
		{
			AriadneMod.ScreenReader.Output(failureAnnouncement);
		}
		return handled;
	}

	private void MirrorActionKeybinds(bool suppressSecondary)
	{
		_nativePrimaryTrigger = CaptureTrigger(primary: true);
		_nativeSecondaryTrigger = CaptureTrigger(primary: false);
		MergeAction(AriadneMod.UseHeldItemKeybind, primary: true);
		if (suppressSecondary)
		{
			// A direct entity interaction replaces this press completely. Keep the
			// restored snapshot empty too, so opening a conversation in this hook cannot
			// put the physical press back during PreUpdatePlayers and also hit a tile.
			_nativeSecondaryTrigger = default;
			PlayerInput.Triggers.Current.MouseRight = false;
			PlayerInput.Triggers.JustPressed.MouseRight = false;
			PlayerInput.Triggers.JustReleased.MouseRight = false;
		}
		else
		{
			MergeAction(AriadneMod.SecondaryUseKeybind, primary: false);
		}
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
		string targetCycle = DescribeModBinding(AriadneMod.CombatTargetCycleKeybind);
		string status = DescribeModBinding(AriadneMod.PlayerStatusKeybind);
		string scanner = DescribeModBinding(AriadneMod.OpenScannerKeybind);
		string wallTones = DescribeModBinding(AriadneMod.ToggleWallTonesKeybind);
		string waypoints = DescribeModBinding(AriadneMod.OpenWaypointsKeybind);
		string freecam = DescribeModBinding(AriadneMod.FreecamModifierKeybind);
		string radar = DescribeModBinding(AriadneMod.RadarSweepKeybind);
		string hotbarPrevious = DescribeModBinding(AriadneMod.HotbarPreviousKeybind);
		string hotbarNext = DescribeModBinding(AriadneMod.HotbarNextKeybind);
		string housing = DescribeModBinding(AriadneMod.HousingQueryKeybind);
		AriadneMod.ScreenReader.Output(
			$"Gameplay controls. Move with {movement}. Aim with {aiming}. " +
			$"Use the held item with {use}, and secondary use or interact with {secondary}. " +
			$"Terraria's Smart Cursor binding keeps its configured toggle or hold behavior. " +
			$"With the cursor unlocked, interactable entities directly under it take priority, then the exact tile; Smart Cursor uses Terraria's genuine interaction target first. " +
			$"Each manual unlocked-cursor step also describes the NPC, creature, critter, or enemy directly under the cursor. Noninteractable mobs never consume secondary use or block the exact tile behind them. " +
			$"Centered rising and falling ticks mark vertical tile boundaries while climbing a rope-like tile. A spatial reassuring or warning cue classifies ledges beginning at three tiles, using the configured reachable lookahead. Spatial landmark cues identify platforms, minecart tracks, and ropes crossed along the path. " +
			$"Press {targetCycle} to lock the nearest enemy, and press it again to step " +
			$"outward to the next one; pressing it on the farthest enemy releases the lock " +
			$"and recenters the cursor. " +
			$"A locked enemy that goes out of reach is taken back automatically when it returns. " +
			$"Boss parts and shielded pillars can be locked before they can be hurt; a short high rise " +
			$"says the target has opened, and a short high fall says it has closed again. " +
			$"Manual aim always cancels combat lock. " +
			$"Press {status} for a character status readout, {scanner} to scan the visible surroundings, and {wallTones} to toggle wall tones. " +
			$"The radar sounds a bell for anything the scanner would list as it comes into range: one strike for ore, " +
			$"two for a container, three for a creature, four for anything else you have armed. " +
			$"Press {radar} repeatedly to step through a fixed nearest-first snapshot one contact and matching ping at a time; it resets after four idle seconds and always includes dropped items. Hold {targetModifier} with {radar} to switch passive radar off for this session. " +
			$"Hold {targetModifier} with {hotbarPrevious} or {hotbarNext} to step through the hotbar. " +
			$"Press {housing} for the semantic Housing Query cursor over visible enclosed rooms. Hold {targetModifier} with {waypoints} to open waypoints. Hold {freecam} to move a free camera, and release it to return to your body. " +
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
				? SpokenKeyName.Join(bindings, " or ")
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
		return bindings.Count == 0 ? "unbound" : SpokenKeyName.Join(bindings, " or ");
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

	private readonly record struct TriggerSnapshot(bool Current, bool JustPressed, bool JustReleased);
}
