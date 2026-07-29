#nullable enable

using Ariadne.Audio;

namespace Ariadne.TestBenches.AudioAnalysis;

/// <summary>
/// The questions a listener would otherwise have to answer by ear, asked of the
/// samples instead: how close the mix came to the rails, whether anything was added
/// that was not in the signal, and whether a cue ended or was stopped.
/// </summary>
internal static class Measure
{
	internal static float Peak(ReadOnlySpan<float> samples)
	{
		float peak = 0f;
		foreach (float sample in samples)
		{
			peak = MathF.Max(peak, MathF.Abs(sample));
		}
		return peak;
	}

	internal static float Peak(ReadOnlySpan<float> left, ReadOnlySpan<float> right)
	{
		return MathF.Max(Peak(left), Peak(right));
	}

	/// <summary>A level, against full scale.</summary>
	internal static string Decibels(float amplitude)
	{
		return amplitude <= 0f ? "-inf dBFS" : $"{20f * MathF.Log10(amplitude):F2} dBFS";
	}

	/// <summary>A gain, as the change it makes rather than as a level.</summary>
	internal static string Gain(float gain)
	{
		return gain <= 0f ? "-inf dB" : $"{20f * MathF.Log10(gain):F2} dB";
	}

	/// <summary>
	/// The highest value the waveform between the samples actually reaches. A buffer
	/// can sit under full scale at every sample and still overshoot once a converter
	/// or a resampler reconstructs the curve through them, which is what clips on the
	/// way out of the machine rather than inside the mod.
	/// </summary>
	internal static float TruePeak(ReadOnlySpan<float> samples, int oversample = 4)
	{
		const int halfTaps = 16;
		float peak = 0f;
		for (int index = 0; index < samples.Length; index++)
		{
			for (int phase = 0; phase < oversample; phase++)
			{
				float fraction = phase / (float)oversample;
				float sum = 0f;
				for (int tap = -halfTaps + 1; tap <= halfTaps; tap++)
				{
					int source = index + tap;
					if ((uint)source >= samples.Length)
					{
						continue;
					}
					sum += samples[source] * WindowedSinc(tap - fraction, halfTaps);
				}
				peak = MathF.Max(peak, MathF.Abs(sum));
			}
		}
		return peak;
	}

	private static float WindowedSinc(float x, int halfTaps)
	{
		if (MathF.Abs(x) < 1e-6f)
		{
			return 1f;
		}
		if (MathF.Abs(x) >= halfTaps)
		{
			return 0f;
		}

		float sinc = MathF.Sin(MathF.PI * x) / (MathF.PI * x);
		// Blackman window over the tap span, which keeps the reconstruction's own
		// ripple far below the overshoot being measured.
		float position = (x + halfTaps) / (2f * halfTaps);
		float window =
			0.42f -
			0.5f * MathF.Cos(MathF.Tau * position) +
			0.08f * MathF.Cos(2f * MathF.Tau * position);
		return sinc * window;
	}

	internal static int CountAtOrAbove(ReadOnlySpan<float> samples, float threshold)
	{
		int count = 0;
		foreach (float sample in samples)
		{
			if (MathF.Abs(sample) >= threshold)
			{
				count++;
			}
		}
		return count;
	}

	internal static int CountNonFinite(ReadOnlySpan<float> samples)
	{
		int count = 0;
		foreach (float sample in samples)
		{
			if (!float.IsFinite(sample))
			{
				count++;
			}
		}
		return count;
	}

