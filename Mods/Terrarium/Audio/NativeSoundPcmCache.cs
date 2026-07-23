#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;

namespace Terrarium.Audio;

internal sealed class NativeSoundPcmCache : IDisposable
{
	private const int MaximumCachedSamples = 16 * 1024 * 1024;
	private const string TerrariaSoundPrefix = "Terraria/Sounds/";

	private readonly Mod _owner;
	private readonly string[] _contentRoots;
	private readonly Dictionary<string, NativePcmClip> _clips =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly HashSet<string> _pending =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly HashSet<string> _failed =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly ConcurrentQueue<LoadResult> _completed = new();
	private readonly CancellationTokenSource _cancellation = new();

	private int _cachedSamples;
	private bool _disposed;

	internal NativeSoundPcmCache(Mod owner)
	{
		_owner = owner;
		_contentRoots = ResolveContentRoots();
	}

	internal bool TryGetOrQueue(string soundPath, out NativePcmClip? clip)
	{
		DrainCompletedLoads();
		if (_clips.TryGetValue(soundPath, out clip))
		{
			return true;
		}

		Queue(soundPath);
		clip = null;
		return false;
	}

	internal void Queue(string soundPath)
	{
		if (_disposed ||
			!TryGetRelativeXnbPath(soundPath, out string? relativePath) ||
			_clips.ContainsKey(soundPath) ||
			_pending.Contains(soundPath) ||
			_failed.Contains(soundPath))
		{
			return;
		}

		_pending.Add(soundPath);
		string[] roots = _contentRoots;
		CancellationToken cancellationToken = _cancellation.Token;
		_ = Task.Run(
			() => LoadOnWorker(soundPath, relativePath, roots, cancellationToken),
			cancellationToken);
	}

	internal void Update() => DrainCompletedLoads();

	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		_disposed = true;
		_cancellation.Cancel();
		_cancellation.Dispose();
		_clips.Clear();
		_pending.Clear();
		_failed.Clear();
		while (_completed.TryDequeue(out _))
		{
		}
		_cachedSamples = 0;
	}

	private void LoadOnWorker(
		string soundPath,
		string relativePath,
		string[] roots,
		CancellationToken cancellationToken)
	{
		try
		{
			cancellationToken.ThrowIfCancellationRequested();
			string? filePath = FindAssetPath(relativePath, roots);
			if (filePath is null)
			{
				cancellationToken.ThrowIfCancellationRequested();
				_completed.Enqueue(new(
					soundPath,
					Clip: null,
					$"The installed asset '{relativePath}' was not found."));
				return;
			}

			using FileStream stream = new(
				filePath,
				FileMode.Open,
				FileAccess.Read,
				FileShare.Read);
			NativePcmClip clip = XnbSoundEffectPcmDecoder.Decode(stream, soundPath);
			cancellationToken.ThrowIfCancellationRequested();
			_completed.Enqueue(new(soundPath, clip, Error: null));
		}
		catch (OperationCanceledException)
		{
			// Mod unload intentionally abandons pending cache work.
		}
		catch (Exception exception)
		{
			if (cancellationToken.IsCancellationRequested)
			{
				return;
			}
			_completed.Enqueue(new(
				soundPath,
				Clip: null,
				exception.GetBaseException().Message));
		}
	}

	private void DrainCompletedLoads()
	{
		if (_disposed)
		{
			return;
		}

		while (_completed.TryDequeue(out LoadResult result))
		{
			_pending.Remove(result.SoundPath);
			if (result.Clip is not null &&
				_cachedSamples + result.Clip.Samples.Length <= MaximumCachedSamples)
			{
				_clips[result.SoundPath] = result.Clip;
				_cachedSamples += result.Clip.Samples.Length;
				continue;
			}

			_failed.Add(result.SoundPath);
			string error = result.Error ??
				"the native cursor-sound cache reached its 64 MB session limit";
			_owner.Logger.Warn(
				$"Native cursor sound '{result.SoundPath}' will use Terraria's normal " +
				$"playback without ITD because {error}");
		}
	}

	private static string[] ResolveContentRoots()
	{
		List<string> roots = [];
		AddIfDirectory(Path.Combine(AppContext.BaseDirectory, "Content"));
		if (Main.instance?.Content?.RootDirectory is string vanillaRoot)
		{
			AddIfDirectory(vanillaRoot);
		}
		return [.. roots];

		void AddIfDirectory(string candidate)
		{
			try
			{
				string fullPath = Path.GetFullPath(candidate);
				if (Directory.Exists(fullPath) &&
					!roots.Exists(root =>
						root.Equals(fullPath, StringComparison.OrdinalIgnoreCase)))
				{
					roots.Add(fullPath);
				}
			}
			catch
			{
				// An invalid optional root is simply unavailable.
			}
		}
	}

	private static bool TryGetRelativeXnbPath(
		string soundPath,
		out string relativePath)
	{
		if (!soundPath.StartsWith(TerrariaSoundPrefix, StringComparison.OrdinalIgnoreCase))
		{
			relativePath = string.Empty;
			return false;
		}

		string relativeSoundPath = soundPath["Terraria/".Length..]
			.Replace('/', Path.DirectorySeparatorChar);
		if (relativeSoundPath.Contains("..", StringComparison.Ordinal))
		{
			relativePath = string.Empty;
			return false;
		}

		relativePath = relativeSoundPath + ".xnb";
		return true;
	}

	private static string? FindAssetPath(string relativePath, string[] roots)
	{
		StringComparison comparison = OperatingSystem.IsWindows()
			? StringComparison.OrdinalIgnoreCase
			: StringComparison.Ordinal;
		foreach (string root in roots)
		{
			string candidate = Path.GetFullPath(Path.Combine(root, relativePath));
			string rootPrefix = root.EndsWith(Path.DirectorySeparatorChar)
				? root
				: root + Path.DirectorySeparatorChar;
			if (candidate.StartsWith(rootPrefix, comparison) && File.Exists(candidate))
			{
				return candidate;
			}
		}
		return null;
	}

	private readonly record struct LoadResult(
		string SoundPath,
		NativePcmClip? Clip,
		string? Error);
}

