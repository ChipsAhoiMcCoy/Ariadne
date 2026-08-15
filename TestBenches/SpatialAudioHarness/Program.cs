#nullable enable

using Ariadne.Audio;

namespace Ariadne.TestBenches.SpatialAudio;

/// <summary>
/// Checks the parts of the mixer that can be answered without a listener: that the
/// delay line is long enough for the configured interaural delay at whatever rate
/// the device runs, that the interpolator is exact where it should be, that the pan
/// still lands on the gains the level calibration assumes, and that the loudness
/// meter and its trim still agree once the rate is no longer 44.1 kHz.
/// </summary>
internal static class Program
{
	private static int _failures;

	private static int Main()
	{
		int rate = SpatialAudioTransformCalculator.SampleRate;
		Console.WriteLine($"Device sample rate: {rate} Hz ({AudioFormat.Diagnostic})");
		Console.WriteLine();

		HermiteIsExactOnAQuadratic();
		HermitePassesThroughItsKnots();
		HermiteBeatsLinearOnATone();
		CentredSourceLandsOnTheCalibratedGain();
		HardPannedSourceReachesTheFullInteauralDelay();
		DelayLineHoldsTheLongestConfigurableDelay();
		MovingSourceStaysContinuous();
		LoudnessTrimReachesItsReference();
		HeadShadowLeavesACentredSourceAlone();
		UnpitchedPlaybackReturnsTheBufferItWasGiven();
		CentredCueReachesBothChannelsWhole();
		OneShotRetiresItself();
		DistanceAttenuationCanBeDisabled();

		Console.WriteLine();
		Console.WriteLine(_failures == 0 ? "All checks passed." : $"{_failures} check(s) FAILED.");
		return _failures == 0 ? 0 : 1;
	}

	private static void DistanceAttenuationCanBeDisabled()
	{
		float enabled = SpatialAudioDistanceGain.FromProximity(0.2f, attenuationEnabled: true);
		float disabled = SpatialAudioDistanceGain.FromProximity(0.2f, attenuationEnabled: false);
		(float[] attenuated, _) = RenderImpulse(0f, itdEnabled: false, distanceGain: enabled);
		(float[] fullLevel, _) = RenderImpulse(0f, itdEnabled: false, distanceGain: disabled);
		Report(
			"Distance attenuation toggle preserves full level when disabled",
			MathF.Abs(enabled - 0.2f) < 1e-6f && MathF.Abs(disabled - 1f) < 1e-6f &&
				MathF.Abs(fullLevel[0] / attenuated[0] - 5f) < 1e-4f,
			$"enabled gain {enabled:F2}, disabled gain {disabled:F2}, rendered level ratio {fullLevel[0] / attenuated[0]:F2}x");
	}

	private static void HermiteIsExactOnAQuadratic()
	{
		// The end slopes are central differences, which are exact to second order. That
		// makes a quadratic the strongest polynomial this reproduces exactly; a cubic
		// leaves a small residual by construction, not by mistake.
		static float Quadratic(float t) => -1.3f * t * t + 0.4f * t + 0.9f;

		float worst = 0f;
		for (float fraction = 0f; fraction <= 1f; fraction += 0.05f)
		{
			float actual = AudioInterpolation.Hermite(
				Quadratic(-1f), Quadratic(0f), Quadratic(1f), Quadratic(2f), fraction);
			worst = MathF.Max(worst, MathF.Abs(actual - Quadratic(fraction)));
		}

		Report("Hermite reproduces a quadratic", worst < 1e-5f, $"worst error {worst:E2}");
	}

