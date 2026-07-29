#nullable enable

using System.Diagnostics;
using Microsoft.Xna.Framework;
using Ariadne.Audio;

namespace Ariadne.TestBenches.AudioAnalysis;

/// <summary>
/// Renders the mod's own signal path off-line and measures the result, so the
/// questions a play test answers by ear — is it distorting, is anything clipped, is a
/// cue being cut off, can the machine keep up — are answered from the samples.
///
/// Everything downstream of a source's <see cref="IAudioBusSource.Render"/> is the
/// real code. What the bench supplies is the situation: where the sources are, how
/// loud, and how many at once.
/// </summary>
internal static class Program
{
	/// <summary>Terraria's sound slider and every Ariadne slider at maximum.</summary>
	private const float MasterVolume = 1f;
	private const float SliderVolume = 1f;

	private static readonly SpatialAudioSettings Settings = new(
		ItdEnabled: true,
		MaximumItdMilliseconds: 0.65f,
		HeadShadowEnabled: true);

	private static readonly int Rate = SpatialAudioTransformCalculator.SampleRate;
	private static int _failures;
	private static string _wavDirectory = "";

	private static int Main(string[] arguments)
	{
		_wavDirectory = arguments.Length > 0
			? arguments[0]
			: Path.Combine(AppContext.BaseDirectory, "wav");

		Console.WriteLine($"Device sample rate: {Rate} Hz ({AudioFormat.Diagnostic})");
		Console.WriteLine($"Block size: {OfflineBus.FramesPerBuffer} frames " +
			$"({OfflineBus.FramesPerBuffer * 1000f / Rate:F2} ms)");
		Console.WriteLine($"Reference loudness: {AuthoredAudioLevels.ReferenceLoudness:F1} LUFS " +
			$"(spatial {AuthoredAudioLevels.SpatialVoiceReferenceLoudness:F2} LUFS)");
		Console.WriteLine($"WAV output: {_wavDirectory}");
		Console.WriteLine();

		TruePeakMeasurementSeesAnOvershootItShould();
		WorstCaseStaysUnderTheCeiling();
		WorstCaseNeverReconstructsPastFullScale();
		BlockBoundariesAreNoRougherThanTheSignal();
		LimiterScalesASteadyToneRatherThanBendingIt();
		LimiterDoesNotPumpTheBed();
		OneShotsFinishBeforeTheyAreRetired();
		SpatialOneShotFlushesItsInterauralTail();
		ManyEnemiesHandingOverSlotsStaysContinuous();
		ManyEnemiesOnTopOfEverythingElseStillFits();
		ListeningGateOpensAndClosesWithoutAStep();
		OutputIsCenteredAndSurvivesTheSixteenBitFallback();
		AuthoredCuesSitAtOrUnderTheReference();
		TerrainBedLevelAgainstSurfaceCount();
		RenderCostFitsTheFrameBudget();
		WriteCombatCueAudition();

		Console.WriteLine();
		Console.WriteLine(_failures == 0
			? "All checks passed."
			: $"{_failures} check(s) FAILED.");
		return _failures == 0 ? 0 : 1;
	}

	// ------------------------------------------------------------------- scenes

	/// <summary>
	/// Everything the mod can sound at once, all of it at the closest range and every
	/// slider at maximum: four hostile mobs across the field, a fully enclosed terrain
	/// bed, the body beacon, and optionally a stride of footsteps and wall bumps
	/// landing on top. This is the loudest situation the bus can be put in.
	/// </summary>
	private sealed class WorstCase
	{
		private readonly OfflineBus _bus = new();
		private readonly float _footstepSeconds;
		private readonly float _bumpSeconds;
		private readonly float _burstSeconds;
		private int _footstepIndex;
		private int _bumpIndex;

		internal WorstCase(float footstepSeconds = 0f, float bumpSeconds = 0f, float burstSeconds = 0f)
		{
			_footstepSeconds = footstepSeconds;
			_bumpSeconds = bumpSeconds;
			_burstSeconds = burstSeconds;

			HostileMobBed mobs = new();
			TerrainBed bed = new();
			BodyBeacon beacon = new();
			_bus.Add(bed);
			_bus.Add(mobs);
			_bus.Add(beacon);

			float[] positions = [-1f, -0.35f, 0.35f, 1f];
			float[] heights = [-0.6f, 0.2f, -0.2f, 0.6f];
			for (int index = 0; index < 4; index++)
			{
				mobs.SetTarget(index, positions[index], heights[index], 1f);
			}
			mobs.Apply(SliderVolume, Settings);
			bed.SetEnclosed(SliderVolume, Settings);
			beacon.SetTarget(0f, 0f, SliderVolume, Settings);
		}

		internal float MinimumLimiterGain => _bus.MinimumLimiterGain;

		/// <summary>Renders the scene through the real mix chain.</summary>
		internal (float[] Left, float[] Right) Render(float seconds) => Render(seconds, throughChain: true);

		/// <summary>Renders the summed mix before the master gain and the ceiling.</summary>
		internal (float[] Left, float[] Right) RenderPreChain(float seconds) => Render(seconds, throughChain: false);

		private (float[] Left, float[] Right) Render(float seconds, bool throughChain)
		{
			int blocks = (int)(seconds * Rate) / OfflineBus.FramesPerBuffer;
			float[] left = new float[blocks * OfflineBus.FramesPerBuffer];
			float[] right = new float[left.Length];
			float[] blockLeft = new float[OfflineBus.FramesPerBuffer];
			float[] blockRight = new float[OfflineBus.FramesPerBuffer];

			for (int block = 0; block < blocks; block++)
			{
				int frame = block * OfflineBus.FramesPerBuffer;
				FireScheduledCues(frame);
				if (throughChain)
				{
					_bus.RenderBlock(blockLeft, blockRight, MasterVolume, canListen: true);
				}
				else
				{
					_bus.RenderBlockPreChain(blockLeft, blockRight);
				}
				blockLeft.CopyTo(left.AsSpan(frame));
				blockRight.CopyTo(right.AsSpan(frame));
			}

			return (left, right);
		}