internal sealed class NativePcmClip
{
	internal NativePcmClip(float[] samples)
	{
		Samples = samples;
	}

	internal float[] Samples { get; }
}

internal static class XnbSoundEffectPcmDecoder
{
	private const int TargetSampleRate = SpatialAudioTransformCalculator.SampleRate;
	private const int MaximumTypeReaders = 32;
	private const int MaximumReaderNameBytes = 1_024;
	private const int MaximumWaveFormatBytes = 256;
	private const int MaximumPcmBytes = 64 * 1024 * 1024;
	private const int PcmFormatTag = 1;
	private const int PcmBitsPerSample = 16;

	internal static NativePcmClip Decode(Stream stream, string assetName)
	{
		using BinaryReader reader = new(stream, Encoding.UTF8, leaveOpen: true);
		Require(reader.ReadByte() == (byte)'X', assetName, "invalid XNB signature");
		Require(reader.ReadByte() == (byte)'N', assetName, "invalid XNB signature");
		Require(reader.ReadByte() == (byte)'B', assetName, "invalid XNB signature");
		_ = reader.ReadByte();
		byte version = reader.ReadByte();
		byte flags = reader.ReadByte();
		Require(version is 4 or 5, assetName, $"unsupported XNB version {version}");
		Require(
			(flags & 0xC0) == 0,
			assetName,
			"compressed XNB sound data is not supported");
		int declaredFileSize = reader.ReadInt32();
		Require(
			declaredFileSize >= 10 && declaredFileSize <= stream.Length,
			assetName,
			"invalid XNB file length");

		int readerCount = Read7BitEncodedInt(reader, assetName);
		Require(
			readerCount is > 0 and <= MaximumTypeReaders,
			assetName,
			"invalid XNB type-reader count");
		bool[] soundEffectReaders = new bool[readerCount];
		for (int index = 0; index < readerCount; index++)
		{
			string readerName = ReadLimitedString(reader, assetName);
			soundEffectReaders[index] =
				readerName.Contains("SoundEffectReader", StringComparison.Ordinal);
			_ = reader.ReadInt32();
		}

		int sharedResourceCount = Read7BitEncodedInt(reader, assetName);
		Require(sharedResourceCount == 0, assetName, "shared XNB resources are not supported");
		int rootReaderIndex = Read7BitEncodedInt(reader, assetName);
		Require(
			rootReaderIndex is > 0 && rootReaderIndex <= readerCount,
			assetName,
			"invalid XNB root reader");
		Require(
			soundEffectReaders[rootReaderIndex - 1],
			assetName,
			"XNB root is not a sound effect");

		int waveFormatLength = reader.ReadInt32();
		Require(
			waveFormatLength is >= 16 and <= MaximumWaveFormatBytes,
			assetName,
			"invalid WAVEFORMATEX length");
		byte[] waveFormat = ReadExact(reader, waveFormatLength, assetName);
		int formatTag = ReadUInt16(waveFormat, 0);
		int channels = ReadUInt16(waveFormat, 2);
		int sampleRate = checked((int)ReadUInt32(waveFormat, 4));
		int blockAlign = ReadUInt16(waveFormat, 12);
		int bitsPerSample = ReadUInt16(waveFormat, 14);
		Require(formatTag == PcmFormatTag, assetName, $"unsupported PCM format tag {formatTag}");
		Require(channels is 1 or 2, assetName, $"unsupported channel count {channels}");
		Require(
			sampleRate is >= 8_000 and <= 192_000,
			assetName,
			$"unsupported sample rate {sampleRate}");
		Require(
			bitsPerSample == PcmBitsPerSample,
			assetName,
			$"unsupported {bitsPerSample}-bit PCM");
		Require(
			blockAlign == channels * sizeof(short),
			assetName,
			"invalid PCM block alignment");

		int pcmLength = reader.ReadInt32();
		Require(
			pcmLength is > 0 and <= MaximumPcmBytes &&
			pcmLength % blockAlign == 0,
			assetName,
			"invalid PCM payload length");
		byte[] pcm = ReadExact(reader, pcmLength, assetName);
		_ = reader.ReadInt32();
		_ = reader.ReadInt32();
		_ = reader.ReadInt32();

		float[] mono = DecodeMonoPcm16(pcm, channels, blockAlign);
		if (sampleRate != TargetSampleRate)
		{
			mono = ResampleLinear(mono, sampleRate, TargetSampleRate);
		}
		return new(mono);
	}

