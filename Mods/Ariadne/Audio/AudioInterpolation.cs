#nullable enable

namespace Ariadne.Audio;

/// <summary>
/// Reading a signal between two of its samples.
///
/// Straight lines between neighbouring samples are not a neutral way to do it. The
/// error they leave is largest halfway between samples and vanishes on top of one,
/// so a linear read behaves as a low-pass filter whose corner moves with the
/// fraction being asked for. Ariadne asks for fractional positions in three places
/// that all matter: the interaural delay, whose fraction tracks where a source sits
/// in the field; the pitch shift that carries screen height; and the resample of
/// Terraria's own cues onto the mixer's rate. In the first that made timbre follow
/// the pan, and in the second it made the elevation cue audibly grainy.
/// </summary>
internal static class AudioInterpolation
{
	/// <summary>
	/// Four-point third-order Hermite interpolation between <paramref name="current"/>
	/// and <paramref name="next"/>, using one neighbour on each side to estimate the
	/// slope at both ends. Its response is flat enough across the fraction that a
	/// moving read no longer colours what it reads.
	/// </summary>
	internal static float Hermite(
		float previous,
		float current,
		float next,
		float following,
		float fraction)
	{
		float c1 = 0.5f * (next - previous);
		float c2 = previous - 2.5f * current + 2f * next - 0.5f * following;
		float c3 = 0.5f * (following - previous) + 1.5f * (current - next);
		return ((c3 * fraction + c2) * fraction + c1) * fraction + current;
	}
}
