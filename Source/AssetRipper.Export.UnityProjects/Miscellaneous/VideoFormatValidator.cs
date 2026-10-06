namespace AssetRipper.Export.UnityProjects.Miscellaneous;

public static class VideoFormatValidator
{
	private static readonly byte[] EbmlHeader = [0x1A, 0x45, 0xDF, 0xA3];
	private static readonly byte[] FtypHeader = [0x66, 0x74, 0x79, 0x70];

	public static bool IsValidVideo(byte[]? data)
	{
		if (data is null || data.Length < 8)
		{
			return false;
		}

		return IsWebM(data) || IsMp4(data);
	}

	public static string GetVideoFormat(byte[]? data)
	{
		if (data is null || data.Length < 8)
		{
			return "Unknown";
		}

		if (IsWebM(data))
		{
			return "WebM";
		}

		if (IsMp4(data))
		{
			return "MP4";
		}

		return "Unknown";
	}

	private static bool IsWebM(byte[] data)
	{
		return data.Length >= 4
			&& data[0] == EbmlHeader[0]
			&& data[1] == EbmlHeader[1]
			&& data[2] == EbmlHeader[2]
			&& data[3] == EbmlHeader[3];
	}

	private static bool IsMp4(byte[] data)
	{
		return data.Length >= 8
			&& data[4] == FtypHeader[0]
			&& data[5] == FtypHeader[1]
			&& data[6] == FtypHeader[2]
			&& data[7] == FtypHeader[3];
	}
}