	private static void HermiteBeatsLinearOnATone()
	{
		// The property the change was made for: reading a tone at a fractional offset
		// should return the tone, not a quieter and duller version of it. Linear's
		// error peaks halfway between samples, which is what made a panned source's
		// timbre follow its position.
		int rate = SpatialAudioTransformCalculator.SampleRate;
		const float frequency = 4_000f;
		static float Tone(double t, float frequency, int rate) =>
			MathF.Sin((float)(MathF.Tau * frequency * t / rate));

		float worstHermite = 0f;
		float worstLinear = 0f;
		for (float fraction = 0f; fraction < 1f; fraction += 0.01f)
		{
			for (int index = 8; index < 64; index++)
			{
				float truth = Tone(index + fraction, frequency, rate);
				float hermite = AudioInterpolation.Hermite(
					Tone(index - 1, frequency, rate),
					Tone(index, frequency, rate),
					Tone(index + 1, frequency, rate),
					Tone(index + 2, frequency, rate),
					fraction);
				float linear =
					Tone(index, frequency, rate) * (1f - fraction) +
					Tone(index + 1, frequency, rate) * fraction;
				worstHermite = MathF.Max(worstHermite, MathF.Abs(hermite - truth));
				worstLinear = MathF.Max(worstLinear, MathF.Abs(linear - truth));
			}
		}

		float improvement = worstLinear / MathF.Max(worstHermite, 1e-9f);
		Report(
			"Hermite reads a tone far more accurately than linear",
			improvement > 10f,
			$"{frequency:F0} Hz worst error: linear {worstLinear:E2}, Hermite {worstHermite:E2} " +
			$"({improvement:F0}x better)");
	}

	private static void HermitePassesThroughItsKnots()
	{
		float atZero = AudioInterpolation.Hermite(0.3f, -0.5f, 0.8f, 0.1f, 0f);
		float atOne = AudioInterpolation.Hermite(0.3f, -0.5f, 0.8f, 0.1f, 1f);
		Report(
			"Hermite passes through its knots",
			MathF.Abs(atZero - -0.5f) < 1e-6f && MathF.Abs(atOne - 0.8f) < 1e-6f,
			$"f(0)={atZero:F6} f(1)={atOne:F6}");
	}

	private static void CentredSourceLandsOnTheCalibratedGain()
	{
		// Every authored level is trimmed against CenteredChannelGain, so a centred
		// impulse has to arrive at exactly that amplitude in both ears or the whole
		// loudness calibration is measuring something the mixer does not produce.
		(float[] left, float[] right) = RenderImpulse(normalizedX: 0f, itdEnabled: true);
		float expected = SpatialAudioTransformCalculator.CenteredChannelGain;
		bool matches =
			MathF.Abs(left[0] - expected) < 1e-5f &&
			MathF.Abs(right[0] - expected) < 1e-5f;
		Report(
			"Centred source lands on the calibrated channel gain",
			matches,
			$"left {left[0]:F6}, right {right[0]:F6}, expected {expected:F6}");
	}

	private static void HardPannedSourceReachesTheFullInteauralDelay()
	{
		int rate = SpatialAudioTransformCalculator.SampleRate;
		(float[] left, float[] right) = RenderImpulse(normalizedX: 1f, itdEnabled: true);
		int expectedDelay = (int)MathF.Round(0.65f / 1_000f * rate);
		int leftPeak = IndexOfPeak(left);
		int rightPeak = IndexOfPeak(right);
		Report(
			"Hard pan delays the far ear by the configured amount",
			rightPeak == 0 && Math.Abs(leftPeak - expectedDelay) <= 1,
			$"near ear at {rightPeak}, far ear at {leftPeak}, expected {expectedDelay}");
	}

	private static void DelayLineHoldsTheLongestConfigurableDelay()
	{
		// The slider goes to a full millisecond. A fixed sixty-four-sample line covered
		// that at 44.1 kHz and silently truncated above roughly 60 kHz, so this is the
		// check that the line is now sized from the rate.
		int rate = SpatialAudioTransformCalculator.SampleRate;
		(float[] left, _) = RenderImpulse(normalizedX: 1f, itdEnabled: true, maximumItd: 1f);
		int expectedDelay = (int)MathF.Round(1f / 1_000f * rate);
		int leftPeak = IndexOfPeak(left);
		Report(
			"Delay line holds a full millisecond at the device rate",
			Math.Abs(leftPeak - expectedDelay) <= 1,
			$"far ear at {leftPeak}, expected {expectedDelay} ({rate} Hz)");
	}

