#nullable enable

namespace Ariadne.Audio;

internal enum NavigationCueKind
{
	Ascending,
	Descending,
	SafeDrop,
	UnsafeDrop,
	Platform,
	MinecartTrack,
	Rope,
	HousingSuitable,
	HousingOccupied,
	HousingUnsuitable,
}

/// <summary>
/// Engine-independent designs for the centered elevation and drop cues. Keeping the
/// authored samples here lets the offline analysis bench measure the exact buffers
/// shipped by the mod without copying their parameters.
/// </summary>
internal static class NavigationCueDesigns
{
	private static readonly ImpactToneDesign[] Designs =
	[
		// A negative sweep begins below its settled frequency and rises into it.
		new(720f, 52f, 0.16f, 0.035f, 0.35f, 36f, -0.48f, 0xA175_40C3u),
		// Its mirror begins above a lower settled frequency and falls into it.
		new(460f, 58f, 0.16f, 0.040f, 0.35f, 40f, 0.48f, 0xD35C_718Au),
		// The safe answer is a light, short upward settling gesture.
		new(560f, 76f, 0.12f, 0.025f, 0.45f, 52f, -0.32f, 0x5AFE_31D2u),
		// The unsafe answer is lower, longer and rougher, with an unmistakable fall.
		new(210f, 150f, 0.28f, 0.22f, 0.30f, 105f, 0.72f, 0xBAD0_74E1u),
		// Traversal landmarks share a dry wooden family but remain countable by contour.
		new(640f, 85f, 0.09f, 0.025f, 0.55f, 48f, -0.18f, 0x91A7_5B21u),
		new(980f, 180f, 0.11f, 0.045f, 0.40f, 70f, 0.24f, 0x7A4C_211Du),
		new(430f, 120f, 0.14f, 0.055f, 0.48f, 58f, -0.42f, 0x40BE_8C17u),
		// Housing states are a related three-way answer: rising, paired, and falling.
		new(720f, 120f, 0.15f, 0.030f, 0.42f, 54f, -0.38f, 0x51A7_03E2u),
		new(590f, 105f, 0.18f, 0.050f, 0.38f, 62f, -0.08f, 0x0CC0_912Du),
		new(330f, 170f, 0.22f, 0.100f, 0.34f, 90f, 0.48f, 0xFA17_310Bu),
	];

	internal static int Count => Designs.Length;

	internal static float[] Render(NavigationCueKind kind) =>
		ImpactToneSynthesizer.Render(Designs[(int)kind]);
}
