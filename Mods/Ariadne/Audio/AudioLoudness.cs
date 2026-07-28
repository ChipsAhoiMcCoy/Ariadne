#nullable enable

using System;

namespace Ariadne.Audio;

/// <summary>
/// Measures how loud a rendered buffer actually sounds, following ITU-R BS.1770's
/// K-weighting and its momentary window.
///
/// Peak normalization cannot answer this question. A 40 ms footstep and a 130 ms
/// knock can share a peak and still differ by several decibels of perceived level,
/// because the ear integrates energy over roughly a fifth of a second. Every
/// Ariadne cue is therefore levelled against this measurement instead of its peak.
/// </summary>
internal static class AudioLoudness
{
	private const float WindowSeconds = 0.4f;

	/// <summary>Loudness in LUFS of a mono buffer heard from both channels.</summary>
	internal static float Measure(ReadOnlySpan<float> mono)
	{
		return Measure(mono, mono);
	}

	/// <summary>
	/// Loudness in LUFS of the loudest window in the buffer. Buffers shorter than the
	/// window are measured as if followed by silence, which is what makes a brief cue
	/// read as quieter than a sustained one at the same peak.
	/// </summary>
	internal static float Measure(ReadOnlySpan<float> left, ReadOnlySpan<float> right)
	{
		int windowSamples = (int)MathF.Round(
			WindowSeconds * SpatialAudioTransformCalculator.SampleRate);
		int sampleCount = Math.Max(Math.Max(left.Length, right.Length), windowSamples);
		double[] energy = new double[sampleCount];
		Accumulate(left, energy);
		Accumulate(right, energy);

		double windowSum = 0d;
		double loudestWindow = 0d;
		for (int index = 0; index < sampleCount; index++)
		{
			windowSum += energy[index];
			if (index >= windowSamples)
			{
				windowSum -= energy[index - windowSamples];
			}
			if (index >= windowSamples - 1)
			{
				loudestWindow = Math.Max(loudestWindow, windowSum / windowSamples);
			}
		}

		return loudestWindow <= 0d
			? float.NegativeInfinity
			: (float)(-0.691d + 10d * Math.Log10(loudestWindow));
	}

	internal static float Peak(ReadOnlySpan<float> samples)
	{
		float peak = 0f;
		foreach (float sample in samples)
		{
			peak = MathF.Max(peak, MathF.Abs(sample));
		}
		return peak;
	}

	private static void Accumulate(ReadOnlySpan<float> samples, double[] energy)
	{
		Biquad shelf = Biquad.HeadShelf(SpatialAudioTransformCalculator.SampleRate);
		Biquad highPass = Biquad.RlbHighPass(SpatialAudioTransformCalculator.SampleRate);
		for (int index = 0; index < samples.Length; index++)
		{
			double weighted = highPass.Process(shelf.Process(samples[index]));
			energy[index] += weighted * weighted;
		}
	}

	/// <summary>
	/// The two K-weighting stages. Coefficients are derived for the current sample
	/// rate rather than copied from the standard's 48 kHz table.
	/// </summary>
	private struct Biquad
	{
		private double _b0, _b1, _b2, _a1, _a2;
		private double _x1, _x2, _y1, _y2;

		internal static Biquad HeadShelf(double sampleRate)
		{
			const double centerFrequency = 1_681.974450955533d;
			const double gainDecibels = 3.999843853973347d;
			const double q = 0.7071752369554196d;

			double k = Math.Tan(Math.PI * centerFrequency / sampleRate);
			double highGain = Math.Pow(10d, gainDecibels / 20d);
			double bandGain = Math.Pow(highGain, 0.4996667741545416d);
			double normalizer = 1d + k / q + k * k;
			return new Biquad
			{
				_b0 = (highGain + bandGain * k / q + k * k) / normalizer,
				_b1 = 2d * (k * k - highGain) / normalizer,
				_b2 = (highGain - bandGain * k / q + k * k) / normalizer,
				_a1 = 2d * (k * k - 1d) / normalizer,
				_a2 = (1d - k / q + k * k) / normalizer,
			};
		}

		internal static Biquad RlbHighPass(double sampleRate)
		{
			const double centerFrequency = 38.13547087602444d;
			const double q = 0.5003270373238773d;

			double k = Math.Tan(Math.PI * centerFrequency / sampleRate);
			double normalizer = 1d + k / q + k * k;
			return new Biquad
			{
				_b0 = 1d,
				_b1 = -2d,
				_b2 = 1d,
				_a1 = 2d * (k * k - 1d) / normalizer,
				_a2 = (1d - k / q + k * k) / normalizer,
			};
		}

		internal double Process(double input)
		{
			double output = _b0 * input + _b1 * _x1 + _b2 * _x2 - _a1 * _y1 - _a2 * _y2;
			_x2 = _x1;
			_x1 = input;
			_y2 = _y1;
			_y1 = output;
			return output;
		}
	}
}