	private static float[] DecodeMonoPcm16(
		byte[] pcm,
		int channels,
		int blockAlign)
	{
		int frameCount = pcm.Length / blockAlign;
		float[] mono = new float[frameCount];
		for (int frame = 0; frame < frameCount; frame++)
		{
			int offset = frame * blockAlign;
			short left = (short)(pcm[offset] | pcm[offset + 1] << 8);
			if (channels == 1)
			{
				mono[frame] = left / 32_768f;
				continue;
			}

			short right = (short)(pcm[offset + 2] | pcm[offset + 3] << 8);
			mono[frame] = (left + right) / 65_536f;
		}
		return mono;
	}

	private static float[] ResampleLinear(
		float[] source,
		int sourceRate,
		int targetRate)
	{
		int targetLength = Math.Max(
			1,
			checked((int)Math.Round(source.Length * (double)targetRate / sourceRate)));
		float[] target = new float[targetLength];
		double sourceStep = sourceRate / (double)targetRate;
		for (int index = 0; index < target.Length; index++)
		{
			double sourcePosition = index * sourceStep;
			int lower = Math.Min((int)sourcePosition, source.Length - 1);
			int upper = Math.Min(lower + 1, source.Length - 1);
			float fraction = (float)(sourcePosition - lower);
			target[index] = MathHelper.Lerp(source[lower], source[upper], fraction);
		}
		return target;
	}

	private static string ReadLimitedString(BinaryReader reader, string assetName)
	{
		int byteCount = Read7BitEncodedInt(reader, assetName);
		Require(
			byteCount is >= 0 and <= MaximumReaderNameBytes,
			assetName,
			"invalid XNB reader-name length");
		return Encoding.UTF8.GetString(ReadExact(reader, byteCount, assetName));
	}

	private static int Read7BitEncodedInt(BinaryReader reader, string assetName)
	{
		uint result = 0;
		for (int shift = 0; shift < 35; shift += 7)
		{
			byte value = reader.ReadByte();
			result |= (uint)(value & 0x7F) << shift;
			if ((value & 0x80) == 0)
			{
				Require(result <= int.MaxValue, assetName, "XNB integer exceeds Int32");
				return (int)result;
			}
		}

		throw new InvalidDataException($"{assetName}: malformed XNB integer.");
	}

	private static byte[] ReadExact(
		BinaryReader reader,
		int byteCount,
		string assetName)
	{
		byte[] bytes = reader.ReadBytes(byteCount);
		Require(bytes.Length == byteCount, assetName, "unexpected end of XNB data");
		return bytes;
	}

	private static ushort ReadUInt16(byte[] bytes, int offset) =>
		(ushort)(bytes[offset] | bytes[offset + 1] << 8);

	private static uint ReadUInt32(byte[] bytes, int offset) =>
		(uint)(bytes[offset] |
			bytes[offset + 1] << 8 |
			bytes[offset + 2] << 16 |
			bytes[offset + 3] << 24);

	private static void Require(bool condition, string assetName, string message)
	{
		if (!condition)
		{
			throw new InvalidDataException($"{assetName}: {message}.");
		}
	}
}
