#nullable enable

using System;

namespace Ariadne.Audio;

internal static class PcmWaveDecoder
{
	private const uint RiffId = 0x4646_4952;
	private const uint WaveId = 0x4556_4157;
	private const uint FormatId = 0x2074_6D66;
	private const uint DataId = 0x6174_6164;
	private const ushort PcmFormat = 1;

	internal static float[] DecodeMono16(byte[] bytes, int expectedSampleRate)
	{
		ArgumentNullException.ThrowIfNull(bytes);
		if (bytes.Length < 44 || ReadUInt32(bytes, 0) != RiffId || ReadUInt32(bytes, 8) != WaveId)
		{
			throw new InvalidOperationException("The file is not a RIFF/WAVE stream.");
		}

		long riffEnd = 8L + ReadUInt32(bytes, 4);
		if (riffEnd < 12 || riffEnd > bytes.Length)
		{
			throw new InvalidOperationException("The RIFF length is invalid.");
		}

		bool foundFormat = false;
		int dataOffset = -1;
		int dataLength = 0;
		int offset = 12;
		while (offset + 8L <= riffEnd)
		{
			uint chunkId = ReadUInt32(bytes, offset);
			uint chunkLength = ReadUInt32(bytes, offset + 4);
			long contentOffset = offset + 8L;
			long contentEnd = contentOffset + chunkLength;
			if (contentEnd > riffEnd || contentEnd > bytes.Length)
			{
				throw new InvalidOperationException("A WAVE chunk extends beyond the RIFF data.");
			}

			if (chunkId == FormatId)
			{
				if (chunkLength < 16 ||
					ReadUInt16(bytes, (int)contentOffset) != PcmFormat ||
					ReadUInt16(bytes, (int)contentOffset + 2) != 1 ||
					ReadUInt32(bytes, (int)contentOffset + 4) != expectedSampleRate ||
					ReadUInt16(bytes, (int)contentOffset + 12) != sizeof(short) ||
					ReadUInt16(bytes, (int)contentOffset + 14) != 16 ||
					ReadUInt32(bytes, (int)contentOffset + 8) != expectedSampleRate * sizeof(short))
				{
					throw new InvalidOperationException($"The pulse must be mono, 16-bit PCM at {expectedSampleRate} Hz.");
				}
				foundFormat = true;
			}
			else if (chunkId == DataId && dataOffset < 0)
			{
				if (chunkLength == 0 || chunkLength > int.MaxValue || (chunkLength & 1) != 0)
				{
					throw new InvalidOperationException("The PCM data chunk has an invalid length.");
				}
				dataOffset = (int)contentOffset;
				dataLength = (int)chunkLength;
			}

			long nextOffset = contentEnd + (chunkLength & 1);
			if (nextOffset > int.MaxValue)
			{
				throw new InvalidOperationException("The WAVE file is too large.");
			}
			offset = (int)nextOffset;
		}

		if (!foundFormat || dataOffset < 0)
		{
			throw new InvalidOperationException("The WAVE file is missing its PCM format or data chunk.");
		}

		int sampleCount = dataLength / sizeof(short);
		if (sampleCount < expectedSampleRate / 10 || sampleCount > expectedSampleRate / 5)
		{
			throw new InvalidOperationException("The hostile-mob pulse duration must be between 100 and 200 milliseconds.");
		}

		float[] samples = new float[sampleCount];
		for (int index = 0; index < sampleCount; index++)
		{
			short sample = unchecked((short)ReadUInt16(bytes, dataOffset + index * sizeof(short)));
			samples[index] = sample / 32_768f;
		}
		return samples;
	}

	private static ushort ReadUInt16(byte[] bytes, int offset)
	{
		return (ushort)(bytes[offset] | bytes[offset + 1] << 8);
	}

	private static uint ReadUInt32(byte[] bytes, int offset)
	{
		return (uint)(bytes[offset] |
			bytes[offset + 1] << 8 |
			bytes[offset + 2] << 16 |
			bytes[offset + 3] << 24);
	}
}
