#nullable enable

using Microsoft.Xna.Framework;
using Terraria;
using Ariadne.Audio;
using Ariadne.Ingame.Freecam;

namespace Ariadne.Ingame;

/// <summary>
/// The body and field used by spatial awareness. Gameplay and semantic
/// interaction systems deliberately continue to use <see cref="Main.LocalPlayer"/>.
/// </summary>
internal readonly record struct SpatialObserverSnapshot(
	Vector2 Center,
	int Width,
	int Height,
	float GravityDirection,
	bool IsVirtual)
{
	/// <summary>
	/// The field every spatial cue is measured against, in world pixels. Fixed rather
	/// than taken from <see cref="Main.Camera"/> so that a cue means the same thing for
	/// every player. The camera rectangle is the resolution divided by the game zoom,
	/// which gave a player at 1280x720, or one holding the zoom slider at 2x, two thirds
	/// of the default reach, and gave an ultrawide a vertical field a third shorter than
	/// its horizontal one. This is 120 by 67.5 tiles, which is what the cue levels and
	/// the default ranges were tuned against.
	/// </summary>
	internal static readonly Vector2 FieldSize = new(1920f, 1080f);

	/// <summary>
	/// The field as a rectangle, for callers that test containment rather than
	/// direction. It is always centred on the body and deliberately not clamped to the
	/// world edge: nothing draws it, so letting it hang past the edge costs nothing and
	/// keeps both sides the same length everywhere in the world.
	/// </summary>
	internal Vector2 FieldPosition => Center - FieldSize * 0.5f;

	internal Vector2 NormalizeToField(Vector2 worldPosition)
	{
		return SpatialFieldPosition.Normalize(worldPosition, Center, FieldSize);
	}
}

internal static class SpatialObserverContext
{
	internal static uint Revision => FreecamSystem.ObserverRevision;

	internal static SpatialObserverSnapshot Current
	{
		get
		{
			if (FreecamSystem.TryGetVirtualBody(
				out Vector2 center,
				out int width,
				out int height,
				out float gravityDirection))
			{
				return new(center, width, height, gravityDirection, IsVirtual: true);
			}

			Player player = Main.LocalPlayer;
			return new(
				player.Center,
				player.width,
				player.height,
				player.gravDir < 0f ? -1f : 1f,
				IsVirtual: false);
		}
	}
}