	/// <summary>
	/// Total harmonic distortion: how much energy sits at whole multiples of a tone
	/// that was not there when the tone went in. This is the measurement that answers
	/// "is it distorting" for a steady signal, because a limiter that squares off a
	/// waveform puts its energy exactly here.
	/// </summary>
	internal static float TotalHarmonicDistortion(
		ReadOnlySpan<float> samples,
		float fundamentalHertz,
		int sampleRate,
		int harmonicCount = 10)
	{
		float[] windowed = new float[samples.Length];
		for (int index = 0; index < samples.Length; index++)
		{
			float position = index / (float)(samples.Length - 1);
			float hann = 0.5f * (1f - MathF.Cos(MathF.Tau * position));
			windowed[index] = samples[index] * hann;
		}

		double fundamental = Magnitude(windowed, fundamentalHertz, sampleRate);
		if (fundamental <= 0d)
		{
			return 0f;
		}

		double harmonicEnergy = 0d;
		for (int harmonic = 2; harmonic <= harmonicCount; harmonic++)
		{
			float frequency = fundamentalHertz * harmonic;
			if (frequency >= sampleRate * 0.5f)
			{
				break;
			}
			double magnitude = Magnitude(windowed, frequency, sampleRate);
			harmonicEnergy += magnitude * magnitude;
		}

		return (float)(Math.Sqrt(harmonicEnergy) / fundamental);
	}

	/// <summary>One bin of a discrete Fourier transform, by Goertzel's recurrence.</summary>
	private static double Magnitude(ReadOnlySpan<float> samples, float frequency, int sampleRate)
	{
		double angle = 2d * Math.PI * frequency / sampleRate;
		double coefficient = 2d * Math.Cos(angle);
		double previous = 0d;
		double beforeThat = 0d;
		foreach (float sample in samples)
		{
			double current = sample + coefficient * previous - beforeThat;
			beforeThat = previous;
			previous = current;
		}

		double real = previous - beforeThat * Math.Cos(angle);
		double imaginary = beforeThat * Math.Sin(angle);
		return Math.Sqrt(real * real + imaginary * imaginary) / samples.Length;
	}

	/// <summary>
	/// The largest jump between neighbouring samples. A click is a discontinuity, so
	/// comparing this inside a block against the same figure across block boundaries
	/// says whether the per-block gain the limiter applies is audible as a step.
	/// </summary>
	internal static float LargestStep(ReadOnlySpan<float> samples)
	{
		float worst = 0f;
		for (int index = 1; index < samples.Length; index++)
		{
			worst = MathF.Max(worst, MathF.Abs(samples[index] - samples[index - 1]));
		}
		return worst;
	}

	internal static float LargestStepAcrossBoundaries(ReadOnlySpan<float> samples, int blockSize)
	{
		float worst = 0f;
		for (int index = blockSize; index < samples.Length; index += blockSize)
		{
			worst = MathF.Max(worst, MathF.Abs(samples[index] - samples[index - 1]));
		}
		return worst;
	}

	/// <summary>
	/// Root-mean-square level in fixed windows, which is what a duck under a loud cue
	/// shows up in. Used to ask whether the limiter pumps the terrain bed.
	/// </summary>
	internal static float[] ShortTermRms(ReadOnlySpan<float> samples, int windowSamples)
	{
		int windows = samples.Length / windowSamples;
		float[] trace = new float[Math.Max(0, windows)];
		for (int window = 0; window < trace.Length; window++)
		{
			double sum = 0d;
			for (int index = 0; index < windowSamples; index++)
			{
				float sample = samples[window * windowSamples + index];
				sum += sample * sample;
			}
			trace[window] = (float)Math.Sqrt(sum / windowSamples);
		}
		return trace;
	}

	/// <summary>
	/// The loudness of a cue as the listener receives it: both channels together,
	/// through the mod's own meter, so the figure is comparable to the references the
	/// cues are authored against.
	/// </summary>
	internal static float Loudness(ReadOnlySpan<float> left, ReadOnlySpan<float> right)
	{
		return AudioLoudness.Measure(left, right);
	}

	/// <summary>
	/// Where in a buffer the signal was last meaningfully above silence. A cue whose
	/// last audible sample sits at the very end of what was rendered was cut off
	/// rather than finished.
	/// </summary>
	internal static int LastSampleAbove(ReadOnlySpan<float> samples, float threshold)
	{
		for (int index = samples.Length - 1; index >= 0; index--)
		{
			if (MathF.Abs(samples[index]) > threshold)
			{
				return index;
			}
		}
		return -1;
	}
}