		private void FireScheduledCues(int frame)
		{
			if (Crossed(frame, _footstepSeconds))
			{
				_bus.Add(new MonoOneShotVoice(
					Cues.FootstepTones[_footstepIndex++ % Cues.FootstepTones.Length],
					playbackRatio: 1f,
					volume: SliderVolume));
			}
			if (Crossed(frame, _bumpSeconds))
			{
				_bus.Add(new MonoOneShotVoice(
					Cues.BumpTones[_bumpIndex++ % Cues.BumpTones.Length],
					playbackRatio: 1f,
					volume: SliderVolume));
			}
			if (Crossed(frame, _burstSeconds))
			{
				// Several cues landing on the same tick, which is what actually drives the
				// limiter far enough down for its recovery to be worth measuring.
				foreach (float[] tone in Cues.FootstepTones.Concat(Cues.BumpTones))
				{
					_bus.Add(new MonoOneShotVoice(tone, playbackRatio: 1f, volume: SliderVolume));
				}
			}
		}

		private static bool Crossed(int frame, float intervalSeconds)
		{
			if (intervalSeconds <= 0f)
			{
				return false;
			}
			int interval = (int)(intervalSeconds * Rate);
			return frame / interval != (frame - OfflineBus.FramesPerBuffer) / interval;
		}
	}

	// --------------------------------------------------------- the instrument

	/// <summary>
	/// Checks the true-peak meter against a signal whose answer is known before it is
	/// measured, so a reassuring headroom figure below is a fact about the mix rather
	/// than about a meter that cannot see overshoot.
	///
	/// A quarter-rate sine offset by an eighth of a cycle is sampled at +/-0.7071 and
	/// never at its own crest, so the waveform running through those samples reaches
	/// full scale between every one of them.
	/// </summary>
	private static void TruePeakMeasurementSeesAnOvershootItShould()
	{
		float[] samples = new float[4_096];
		for (int index = 0; index < samples.Length; index++)
		{
			samples[index] = MathF.Sin(MathF.Tau * (index / 4f + 0.125f));
		}

		float samplePeak = Measure.Peak(samples);
		float truePeak = Measure.TruePeak(samples);
		Report(
			"True-peak meter sees an overshoot it should",
			samplePeak < 0.71f && MathF.Abs(truePeak - 1f) < 0.02f,
			$"samples reach {samplePeak:F4}, reconstruction reaches {truePeak:F4}, 1.0000 expected");
	}

	// ---------------------------------------------------------------- worst case

	private static void WorstCaseStaysUnderTheCeiling()
	{
		WorstCase scene = new(footstepSeconds: 0.35f, bumpSeconds: 0.90f);
		(float[] left, float[] right) = scene.Render(8f);
		Wav.WriteStereo(Path.Combine(_wavDirectory, "worst-case.wav"), left, right, Rate);

		WorstCase unlimited = new(footstepSeconds: 0.35f, bumpSeconds: 0.90f);
		(float[] rawLeft, float[] rawRight) = unlimited.RenderPreChain(8f);
		float preChainPeak = Measure.Peak(rawLeft, rawRight);

		float peak = Measure.Peak(left, right);
		int overFullScale = Measure.CountAtOrAbove(left, 1f) + Measure.CountAtOrAbove(right, 1f);
		int nonFinite = Measure.CountNonFinite(left) + Measure.CountNonFinite(right);

		// The limiter's ceiling is 0.97, plus a hair for the gain ramp landing a
		// fraction above it inside the block the gain is still moving through.
		bool passed = peak <= 0.98f && overFullScale == 0 && nonFinite == 0;
		Report(
			"Worst-case mix stays under the ceiling",
			passed,
			$"peak {peak:F4} ({Measure.Decibels(peak)}), {overFullScale} sample(s) at or over full scale, " +
			$"{nonFinite} non-finite");
		Console.WriteLine(
			$"        without the limiter this scene would have peaked at {preChainPeak:F3} " +
			$"({Measure.Decibels(preChainPeak)}); the limiter took off at most " +
			$"{Measure.Gain(scene.MinimumLimiterGain)}");
		Console.WriteLine($"        mix loudness {Measure.Loudness(left, right):F2} LUFS");
	}

	private static void WorstCaseNeverReconstructsPastFullScale()
	{
		// Measured over the same eight seconds the peak check runs, so the reconstruction
		// is asked about the loudest moment rather than an early one.
		WorstCase scene = new(footstepSeconds: 0.35f, bumpSeconds: 0.90f);
		(float[] left, float[] right) = scene.Render(8f);

		float samplePeak = Measure.Peak(left, right);
		float truePeak = MathF.Max(Measure.TruePeak(left), Measure.TruePeak(right));
		float headroom = truePeak > 0f ? -20f * MathF.Log10(truePeak) : float.PositiveInfinity;

		// Sanity on the measurement itself: the reconstruction can only ever sit at or
		// above the samples it passes through.
		bool measurementSane = truePeak >= samplePeak - 1e-4f;
		Report(
			"Reconstructed waveform stays under full scale",
			truePeak < 1f && measurementSane,
			$"sample peak {samplePeak:F4}, true peak {truePeak:F4} ({Measure.Decibels(truePeak)}), " +
			$"{headroom:F2} dB of headroom before a converter would clip");
	}

