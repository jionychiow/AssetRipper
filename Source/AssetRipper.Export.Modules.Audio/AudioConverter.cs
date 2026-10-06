using AssetRipper.Export.Modules.Audio.Fmod;
using AssetRipper.Import.Logging;

namespace AssetRipper.Export.Modules.Audio;

public static class AudioConverter
{
	public static byte[] OggToWav(byte[] oggData)
	{
		ArgumentNullException.ThrowIfNull(oggData);

		if (oggData.Length == 0)
		{
			return [];
		}

		byte[]? wavData = FmodRuntimeDecoder.DecodeToWav(oggData);
		if (wavData is not null)
		{
			return wavData;
		}

		Logger.Error(LogCategory.Export, "Failed to convert audio from OGG to WAV via FMOD runtime decoder");
		return [];
	}
}
