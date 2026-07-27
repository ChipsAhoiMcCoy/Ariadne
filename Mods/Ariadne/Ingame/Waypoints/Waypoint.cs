#nullable enable

namespace Ariadne.Ingame.Waypoints;

internal sealed class Waypoint
{
	internal const int MaximumNameLength = 40;

	public string Name { get; set; } = string.Empty;

	public int TileX { get; set; }

	public int TileY { get; set; }
}