	private static void BlockBoundariesAreNoRougherThanTheSignal()
	{
		// The limiter decides one gain per block and walks to it in a straight line, so
		// if that gain were audible it would show as a step exactly at a block edge.
		WorstCase scene = new(footstepSeconds: 0.35f, bumpSeconds: 0.90f);
		(float[] left, _) = scene.Render(6f);
		float insideBlocks = Measure.LargestStep(left);
		float atBoundaries = Measure.LargestStepAcrossBoundaries(left, OfflineBus.FramesPerBuffer);
		Report(
			"Block boundaries are no rougher than the signal itself",
			atBoundaries <= insideBlocks,
			$"largest step at a boundary {atBoundaries:F5}, largest anywhere {insideBlocks:F5}");
	}

	// ------------------------------------------------------------------ limiter

	private static void LimiterScalesASteadyToneRatherThanBendingIt()
	{
		const float frequency = 1_000f;
		const float amplitude = 1.8f;
		OfflineBus bus = new();
		bus.Add(new SineSource(frequency, amplitude));

		int blocks = (int)(2f * Rate) / OfflineBus.FramesPerBuffer;
		float[] output = new float[blocks * OfflineBus.FramesPerBuffer];
		float[] blockLeft = new float[OfflineBus.FramesPerBuffer];
		float[] blockRight = new float[OfflineBus.FramesPerBuffer];
		for (int block = 0; block < blocks; block++)
		{
			bus.RenderBlock(blockLeft, blockRight, MasterVolume, canListen: true);
			blockLeft.CopyTo(output.AsSpan(block * OfflineBus.FramesPerBuffer));
		}

		// The second half only, so the gain has finished arriving at its steady value.
		int start = output.Length / 2;
		ReadOnlySpan<float> settled = output.AsSpan(start);
		float thd = Measure.TotalHarmonicDistortion(settled, frequency, Rate);

		// A limiter that held the peak by bending the waveform would leave a residual
		// against a plain scaled copy of the input; one that only scales it leaves the
		// same sine at a lower level. The look-ahead delays the mix by one block, which
		// the reference has to be shifted by to line the two up.
		float held = Measure.Peak(settled) / amplitude;
		float worstResidual = 0f;
		for (int index = 0; index < settled.Length; index++)
		{
			long sourceFrame = start + index - OfflineBus.FramesPerBuffer;
			float expected = amplitude * held *
				MathF.Sin(MathF.Tau * frequency * sourceFrame / Rate);
			worstResidual = MathF.Max(worstResidual, MathF.Abs(settled[index] - expected));
		}
		float residualPercent = worstResidual / MathF.Max(Measure.Peak(settled), 1e-9f) * 100f;

		Report(
			"Limiter scales a steady tone instead of bending it",
			thd < 0.01f && residualPercent < 1f,
			$"THD {thd * 100f:F4}% over harmonics 2-10, tone held at {Measure.Gain(held)}, " +
			$"worst departure from a plain scaled copy {residualPercent:F3}%");
	}

	private static void LimiterDoesNotPumpTheBed()
	{
		// The same loud scene twice, once with cues landing on it and once without. The
		// bed is identical in both, so any level difference measured in the gaps between
		// cues is the limiter ducking the bed rather than the cue's own envelope.
		const float bumpSeconds = 1f;
		const float seconds = 6f;
		WorstCase quiet = new();
		WorstCase busy = new(burstSeconds: bumpSeconds);
		(float[] withoutCues, _) = quiet.Render(seconds);
		(float[] withCues, float[] withCuesRight) = busy.Render(seconds);
		Wav.WriteStereo(Path.Combine(_wavDirectory, "bed-with-bumps.wav"), withCues, withCuesRight, Rate);

		// A window that ends just before the next cue fires, by which point the previous
		// one has long decayed, so only the bed and the limiter's recovery are in it.
		int windowSamples = (int)(0.10f * Rate);
		float worstDuck = 0f;
		int measured = 0;
		for (float mark = 2f * bumpSeconds; mark + 0.05f < seconds; mark += bumpSeconds)
		{
			int end = (int)(mark * Rate) - (int)(0.05f * Rate);
			int begin = end - windowSamples;
			if (begin < 0 || end > withCues.Length)
			{
				continue;
			}

			float busyRms = Rms(withCues.AsSpan(begin, windowSamples));
			float quietRms = Rms(withoutCues.AsSpan(begin, windowSamples));
			float duck = 20f * MathF.Log10(MathF.Max(busyRms, 1e-9f) / MathF.Max(quietRms, 1e-9f));
			worstDuck = MathF.Min(worstDuck, duck);
			measured++;
		}

		Report(
			"Bed is not ducked by cues landing on it",
			worstDuck > -1.5f,
			$"bed sits {worstDuck:F2} dB below its undisturbed level in the gaps between cues " +
			$"({measured} gap(s) measured); limiter reached {Measure.Gain(busy.MinimumLimiterGain)} " +
			$"with cues, {Measure.Gain(quiet.MinimumLimiterGain)} without");
	}

	private static float Rms(ReadOnlySpan<float> samples)
	{
		double sum = 0d;
		foreach (float sample in samples)
		{
			sum += sample * sample;
		}
		return (float)Math.Sqrt(sum / samples.Length);
	}

	// ---------------------------------------------------------------- one-shots

