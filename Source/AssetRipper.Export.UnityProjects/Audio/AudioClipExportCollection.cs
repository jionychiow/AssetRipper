using AssetRipper.Assets;
using AssetRipper.Export.Modules.Audio;
using AssetRipper.Import.Logging;
using AssetRipper.SourceGenerated.Classes.ClassID_83;
using System.IO;

namespace AssetRipper.Export.UnityProjects.Audio;

public sealed class AudioClipExportCollection : AudioExportCollection
{
	private readonly string fileExtension;
	public AudioClipExportCollection(AudioClipExporter assetExporter, IAudioClip asset, string fileExtension) : base(assetExporter, asset)
	{
		this.fileExtension = fileExtension;
	}

	protected override bool ExportInner(IExportContainer container, string filePath, string dirPath, FileSystem fileSystem)
	{
		if (!TryDecodeAudioData(out byte[]? data, out string? message))
		{
			Logger.Warning(LogCategory.Export, $"Audio decode failed for '{Asset.Name}', writing dummy WAV: {message}");
			data = CreateDummyWav(Asset);
		}
		else if (data.Length < 44 || data[0] != (byte)'R' || data[1] != (byte)'I' || data[2] != (byte)'F' || data[3] != (byte)'F')
		{
			Logger.Warning(LogCategory.Export, $"Decoded audio for '{Asset.Name}' is not valid WAV, writing dummy WAV.");
			data = CreateDummyWav(Asset);
		}

		fileSystem.File.WriteAllBytes(filePath, data);
		return true;
	}


	private bool TryDecodeAudioData([NotNullWhen(true)] out byte[]? decodedData, [NotNullWhen(false)] out string? message)
	{
		if (AudioClipDecoder.TryDecode(Asset, out decodedData, out string? fileExtension, out message))
		{
			if (fileExtension is "ogg" && this.fileExtension is "wav")
			{
				decodedData = AudioConverter.OggToWav(decodedData);
			}
			return true;
		}
		else
		{
			return false;
		}
	}

	protected override string GetExportExtension(IUnityObjectBase asset)
	{
		return fileExtension;
	}

	private static byte[] CreateDummyWav(IAudioClip audio)
	{
		ushort channels = 1;
		uint sampleRate = 44100;
		ushort bitsPerSample = 16;
		uint numSamples = 44100;
		uint dataSize = (uint)(numSamples * channels * (bitsPerSample / 8));
		uint byteRate = (uint)(sampleRate * channels * (bitsPerSample / 8));
		ushort blockAlign = (ushort)(channels * (bitsPerSample / 8));

		using MemoryStream stream = new();
		using BinaryWriter writer = new(stream, System.Text.Encoding.ASCII, leaveOpen: true);

		writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
		writer.Write(36 + dataSize);
		writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
		writer.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
		writer.Write(16u);
		writer.Write((ushort)1);
		writer.Write(channels);
		writer.Write(sampleRate);
		writer.Write(byteRate);
		writer.Write(blockAlign);
		writer.Write(bitsPerSample);
		writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));
		writer.Write(dataSize);
		for (int i = 0; i < dataSize; i++)
		{
			writer.Write((byte)0);
		}

		return stream.ToArray();
	}
}
