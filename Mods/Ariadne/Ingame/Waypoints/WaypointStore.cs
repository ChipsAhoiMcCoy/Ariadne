#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Terraria;
using Terraria.ModLoader;

namespace Ariadne.Ingame.Waypoints;

/// <summary>
/// Per-world waypoints, saved beside Ariadne's other client files rather than in the world.
/// The mod is <c>NoSync</c> and has to work against servers that do not have it, so world data
/// is unavailable in the case that matters most. Worlds are keyed on the same identity vanilla
/// uses to name map files, which is set from the world file in single player and delivered to
/// clients in the WorldData packet.
/// </summary>
internal sealed class WaypointStore
{
	private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

	private readonly Mod _mod;
	private readonly List<Waypoint> _waypoints = [];
	private string? _worldKey;

	internal WaypointStore(Mod mod)
	{
		_mod = mod;
	}

	internal IReadOnlyList<Waypoint> Waypoints
	{
		get
		{
			EnsureLoaded();
			return _waypoints;
		}
	}

	/// <summary>
	/// Loads on first use rather than on world load. A multiplayer client receives the world's
	/// unique id in a packet, so the key this store needs is not reliably available at the
	/// moment the world is considered loaded.
	/// </summary>
	private void EnsureLoaded()
	{
		string? worldKey = ResolveWorldKey();
		if (worldKey is null || worldKey == _worldKey)
		{
			return;
		}

		_waypoints.Clear();
		_worldKey = worldKey;

		string path = GetPath(_worldKey);
		if (!File.Exists(path))
		{
			return;
		}

		try
		{
			List<Waypoint>? loaded = JsonSerializer.Deserialize<List<Waypoint>>(File.ReadAllText(path));
			if (loaded is null)
			{
				return;
			}
			foreach (Waypoint waypoint in loaded)
			{
				if (!string.IsNullOrWhiteSpace(waypoint.Name))
				{
					_waypoints.Add(waypoint);
				}
			}
		}
		catch (Exception exception)
		{
			_mod.Logger.Warn(
				$"Ariadne could not read the waypoints for this world: {exception.GetBaseException().Message}");
		}
	}

	internal void Unload()
	{
		_waypoints.Clear();
		_worldKey = null;
	}

	internal Waypoint Add(string name, int tileX, int tileY)
	{
		EnsureLoaded();
		Waypoint waypoint = new()
		{
			Name = Sanitize(name),
			TileX = tileX,
			TileY = tileY,
		};
		_waypoints.Add(waypoint);
		Save();
		return waypoint;
	}

	internal void Rename(Waypoint waypoint, string name)
	{
		EnsureLoaded();
		waypoint.Name = Sanitize(name);
		Save();
	}

	internal void Remove(Waypoint waypoint)
	{
		EnsureLoaded();
		if (_waypoints.Remove(waypoint))
		{
			Save();
		}
	}

	private static string Sanitize(string name)
	{
		string trimmed = name.Trim();
		if (trimmed.Length == 0)
		{
			return "Waypoint";
		}
		return trimmed.Length > Waypoint.MaximumNameLength
			? trimmed[..Waypoint.MaximumNameLength]
			: trimmed;
	}

	private void Save()
	{
		if (_worldKey is null)
		{
			return;
		}

		try
		{
			string path = GetPath(_worldKey);
			string? directory = Path.GetDirectoryName(path);
			if (!string.IsNullOrEmpty(directory))
			{
				Directory.CreateDirectory(directory);
			}
			File.WriteAllText(path, JsonSerializer.Serialize(_waypoints, SerializerOptions));
		}
		catch (Exception exception)
		{
			_mod.Logger.Warn(
				$"Ariadne could not save the waypoints for this world: {exception.GetBaseException().Message}");
		}
	}

	private static string GetPath(string worldKey)
	{
		return Path.Combine(Main.SavePath, "Ariadne", "Waypoints", $"{worldKey}.json");
	}

	private static string? ResolveWorldKey()
	{
		Guid uniqueId = Main.ActiveWorldFileData?.UniqueId ?? Guid.Empty;
		if (uniqueId != Guid.Empty)
		{
			return uniqueId.ToString("N");
		}

		// Mirrors MapHelper's fallback for worlds that predate the unique id.
		return Main.worldID != 0 ? $"id{Main.worldID}" : null;
	}
}