	private static void OneShotsFinishBeforeTheyAreRetired()
	{
		bool allPassed = true;
		List<string> details = [];

		foreach ((string name, float[] tone, float pitch) in EnumerateOneShots())
		{
			float ratio = MathF.Pow(2f, pitch);
			MonoOneShotVoice voice = new(tone, ratio, volume: 1f);
			List<float> rendered = [];
			float[] blockLeft = new float[OfflineBus.FramesPerBuffer];
			float[] blockRight = new float[OfflineBus.FramesPerBuffer];
			int guard = 0;
			while (guard++ < 10_000)
			{
				blockLeft.AsSpan().Clear();
				blockRight.AsSpan().Clear();
				bool alive = voice.Render(blockLeft, blockRight);
				rendered.AddRange(blockLeft);
				if (!alive)
				{
					break;
				}
			}

			float[] samples = [.. rendered];
			float peak = Measure.Peak(samples);
			// A cue that ended has decayed; a cue that was stopped has not. The residual
			// at the moment of retirement is what tells the two apart, because that step
			// to silence is exactly what a click is.
			float residualRatio = MathF.Abs(samples[^1]) / MathF.Max(peak, 1e-9f);
			int lastAudible = Measure.LastSampleAbove(samples, peak * 0.001f);
			int needed = (int)MathF.Ceiling(tone.Length / ratio);
			bool passed = residualRatio < 0.001f && lastAudible < samples.Length - 1;
			allPassed &= passed;
			details.Add(
				$"{name} at {pitch:+0.00;-0.00;0.00} oct: {tone.Length} source frames, {needed} needed, " +
				$"{samples.Length} rendered, last audible at {lastAudible}, " +
				$"residual at cut {Measure.Decibels(residualRatio)} of peak");
		}

		Report("Every one-shot decays to silence before it is retired", allPassed, details[0]);
		foreach (string detail in details.Skip(1))
		{
			Console.WriteLine($"        {detail}");
		}
	}

	private static IEnumerable<(string Name, float[] Tone, float Pitch)> EnumerateOneShots()
	{
		for (int index = 0; index < Cues.Footsteps.Length; index++)
		{
			yield return (Cues.Footsteps[index].Name, Cues.FootstepTones[index], 0f);
		}
		for (int index = 0; index < Cues.Bumps.Length; index++)
		{
			yield return (Cues.Bumps[index].Name, Cues.BumpTones[index], 0f);
		}
		// Pitched playback stretches or compresses the cue against a frame budget that
		// is computed from the ratio, which is where a truncation would appear.
		yield return ("footstep 1", Cues.FootstepTones[0], -0.5f);
		yield return ("footstep 1", Cues.FootstepTones[0], 0.5f);
		yield return ("bump terrain", Cues.BumpTones[0], -1f);
		yield return ("bump terrain", Cues.BumpTones[0], 1f);
	}

	private static void SpatialOneShotFlushesItsInterauralTail()
	{
		// A hard-panned cue puts the far ear behind the near one by the full interaural
		// delay, so the far ear is still sounding after the source has run out. The
		// voice has to stay on the bus long enough to let that tail out.
		// The longest delay the mixer can be configured for, which is what the tail
		// budget has to cover rather than the 0.65 ms default.
		int longestDelay = (int)MathF.Ceiling(Rate / 1_000f);
		bool allPassed = true;
		List<string> details = [];

		foreach ((string name, CombatTargetCue cue) in CombatCues())
		{
			SpatialOneShotVoice voice = new(
				cue.CreateVoice(),
				cue.BusFrameCount,
				new(1f, 0f),
				Settings,
				volume: 1f);

			List<float> farEar = [];
			float[] blockLeft = new float[OfflineBus.FramesPerBuffer];
			float[] blockRight = new float[OfflineBus.FramesPerBuffer];
			int guard = 0;
			while (guard++ < 10_000)
			{
				blockLeft.AsSpan().Clear();
				blockRight.AsSpan().Clear();
				bool alive = voice.Render(blockLeft, blockRight);
				farEar.AddRange(blockLeft);
				if (!alive)
				{
					break;
				}
			}

			float[] samples = [.. farEar];
			float peak = Measure.Peak(samples);
			float residualRatio = MathF.Abs(samples[^1]) / MathF.Max(peak, 1e-9f);
			int slack = samples.Length - cue.FrameCount - longestDelay;
			bool passed = residualRatio < 0.001f && slack >= 0;
			allPassed &= passed;
			details.Add(
				$"{name}: {samples.Length} frames rendered for a {cue.FrameCount}-frame cue with a " +
				$"{cue.BusFrameCount - cue.FrameCount}-frame tail budget against a {longestDelay}-frame " +
				$"longest delay; {slack} frames of slack, residual at cut " +
				$"{Measure.Decibels(residualRatio)} of peak");
		}

		Report("Hard-panned one-shot flushes its interaural tail", allPassed, details[0]);
		foreach (string detail in details.Skip(1))
		{
			Console.WriteLine($"        {detail}");
		}
	}

	private static (string Name, CombatTargetCue Cue)[] CombatCues() =>
	[
		("target taken", CombatTargetCue.Acquired),
		("target lost", CombatTargetCue.Lost),
	];

	// ------------------------------------------------------------- many enemies

