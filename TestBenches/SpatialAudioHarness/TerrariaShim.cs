#nullable enable

// The mixer sources reach Terraria for exactly one thing: whether this is a server
// build, which decides if the audio device is worth asking about. Standing that up
// is cheaper than pulling the game in, and keeps the harness to the audio maths.
namespace Terraria;

internal static class Main
{
	internal static bool dedServ = false;
}