	private static void MovingSourceStaysContinuous()
	{
		// The transform is solved on a control grid and carried across it by straight
		// lines. If that grid were audible it would show up as a step at a block edge,
		// so a source swept across the whole field must not produce one.
		SpatialAudioEmitter emitter = new(gainAttackSeconds: 0.001f);
		SpatialAudioSettings settings = new(true, 0.65f, false);
		emitter.SetTargetImmediately(new(-1f, 0f, 1f));

		int rate = SpatialAudioTransformCalculator.SampleRate;
		int blocks = 120;
		float[] left = new float[256];
		float[] right = new float[256];
		float worstStep = 0f;
		float previous = 0f;
		bool finite = true;

		SineSource source = new(440f, rate);
		for (int block = 0; block < blocks; block++)
		{
			Array.Clear(left);
			Array.Clear(right);
			float sweep = block / (float)(blocks - 1) * 2f - 1f;
			emitter.SetTarget(new(sweep, 0f, 1f));
			emitter.Render(source, settings, left, right);
			for (int index = 0; index < left.Length; index++)
			{
				finite &= float.IsFinite(left[index]) && float.IsFinite(right[index]);
				worstStep = MathF.Max(worstStep, MathF.Abs(left[index] - previous));
				previous = left[index];
			}
		}

		// One cycle of a 440 Hz sine advances by well under this between samples; a
		// control-grid discontinuity would be far larger.
		float ceiling = 0.15f;
		Report(
			"Swept source stays continuous across control blocks",
			finite && worstStep < ceiling,
			$"largest sample-to-sample step {worstStep:F4} (ceiling {ceiling:F2}), all finite: {finite}");
	}

	private static void LoudnessTrimReachesItsReference()
	{
		int rate = SpatialAudioTransformCalculator.SampleRate;
		float[] samples = new float[rate];
		for (int index = 0; index < samples.Length; index++)
		{
			samples[index] = 0.2f * MathF.Sin(MathF.Tau * 1_000f * index / rate);
		}

		float reference = AuthoredAudioLevels.ReferenceLoudness;
		float trim = AuthoredAudioLevels.LoudnessTrim(samples, reference);
		for (int index = 0; index < samples.Length; index++)
		{
			samples[index] *= trim;
		}

		float measured = AudioLoudness.Measure(samples);
		Report(
			"Loudness trim reaches its reference at the device rate",
			MathF.Abs(measured - reference) < 0.1f,
			$"measured {measured:F3} LUFS against a {reference:F1} LUFS reference");
	}

	private static void HeadShadowLeavesACentredSourceAlone()
	{
		// The shadow has to cross through the midline without a step, or a source
		// walking past the player would click as it changed ears.
		(float[] withoutShadow, _) = RenderImpulse(0f, itdEnabled: false, headShadow: false);
		(float[] withShadow, _) = RenderImpulse(0f, itdEnabled: false, headShadow: true);
		float difference = MathF.Abs(withoutShadow[0] - withShadow[0]);
		Report(
			"Head shadow leaves a centred source untouched",
			difference < 1e-6f,
			$"difference at the midline {difference:E2}");
	}