	private static (float[] Left, float[] Right, int Handovers) RenderSwarm(int mobCount, float seconds)
	{
		OfflineBus bus = new();
		HostileMobBed mobs = new();
		bus.Add(mobs);

		MobSwarm swarm = new(mobCount);
		SlotAssigner assigner = new(4);
		MobState[] states = new MobState[mobCount];
		bool[] changed = new bool[4];
		MobState[] assigned = new MobState[4];

		int blocks = (int)(seconds * Rate) / OfflineBus.FramesPerBuffer;
		float[] left = new float[blocks * OfflineBus.FramesPerBuffer];
		float[] right = new float[left.Length];
		float[] blockLeft = new float[OfflineBus.FramesPerBuffer];
		float[] blockRight = new float[OfflineBus.FramesPerBuffer];

		// Targets are set once a game frame, the way the system sets them, rather than
		// once an audio block, so handovers land where they would really land.
		const float frameSeconds = 1f / 60f;
		float nextFrame = 0f;

		for (int block = 0; block < blocks; block++)
		{
			int frame = block * OfflineBus.FramesPerBuffer;
			float time = frame / (float)Rate;
			if (time >= nextFrame)
			{
				nextFrame += frameSeconds;
				swarm.Sample(time, states);
				assigner.Reconcile(states, changed, assigned);
				for (int slot = 0; slot < 4; slot++)
				{
					if (changed[slot])
					{
						mobs.RetireEmitter(slot);
					}

					if (assigner.IsOccupied(slot))
					{
						mobs.SetTarget(
							slot,
							assigned[slot].NormalizedX,
							assigned[slot].NormalizedY,
							assigned[slot].Proximity);
					}
					else
					{
						mobs.ClearTarget(slot);
					}
				}
				mobs.Apply(SliderVolume, Settings);
			}

			bus.RenderBlockPreChain(blockLeft, blockRight);
			blockLeft.CopyTo(left.AsSpan(frame));
			blockRight.CopyTo(right.AsSpan(frame));
		}

		return (left, right, assigner.Reassignments);
	}

	/// <summary>
	/// A crowd of hostile mobs circling the listener, so the four voices keep being
	/// handed from one mob to the next. A handover that drops the voice's level in a
	/// single sample is a step to silence in the middle of a waveform, which is what a
	/// click is; enough of them in a row is what crackling is.
	/// </summary>
	private static void ManyEnemiesHandingOverSlotsStaysContinuous()
	{
		const float seconds = 8f;
		const int settleSamples = 48_000;

		// Four mobs cannot displace one another, so once the slots are filled this run
		// has no handovers at all. Whatever it does to the signal is the signal, and it
		// sets the bar a step has to beat to be something other than the waveform.
		(float[] calmLeft, _, _) = RenderSwarm(4, seconds);
		float naturalSlope = Measure.LargestStep(calmLeft.AsSpan(settleSamples));
		float clickThreshold = MathF.Max(naturalSlope * 4f, 1e-6f);

		int totalClicks = 0;
		int totalHandovers = 0;
		List<string> rows = [];
		foreach (int mobCount in (int[])[8, 16, 24, 48])
		{
			(float[] left, float[] right, int handovers) = RenderSwarm(mobCount, seconds);
			float largest = Measure.LargestStep(left.AsSpan(settleSamples));
			int clicks = CountStepsAbove(left.AsSpan(settleSamples), clickThreshold);
			totalClicks += clicks;
			totalHandovers += handovers;
			rows.Add(
				$"{mobCount,3} mobs: {handovers,3} handover(s), {clicks,3} discontinuity(ies), " +
				$"largest step {largest:F5} ({largest / MathF.Max(naturalSlope, 1e-9f):F2}x the signal)");
			if (mobCount == 24)
			{
				Wav.WriteStereo(Path.Combine(_wavDirectory, "swarm-24.wav"), left, right, Rate);
			}
		}

		Report(
			"Handing a voice to another mob does not step the waveform",
			totalClicks == 0,
			$"{totalClicks} discontinuity(ies) past {clickThreshold:F5} across {totalHandovers} handover(s); " +
			$"the threshold is four times the {naturalSlope:F5} the waveform itself reaches in one sample");
		foreach (string row in rows)
		{
			Console.WriteLine($"        {row}");
		}
	}

	/// <summary>
	/// The listening gate closing and opening under a running bed. It is a ramp rather
	/// than a switch precisely so that opening a menu mid-tone is not a click, which is
	/// the claim this puts a number on.
	/// </summary>
	private static void ListeningGateOpensAndClosesWithoutAStep()
	{
		OfflineBus bus = new();
		HostileMobBed mobs = new();
		TerrainBed bed = new();
		bus.Add(bed);
		bus.Add(mobs);
		bed.SetEnclosed(SliderVolume, Settings);
		mobs.SetTarget(0, 0.5f, 0f, 1f);
		mobs.Apply(SliderVolume, Settings);

		int blocks = (int)(4f * Rate) / OfflineBus.FramesPerBuffer;
		float[] output = new float[blocks * OfflineBus.FramesPerBuffer];
		float[] blockLeft = new float[OfflineBus.FramesPerBuffer];
		float[] blockRight = new float[OfflineBus.FramesPerBuffer];
		for (int block = 0; block < blocks; block++)
		{
			int frame = block * OfflineBus.FramesPerBuffer;
			float time = frame / (float)Rate;
			// Shut twice, so both edges of the ramp are exercised under a live signal.
			bool canListen = !(time is > 1f and < 1.5f or > 2.5f and < 3f);
			bus.RenderBlock(blockLeft, blockRight, MasterVolume, canListen);
			blockLeft.CopyTo(output.AsSpan(frame));
		}

		Wav.WriteStereo(Path.Combine(_wavDirectory, "gate-transitions.wav"), output, output, Rate);

		// The steady stretch before the first transition is what the signal does on its
		// own; the gate must not make it rougher than that.
		float steady = Measure.LargestStep(output.AsSpan(Rate / 2, Rate / 4));
		float overall = Measure.LargestStep(output);
		Report(
			"Listening gate opens and closes without a step",
			overall <= steady * 1.5f,
			$"largest step while steady {steady:F5}, largest across the whole run including four " +
			$"gate edges {overall:F5}");
	}

