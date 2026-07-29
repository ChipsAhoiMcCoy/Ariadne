#nullable enable

using System;
using Terraria;

namespace Ariadne.Audio;

/// <summary>
/// The rate every Ariadne voice is synthesized at, taken from the output device
/// rather than asserted.
///
/// Nothing in the mod's synthesis is tied to a particular rate: filters, envelopes
/// and smoothing coefficients are all derived from this value. Asserting 44.1 kHz
/// therefore bought nothing and cost a resample, because FAudio has to carry every
/// buffer to the endpoint rate and the resampler in a source voice is linear. On a
/// 48 kHz device, which is the common case, that resample fell on band-passed noise
/// reaching 3.2 kHz and on the pitch-shifted elevation cue, which are exactly the
/// signals it handles worst.
/// </summary>
internal static class AudioFormat
{
	/// <summary>
	/// Used when the device cannot be queried. It is the rate the mod ran at before
	/// the endpoint was consulted, so a failed query changes nothing.
	/// </summary>
	internal const int FallbackSampleRate = 44_100;

	/// <summary>
	/// Guards against a device reporting something a voice cannot be built for. FAudio
	/// itself accepts 1 kHz to 200 kHz, which is far wider than anything worth
	/// synthesizing into.
	/// </summary>
	private const int MinimumSampleRate = 22_050;
	private const int MaximumSampleRate = 192_000;

	private static readonly DeviceQuery Query = ResolveDeviceSampleRate();

	internal static int SampleRate => Query.SampleRate;

	/// <summary>
	/// Why the rate is what it is, for the log line the bus writes once at startup.
	/// The query runs on first touch, which can be before any logger exists.
	/// </summary>
	internal static string Diagnostic => Query.Diagnostic;

	private readonly record struct DeviceQuery(int SampleRate, string Diagnostic);

	private static DeviceQuery ResolveDeviceSampleRate()
	{
		if (Main.dedServ)
		{
			return new(FallbackSampleRate, "server build; device not queried");
		}

		nint engine = nint.Zero;
		try
		{
			if (FAudio.FAudioCreate(out engine, Flags: 0u, FAudio.FAUDIO_DEFAULT_PROCESSOR) != 0 ||
				engine == nint.Zero)
			{
				return new(FallbackSampleRate, "device query unavailable");
			}

			// Index 0 is the default renderer. No mastering voice is created, so this
			// only reads what the endpoint reports and never opens it for output.
			if (FAudio.FAudio_GetDeviceDetails(engine, 0u, out FAudio.FAudioDeviceDetails details) != 0)
			{
				return new(FallbackSampleRate, "device details unavailable");
			}

			uint reported = details.OutputFormat.Format.nSamplesPerSec;
			return reported is >= MinimumSampleRate and <= MaximumSampleRate
				? new((int)reported, "from output device")
				: new(FallbackSampleRate, $"device reported an unusable {reported} Hz");
		}
		catch (Exception exception)
		{
			return new(FallbackSampleRate, $"device query failed: {exception.GetBaseException().Message}");
		}
		finally
		{
			if (engine != nint.Zero)
			{
				try
				{
					FAudio.FAudio_Release(engine);
				}
				catch
				{
					// The engine handle is being discarded either way.
				}
			}
		}
	}
}
