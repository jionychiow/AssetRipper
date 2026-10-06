using System;
using System.Text;

namespace AssetRipper.Export.Modules.Audio.Fmod;

public static class WavBuilder
{
	public static byte[] BuildWav(byte[] pcmData, int channels, int sampleRate, int bitsPerSample)
	{
		ArgumentNullException.ThrowIfNull(pcmData);
		if (channels <= 0)
		{
			throw new ArgumentException("Channels must be positive", nameof(channels));
		}
		if (sampleRate <= 0)
		{
			throw new ArgumentException("Sample rate must be positive", nameof(sampleRate));
		}
		if (bitsPerSample is not (8 or 16 or 24 or 32))
		{
			throw new ArgumentException("Bits per sample must be 8, 16, 24, or 32", nameof(bitsPerSample));
		}

		int byteRate = sampleRate * channels * bitsPerSample / 8;
		short blockAlign = (short)(channels * bitsPerSample / 8);
		byte[] wavData = new byte[pcmData.Length + 44];

		Encoding.UTF8.GetBytes("RIFF").CopyTo(wavData, 0);
		BitConverter.GetBytes((uint)(pcmData.Length + 36)).CopyTo(wavData, 4);
		Encoding.UTF8.GetBytes("WAVEfmt ").CopyTo(wavData, 8);
		BitConverter.GetBytes(16).CopyTo(wavData, 16);
		BitConverter.GetBytes((short)1).CopyTo(wavData, 20);
		BitConverter.GetBytes((short)channels).CopyTo(wavData, 22);
		BitConverter.GetBytes(sampleRate).CopyTo(wavData, 24);
		BitConverter.GetBytes(byteRate).CopyTo(wavData, 28);
		BitConverter.GetBytes(blockAlign).CopyTo(wavData, 32);
		BitConverter.GetBytes((short)bitsPerSample).CopyTo(wavData, 34);
		Encoding.UTF8.GetBytes("data").CopyTo(wavData, 36);
		BitConverter.GetBytes((uint)pcmData.Length).CopyTo(wavData, 40);
		Buffer.BlockCopy(pcmData, 0, wavData, 44, pcmData.Length);

		return wavData;
	}
}