	/// <summary>
	/// Two things that quietly cost quality rather than announcing themselves: a
	/// standing offset, which spends headroom and thumps when it is switched away, and
	/// the sixteen-bit encode the bus falls back to when the float path is unavailable.
	/// </summary>
	private static void OutputIsCenteredAndSurvivesTheSixteenBitFallback()
	{
		WorstCase scene = new(footstepSeconds: 0.35f, bumpSeconds: 0.90f);
		(float[] left, float[] right) = scene.Render(8f);

		float leftOffset = Mean(left);
		float rightOffset = Mean(right);
		float worstOffset = MathF.Max(MathF.Abs(leftOffset), MathF.Abs(rightOffset));

		// Which source it comes from, so the figure above is a lead rather than a
		// number. Each is rendered alone, without the chain, as it is authored.
		List<string> contributions = [];
		foreach ((string name, float[] cueLeft, float[] _, float __) in EnumerateCuesInIsolation())
		{
			contributions.Add($"{name,-16} {Mean(cueLeft):E2}");
		}

		// What the bus does when SubmitFloatBufferEXT is not available on this build.
		float worstError = 0f;
		int clamped = 0;
		foreach (float sample in left)
		{
			if (sample is < -1f or > 1f)
			{
				clamped++;
			}
			short encoded = (short)MathF.Round(Math.Clamp(sample, -1f, 1f) * short.MaxValue);
			worstError = MathF.Max(worstError, MathF.Abs(encoded / (float)short.MaxValue - sample));
		}

		// Half a step of a sixteen-bit word is the most rounding can cost; anything more
		// would mean the clamp had engaged and taken a real part of the signal.
		float quantizationStep = 1f / short.MaxValue;
		// One percent of full scale, which is the usual point past which an offset is
		// worth removing: below it the headroom it spends is immaterial and the step it
		// leaves when the mix is switched away is under the noise. A decaying tone is
		// asymmetric by construction — its first half-cycle is louder than the one
		// after it — so the percussive cues carry a little offset by design, and asking
		// for none of it would mean filtering every cue for no audible return.
		const float offsetCeiling = 0.01f;
		Report(
			"Output is centred and survives the sixteen-bit fallback",
			worstOffset < offsetCeiling && clamped == 0 && worstError <= quantizationStep,
			$"DC offset {leftOffset:E2} left, {rightOffset:E2} right (ceiling {offsetCeiling:E2}); " +
			$"{clamped} sample(s) would be clamped by the fallback, worst rounding error " +
			$"{worstError / quantizationStep:F2} of a step");
		foreach (string contribution in contributions)
		{
			Console.WriteLine($"        offset from {contribution}");
		}
	}

	private static float Mean(ReadOnlySpan<float> samples)
	{
		double sum = 0d;
		foreach (float sample in samples)
		{
			sum += sample;
		}
		return (float)(sum / samples.Length);
	}

	private static int CountStepsAbove(ReadOnlySpan<float> samples, float threshold)
	{
		int count = 0;
		for (int index = 1; index < samples.Length; index++)
		{
			if (MathF.Abs(samples[index] - samples[index - 1]) > threshold)
			{
				count++;
			}
		}
		return count;
	}

	/// <summary>
	/// The crowd on top of everything else, which is what the player would actually be
	/// standing in: a swarm, an enclosed terrain bed, the beacon, and a stride landing
	/// through it. Checked for the rails rather than for continuity.
	/// </summary>
	private static void ManyEnemiesOnTopOfEverythingElseStillFits()
	{
		OfflineBus bus = new();
		HostileMobBed mobs = new();
		TerrainBed bed = new();
		BodyBeacon beacon = new();
		bus.Add(bed);
		bus.Add(mobs);
		bus.Add(beacon);
		bed.SetEnclosed(SliderVolume, Settings);
		beacon.SetTarget(0f, 0f, SliderVolume, Settings);

		MobSwarm swarm = new(24);
		SlotAssigner assigner = new(4);
		MobState[] states = new MobState[24];
		bool[] changed = new bool[4];
		MobState[] assigned = new MobState[4];

		const float seconds = 8f;
		int blocks = (int)(seconds * Rate) / OfflineBus.FramesPerBuffer;
		float[] left = new float[blocks * OfflineBus.FramesPerBuffer];
		float[] right = new float[left.Length];
		float[] blockLeft = new float[OfflineBus.FramesPerBuffer];
		float[] blockRight = new float[OfflineBus.FramesPerBuffer];
		float nextFrame = 0f;
		int footstepInterval = (int)(0.35f * Rate);
		int bumpInterval = (int)(0.90f * Rate);
		int footstepIndex = 0;
		int bumpIndex = 0;

		for (int block = 0; block < blocks; block++)
		{
			int frame = block * OfflineBus.FramesPerBuffer;
			float time = frame / (float)Rate;
			if (time >= nextFrame)
			{
				nextFrame += 1f / 60f;
				swarm.Sample(time, states);
				assigner.Reconcile(states, changed, assigned);
				for (int slot = 0; slot < 4; slot++)
				{
					if (changed[slot])
					{
						mobs.RetireEmitter(slot);
					}
					if (assigner.IsOccupied(slot))
					{
						mobs.SetTarget(
							slot,
							assigned[slot].NormalizedX,
							assigned[slot].NormalizedY,
							assigned[slot].Proximity);
					}
					else
					{
						mobs.ClearTarget(slot);
					}
				}
				mobs.Apply(SliderVolume, Settings);
			}

			if (frame / footstepInterval != (frame - OfflineBus.FramesPerBuffer) / footstepInterval)
			{
				bus.Add(new MonoOneShotVoice(
					Cues.FootstepTones[footstepIndex++ % Cues.FootstepTones.Length], 1f, SliderVolume));
			}
			if (frame / bumpInterval != (frame - OfflineBus.FramesPerBuffer) / bumpInterval)
			{
				bus.Add(new MonoOneShotVoice(
					Cues.BumpTones[bumpIndex++ % Cues.BumpTones.Length], 1f, SliderVolume));
			}

			bus.RenderBlock(blockLeft, blockRight, MasterVolume, canListen: true);
			blockLeft.CopyTo(left.AsSpan(frame));
			blockRight.CopyTo(right.AsSpan(frame));
		}

		Wav.WriteStereo(Path.Combine(_wavDirectory, "swarm-in-context.wav"), left, right, Rate);

		float peak = Measure.Peak(left, right);
		int overFullScale = Measure.CountAtOrAbove(left, 1f) + Measure.CountAtOrAbove(right, 1f);
		int nonFinite = Measure.CountNonFinite(left) + Measure.CountNonFinite(right);
		float truePeak = MathF.Max(Measure.TruePeak(left), Measure.TruePeak(right));

		Report(
			"A crowd on top of everything else stays under the rails",
			peak <= 0.98f && truePeak < 1f && overFullScale == 0 && nonFinite == 0,
			$"peak {peak:F4}, true peak {truePeak:F4} ({Measure.Decibels(truePeak)}), " +
			$"{overFullScale} at full scale, {nonFinite} non-finite, " +
			$"limiter reached {Measure.Gain(bus.MinimumLimiterGain)}");
	}

