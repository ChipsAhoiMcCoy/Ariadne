#nullable enable

namespace Ariadne.TestBenches.AudioAnalysis;

/// <summary>
/// Writes what the bench rendered to disk. The checks are the answer; these files
/// exist so a figure that looks wrong can be listened to deliberately rather than
/// the whole mix having to be listened to on principle.
/// </summary>
internal static class Wav
{
	internal static void WriteStereo(string path, ReadOnlySpan<float> left, ReadOnlySpan<float> right, int sampleRate)
	{
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		int frames = Math.Min(left.Length, right.Length);
		using FileStream file = File.Create(path);
		using BinaryWriter writer = new(file);

		int dataBytes = frames * 2 * sizeof(short);
		writer.Write("RIFF"u8);
		writer.Write(36 + dataBytes);
		writer.Write("WAVE"u8);
		writer.Write("fmt "u8);
		writer.Write(16);
		writer.Write((short)1);
		writer.Write((short)2);
		writer.Write(sampleRate);
		writer.Write(sampleRate * 2 * sizeof(short));
		writer.Write((short)(2 * sizeof(short)));
		writer.Write((short)16);
		writer.Write("data"u8);
		writer.Write(dataBytes);

		for (int frame = 0; frame < frames; frame++)
		{
			writer.Write(Encode(left[frame]));
			writer.Write(Encode(right[frame]));
		}
	}

	private static short Encode(float sample)
	{
		return (short)MathF.Round(Math.Clamp(sample, -1f, 1f) * short.MaxValue);
	}
}
