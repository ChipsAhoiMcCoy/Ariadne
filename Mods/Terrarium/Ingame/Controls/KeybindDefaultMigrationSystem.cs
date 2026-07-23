#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using Terraria;
using Terraria.GameInput;
using Terraria.ModLoader;

namespace Terrarium.Ingame.Controls;

/// <summary>
/// Assigns Terrarium's declared defaults once for profiles that predate a
/// binding. Later user rebinding or clearing remains authoritative.
/// </summary>
[Autoload(Side = ModSide.Client)]
internal sealed class KeybindDefaultMigrationSystem : ModSystem
{
	private const int CurrentMigrationVersion = 1;
	private const string MigrationMarkerFileName = "default-bindings-v1.applied";

	private bool _attemptedThisSession;

	public override void PostUpdateInput()
	{
		if (_attemptedThisSession)
		{
			return;
		}

		if (File.Exists(MigrationMarkerPath))
		{
			_attemptedThisSession = true;
			return;
		}

		IReadOnlyList<RegisteredKeybindDefault> defaults = TerrariumMod.KeybindDefaults;
		if (defaults.Count == 0 || PlayerInput.Profiles.Count == 0)
		{
			return;
		}

		foreach (PlayerInputProfile profile in PlayerInput.Profiles.Values)
		{
			if (!profile.InputModes.TryGetValue(InputMode.Keyboard, out KeyConfiguration? keyboard))
			{
				return;
			}

			foreach (RegisteredKeybindDefault binding in defaults)
			{
				if (!keyboard.KeyStatus.ContainsKey(binding.FullName))
				{
					// PlayerInput is reinitialized after mod content finishes
					// loading. Wait for that pass rather than editing a partial
					// profile and losing the assignment immediately afterward.
					return;
				}
			}
		}

		int assignedCount = AssignMissingDefaults(defaults);
		if (assignedCount > 0 && !PlayerInput.Save())
		{
			Mod.Logger.Warn(
				$"Assigned {assignedCount} missing Terrarium controls in memory, " +
				"but tModLoader could not save the input profiles.");
			_attemptedThisSession = true;
			return;
		}

		try
		{
			WriteMigrationMarker();
		}
		catch (Exception exception)
		{
			Mod.Logger.Warn(
				$"Terrarium controls were initialized, but the one-time migration marker " +
				$"could not be saved: {exception.GetBaseException().Message}");
			_attemptedThisSession = true;
			return;
		}

		if (assignedCount > 0)
		{
			Mod.Logger.Info(
				$"Assigned {assignedCount} missing default Terrarium controls across " +
				$"{PlayerInput.Profiles.Count} input profiles.");
		}

		_attemptedThisSession = true;
	}

	public override void Unload()
	{
		_attemptedThisSession = false;
	}

	private static int AssignMissingDefaults(IReadOnlyList<RegisteredKeybindDefault> defaults)
	{
		int assignedCount = 0;
		foreach (PlayerInputProfile profile in PlayerInput.Profiles.Values)
		{
			KeyConfiguration keyboard = profile.InputModes[InputMode.Keyboard];
			foreach (RegisteredKeybindDefault binding in defaults)
			{
				List<string> assignedKeys = keyboard.KeyStatus[binding.FullName];
				if (assignedKeys.Count != 0)
				{
					continue;
				}

				assignedKeys.Add(binding.Key);
				assignedCount++;
			}
		}
		return assignedCount;
	}

	private static string MigrationMarkerPath => Path.Combine(
		Main.SavePath,
		"Terrarium",
		"Controls",
		MigrationMarkerFileName);

	private static void WriteMigrationMarker()
	{
		string markerPath = MigrationMarkerPath;
		string? directory = Path.GetDirectoryName(markerPath);
		if (!string.IsNullOrEmpty(directory))
		{
			Directory.CreateDirectory(directory);
		}
		File.WriteAllText(markerPath, CurrentMigrationVersion.ToString());
	}
}