	// ------------------------------------------------------------------- levels

	private static void AuthoredCuesSitAtOrUnderTheReference()
	{
		float reference = AuthoredAudioLevels.ReferenceLoudness;
		// A decibel is roughly the smallest level difference a listener reliably hears,
		// so cues inside this band are matched as far as the claim can be heard. Asking
		// for less is asking the arithmetic to be exact rather than the result to be
		// even, and a bed built from independent noise voices will never be exact.
		const float tolerance = 1f;
		bool allPassed = true;
		List<string> lines = [];

		foreach ((string name, float[] left, float[] right, float ceiling) in EnumerateCuesInIsolation())
		{
			float loudness = Measure.Loudness(left, right);
			float peak = Measure.Peak(left, right);
			bool tooLoud = loudness > reference + tolerance;
			// Falling short is allowed only when the peak ceiling is what stopped it;
			// anything else quiet is quiet for no reason the design accounts for.
			bool quietWithoutReason = loudness < reference - tolerance && peak < ceiling - 0.02f;
			bool passed = !tooLoud && !quietWithoutReason;
			allPassed &= passed;
			lines.Add(
				$"{(passed ? "  " : "!!")} {name,-16} {loudness,7:F2} LUFS   peak {peak:F3}" +
				(tooLoud ? "   OVER REFERENCE" : quietWithoutReason ? "   QUIET, NOT PEAK-LIMITED" : ""));
		}

		Report(
			"Every cue lands on the reference or is held under it by its peak ceiling",
			allPassed,
			$"reference {reference:F1} LUFS +/- {tolerance:F1} dB, measured on each cue alone at a full slider");
		foreach (string line in lines)
		{
			Console.WriteLine($"        {line}");
		}
	}

	private static IEnumerable<(string Name, float[] Left, float[] Right, float Ceiling)> EnumerateCuesInIsolation()
	{
		foreach ((string name, float[] tone) in
			Cues.Footsteps.Select((entry, index) => (entry.Name, Cues.FootstepTones[index]))
			.Concat(Cues.Bumps.Select((entry, index) => (entry.Name, Cues.BumpTones[index]))))
		{
			OfflineBus bus = new();
			bus.Add(new MonoOneShotVoice(tone, 1f, SliderVolume));
			(float[] left, float[] right) = RenderPreChain(bus, 1.5f);
			yield return (name, left, right, AuthoredAudioLevels.NormalizedOneShotPeak);
		}

		{
			OfflineBus bus = new();
			HostileMobBed mobs = new();
			bus.Add(mobs);
			mobs.SetTarget(0, 0f, 0f, 1f);
			mobs.Apply(SliderVolume, Settings);
			(float[] left, float[] right) = RenderPreChain(bus, 2.5f);
			yield return ("mob tone", left, right, 1f);
		}

		{
			OfflineBus bus = new();
			BodyBeacon beacon = new();
			bus.Add(beacon);
			beacon.SetTarget(0f, 0f, SliderVolume, Settings);
			(float[] left, float[] right) = RenderPreChain(bus, 2.5f);
			yield return ("body beacon", left, right, 1f);
		}

		// The combat cues reach the listener through the spatializer, so they are
		// measured through a centred one-shot rather than as a bare buffer.
		foreach ((string name, CombatTargetCue cue) in CombatCues())
		{
			OfflineBus bus = new();
			bus.Add(new SpatialOneShotVoice(
				cue.CreateVoice(),
				cue.BusFrameCount,
				Vector2.Zero,
				Settings,
				SliderVolume));
			(float[] left, float[] right) = RenderPreChain(bus, 1.5f);
			yield return (name, left, right, AuthoredAudioLevels.NormalizedSpatialVoicePeak);
		}

		{
			// Each bed voice is authored three decibels under the reference so that a
			// corridor — a side and the floor answering together — arrives on it.
			OfflineBus bus = new();
			TerrainBed bed = new();
			bus.Add(bed);
			bed.SetActiveSurfaces(2, SliderVolume, Settings);
			(float[] left, float[] right) = RenderPreChain(bus, 2.5f);
			yield return ("terrain bed x2", left, right, 1f);
		}
	}

