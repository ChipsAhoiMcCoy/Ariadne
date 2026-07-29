#nullable enable

using System;

namespace Ariadne.Audio;

/// <summary>
/// Anything that writes into the shared mix. Sources are rendered additively and in
/// registration order, on the game thread, from <see cref="AriadneAudioBus.Pump"/>.
/// </summary>
internal interface IAudioBusSource
{
	/// <summary>
	/// Adds this source's contribution to the mix. Returning false retires the source,
	/// which is how a one-shot removes itself once it has finished sounding.
	/// </summary>
	bool Render(Span<float> left, Span<float> right);
}
