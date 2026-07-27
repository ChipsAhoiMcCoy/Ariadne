#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using Terraria;
using Terraria.GameInput;
using Terraria.ModLoader;

namespace Ariadne.Ingame.Controls;

/// <summary>
/// Assigns Ariadne's declared defaults once for profiles that predate a
/// binding. Later user rebinding or clearing remains authoritative.
/// </summary>
[Autoload(Side = ModSide.Client)]
internal sealed class KeybindDefaultMigrationSystem : ModSystem
{
	private const int CurrentMigrationVersion = 4;
	private const string MigrationMarkerFileName = "default-bindings.version";
	private const string LegacyVersionOneMarkerFileName = "default-bindings-v1.applied";

	private bool _attemptedThisSession;

	public override void PostUpdateInput()
	{
		if (_attemptedThisSession)
		{
			return;
		}

		int appliedVersion = ReadAppliedVersion();
		if (appliedVersion >= CurrentMigrationVersion)
		{
			_attemptedThisSession = true;
			return;
		}

		IReadOnlyList<RegisteredKeybindDefault> defaults = AriadneMod.KeybindDefaults;
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

		int assignedCount = AssignIntroducedDefaults(defaults, appliedVersion);
		if (assignedCount > 0 && !PlayerInput.Save())
		{
			Mod.Logger.Warn(
				$"Assigned {assignedCount} missing Ariadne controls in memory, " +
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
				$"Ariadne controls were initialized, but the one-time migration marker " +
				$"could not be saved: {exception.GetBaseException().Message}");
			_attemptedThisSession = true;
			return;
		}

		if (assignedCount > 0)
		{
			Mod.Logger.Info(
				$"Assigned {assignedCount} missing default Ariadne controls across " +
				$"{PlayerInput.Profiles.Count} input profiles.");
		}

		_attemptedThisSession = true;
	}

	public override void Unload()
	{
		_attemptedThisSession = false;
	}

	private static int AssignIntroducedDefaults(
		IReadOnlyList<RegisteredKeybindDefault> defaults,
		int appliedVersion)
	{
		int assignedCount = 0;
		foreach (PlayerInputProfile profile in PlayerInput.Profiles.Values)
		{
			KeyConfiguration keyboard = profile.InputModes[InputMode.Keyboard];
			foreach (RegisteredKeybindDefault binding in defaults)
			{
				if (binding.IntroducedVersion <= appliedVersion)
				{
					continue;
				}

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
		"Ariadne",
		"Controls",
		MigrationMarkerFileName);

	private int ReadAppliedVersion()
	{
		string markerPath = MigrationMarkerPath;
		if (!File.Exists(markerPath) && File.Exists(LegacyVersionOneMarkerPath))
		{
			markerPath = LegacyVersionOneMarkerPath;
		}
		if (!File.Exists(markerPath))
		{
			return 0;
		}

		try
		{
			string marker = File.ReadAllText(markerPath).Trim();
			return int.TryParse(marker, out int version)
				? Math.Max(0, version)
				: 1;
		}
		catch (Exception exception)
		{
			Mod.Logger.Warn(
				$"Ariadne could not read the default-control migration marker: " +
				$"{exception.GetBaseException().Message}");
			return CurrentMigrationVersion;
		}
	}

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

	private static string LegacyVersionOneMarkerPath => Path.Combine(
		Main.SavePath,
		"Ariadne",
		"Controls",
		LegacyVersionOneMarkerFileName);
}