	/// <summary>
	/// Reported rather than asserted. The bed's per-voice offset is authored for two
	/// surfaces answering at once; how far the level walks as more of them answer is a
	/// design decision, and this is the number that decision should be made against.
	/// </summary>
	private static void TerrainBedLevelAgainstSurfaceCount()
	{
		Console.WriteLine("      terrain bed level against the number of answering surfaces");
		for (int surfaces = 1; surfaces <= 4; surfaces++)
		{
			OfflineBus bus = new();
			TerrainBed bed = new();
			bus.Add(bed);
			bed.SetActiveSurfaces(surfaces, SliderVolume, Settings);
			(float[] left, float[] right) = RenderPreChain(bus, 2.5f);
			float loudness = Measure.Loudness(left, right);
			float offset = loudness - AuthoredAudioLevels.ReferenceLoudness;
			Console.WriteLine(
				$"        {surfaces} surface(s): {loudness,7:F2} LUFS  ({offset:+0.00;-0.00;0.00} dB " +
				$"against the reference)");
		}
	}

	private static (float[] Left, float[] Right) RenderPreChain(OfflineBus bus, float seconds)
	{
		int blocks = (int)(seconds * Rate) / OfflineBus.FramesPerBuffer;
		float[] left = new float[blocks * OfflineBus.FramesPerBuffer];
		float[] right = new float[left.Length];
		float[] blockLeft = new float[OfflineBus.FramesPerBuffer];
		float[] blockRight = new float[OfflineBus.FramesPerBuffer];
		for (int block = 0; block < blocks; block++)
		{
			bus.RenderBlockPreChain(blockLeft, blockRight);
			blockLeft.CopyTo(left.AsSpan(block * OfflineBus.FramesPerBuffer));
			blockRight.CopyTo(right.AsSpan(block * OfflineBus.FramesPerBuffer));
		}
		return (left, right);
	}

	// --------------------------------------------------------------------- cost

	private static void RenderCostFitsTheFrameBudget()
	{
		// The pump runs on the game thread, so the mix has to cost a small fraction of
		// a frame. Anything approaching the frame itself is what an underrun sounds
		// like: the voice running dry and the queue growing to cover it.
		new WorstCase(0.35f, 0.90f).Render(1f);

		Stopwatch stopwatch = Stopwatch.StartNew();
		new WorstCase(0.35f, 0.90f).Render(5f);
		stopwatch.Stop();

		double costPerSecond = stopwatch.Elapsed.TotalMilliseconds / 5d;
		double costPerFrame = costPerSecond / 60d;
		Report(
			"Rendering the worst case fits inside a frame",
			costPerFrame < 1.5d,
			$"{costPerSecond:F1} ms of CPU per second of audio, {costPerFrame:F3} ms per 60 Hz frame " +
			$"({costPerSecond / 10d:F2}% of real time)");
	}

	/// <summary>
	/// Both combat cues in a row, centred, so the pair can be heard as a pair without
	/// having to find an enemy and lose it. Not a check — just the file.
	/// </summary>
	private static void WriteCombatCueAudition()
	{
		OfflineBus bus = new();
		int blocks = (int)(1.6f * Rate) / OfflineBus.FramesPerBuffer;
		float[] left = new float[blocks * OfflineBus.FramesPerBuffer];
		float[] right = new float[left.Length];
		float[] blockLeft = new float[OfflineBus.FramesPerBuffer];
		float[] blockRight = new float[OfflineBus.FramesPerBuffer];
		int lostAt = (int)(0.6f * Rate);

		bus.Add(new SpatialOneShotVoice(
			CombatTargetCue.Acquired.CreateVoice(),
			CombatTargetCue.Acquired.BusFrameCount,
			Vector2.Zero,
			Settings,
			SliderVolume));

		for (int block = 0; block < blocks; block++)
		{
			int frame = block * OfflineBus.FramesPerBuffer;
			if (frame / lostAt != (frame - OfflineBus.FramesPerBuffer) / lostAt)
			{
				bus.Add(new SpatialOneShotVoice(
					CombatTargetCue.Lost.CreateVoice(),
					CombatTargetCue.Lost.BusFrameCount,
					Vector2.Zero,
					Settings,
					SliderVolume));
			}

			bus.RenderBlock(blockLeft, blockRight, MasterVolume, canListen: true);
			blockLeft.CopyTo(left.AsSpan(frame));
			blockRight.CopyTo(right.AsSpan(frame));
		}

		string path = Path.Combine(_wavDirectory, "combat-target-cues.wav");
		Wav.WriteStereo(path, left, right, Rate);
		Console.WriteLine($"      wrote {Path.GetFileName(path)}: target taken, then target lost");
	}

	// ------------------------------------------------------------------ reports

	private static void Report(string name, bool passed, string detail)
	{
		if (!passed)
		{
			_failures++;
		}
		Console.WriteLine($"{(passed ? "pass" : "FAIL")}  {name}");
		Console.WriteLine($"        {detail}");
	}

	private sealed class SineSource(float frequency, float amplitude) : IAudioBusSource
	{
		private long _frame;

		public bool Render(Span<float> left, Span<float> right)
		{
			for (int index = 0; index < left.Length; index++)
			{
				float sample = amplitude * MathF.Sin(
					MathF.Tau * frequency * (_frame + index) / SpatialAudioTransformCalculator.SampleRate);
				left[index] += sample;
				right[index] += sample;
			}
			_frame += left.Length;
			return true;
		}
	}

}