	private static void UnpitchedPlaybackReturnsTheBufferItWasGiven()
	{
		// At a ratio of one the interpolator sits exactly on each sample, so a cue
		// played at its authored pitch must come back bit for bit. Anything else would
		// mean the pre-rendered banks are now being filtered on their way out.
		float[] source = new float[64];
		for (int index = 0; index < source.Length; index++)
		{
			source[index] = MathF.Sin(index * 0.37f) * 0.8f;
		}

		PcmPlaybackVoice voice = new(source, source.Length, 1f);
		float worst = 0f;
		for (int index = 0; index < source.Length; index++)
		{
			worst = MathF.Max(worst, MathF.Abs(voice.ReadSample(1f) - source[index]));
		}

		Report(
			"Unpitched playback returns its buffer unchanged",
			worst < 1e-6f,
			$"worst deviation {worst:E2}");
	}

	private static void CentredCueReachesBothChannelsWhole()
	{
		// Footsteps, bumps and the heartbeat are levelled against the plain reference
		// because they used to reach both channels at unity through a sound effect.
		// Moving them onto the bus must not quietly cost them the three decibels the
		// spatializer's pan would take.
		float[] source = [1f, 0f, 0f, 0f, 0f, 0f, 0f, 0f];
		MonoOneShotVoice voice = new(source, 1f, volume: 0.5f);
		float[] left = new float[8];
		float[] right = new float[8];
		voice.Render(left, right);
		Report(
			"Centred cue reaches both channels whole",
			MathF.Abs(left[0] - 0.5f) < 1e-6f && MathF.Abs(right[0] - 0.5f) < 1e-6f,
			$"left {left[0]:F6}, right {right[0]:F6}, expected 0.500000 in each");
	}

	private static void OneShotRetiresItself()
	{
		// A one-shot that never returns false would accumulate on the bus for the rest
		// of the session, so this is the check that the frame budget actually expires.
		float[] source = new float[500];
		MonoOneShotVoice voice = new(source, 1f, volume: 1f);
		float[] left = new float[256];
		float[] right = new float[256];
		bool first = voice.Render(left, right);
		bool second = voice.Render(left, right);
		Report(
			"One-shot retires itself once its frames are spent",
			first && !second && voice.IsFinished,
			$"after one block {first}, after two {second}, finished {voice.IsFinished}");
	}

	private static (float[] Left, float[] Right) RenderImpulse(
		float normalizedX,
		bool itdEnabled,
		float maximumItd = 0.65f,
		bool headShadow = false,
		float distanceGain = 1f)
	{
		SpatialAudioEmitter emitter = new(gainAttackSeconds: 0.001f);
		emitter.SetTargetImmediately(new(normalizedX, 0f, distanceGain));
		float[] left = new float[512];
		float[] right = new float[512];
		emitter.Render(
			new ImpulseSource(),
			new SpatialAudioSettings(itdEnabled, maximumItd, headShadow),
			left,
			right);
		return (left, right);
	}

	private static int IndexOfPeak(float[] samples)
	{
		int peakIndex = 0;
		float peak = 0f;
		for (int index = 0; index < samples.Length; index++)
		{
			float magnitude = MathF.Abs(samples[index]);
			if (magnitude > peak)
			{
				peak = magnitude;
				peakIndex = index;
			}
		}
		return peakIndex;
	}

	private static void Report(string name, bool passed, string detail)
	{
		if (!passed)
		{
			_failures++;
		}
		Console.WriteLine($"{(passed ? "pass" : "FAIL")}  {name}");
		Console.WriteLine($"        {detail}");
	}

	private sealed class ImpulseSource : ISpatialMonoSource
	{
		private bool _fired;

		public float ReadSample(float pitchRatio)
		{
			if (_fired)
			{
				return 0f;
			}
			_fired = true;
			return 1f;
		}

		public void Reset() => _fired = false;
	}

	private sealed class SineSource(float frequency, int sampleRate) : ISpatialMonoSource
	{
		private float _phase;

		public float ReadSample(float pitchRatio)
		{
			float sample = MathF.Sin(MathF.Tau * _phase);
			_phase += frequency * pitchRatio / sampleRate;
			_phase -= MathF.Floor(_phase);
			return sample;
		}

		public void Reset() => _phase = 0f;
	}
}
