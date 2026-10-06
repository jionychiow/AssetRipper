using System.IO;

namespace AssetRipper.SourceGenerated.Extensions;

internal static class Fsb5OffsetCorrector
{
	private const int Fsb5MagicLength = 4;
	private const int SearchRange = 65536;
	private static readonly byte[] Fsb5Magic = [0x46, 0x53, 0x42, 0x35];

	internal static byte[]? TryCorrectFsb5Offset(Stream stream, long originalOffset, long originalSize, byte[] originalData)
	{
		if (originalData.Length < Fsb5MagicLength || !IsFsb5Magic(originalData, 0))
		{
			long? fsb5Pos = SearchFsb5Header(stream, originalOffset);
			if (fsb5Pos is null)
			{
				return null;
			}

			long headerPos = fsb5Pos.Value;
			int totalSize = CalculateFsb5TotalSize(stream, headerPos);
			if (totalSize <= 0)
			{
				long fallbackSize = originalOffset + originalSize - headerPos;
				if (fallbackSize <= 0 || fallbackSize > stream.Length - headerPos)
				{
					return null;
				}
				totalSize = (int)fallbackSize;
			}

			if (headerPos + totalSize > stream.Length)
			{
				totalSize = (int)(stream.Length - headerPos);
			}

			byte[] correctedData = new byte[totalSize];
			long savedPos = stream.Position;
			try
			{
				stream.Position = headerPos;
				stream.ReadExactly(correctedData);
			}
			catch (IOException)
			{
				stream.Position = savedPos;
				return null;
			}
			finally
			{
				stream.Position = savedPos;
			}

			if (IsFsb5Magic(correctedData, 0))
			{
				return correctedData;
			}
		}

		return null;
	}

	private static long? SearchFsb5Header(Stream stream, long originalOffset)
	{
		long searchStart = Math.Max(0, originalOffset - SearchRange);
		long searchEnd = Math.Min(stream.Length - Fsb5MagicLength, originalOffset + SearchRange);
		int bufferSize = (int)(searchEnd - searchStart + Fsb5MagicLength);
		if (bufferSize <= 0)
		{
			return null;
		}

		byte[] buffer = new byte[bufferSize];
		long savedPos = stream.Position;
		try
		{
			stream.Position = searchStart;
			int bytesRead = stream.Read(buffer, 0, bufferSize);
			if (bytesRead < Fsb5MagicLength)
			{
				return null;
			}

			for (int i = 0; i <= bytesRead - Fsb5MagicLength; i++)
			{
				if (buffer[i] == Fsb5Magic[0] &&
					buffer[i + 1] == Fsb5Magic[1] &&
					buffer[i + 2] == Fsb5Magic[2] &&
					buffer[i + 3] == Fsb5Magic[3])
				{
					return searchStart + i;
				}
			}
		}
		catch (IOException)
		{
			return null;
		}
		finally
		{
			stream.Position = savedPos;
		}

		return null;
	}

	private static int CalculateFsb5TotalSize(Stream stream, long fsb5HeaderPos)
	{
		if (fsb5HeaderPos + 16 > stream.Length)
		{
			return -1;
		}

		byte[] header = new byte[16];
		long savedPos = stream.Position;
		try
		{
			stream.Position = fsb5HeaderPos;
			stream.ReadExactly(header, 0, 16);
		}
		catch (IOException)
		{
			stream.Position = savedPos;
			return -1;
		}
		finally
		{
			stream.Position = savedPos;
		}

		if (!IsFsb5Magic(header, 0))
		{
			return -1;
		}

		int headerSize = BitConverter.ToInt32(header, 4);
		int dataSize = BitConverter.ToInt32(header, 8);
		if (headerSize <= 0 || dataSize <= 0)
		{
			return -1;
		}

		return headerSize + dataSize;
	}

	private static bool IsFsb5Magic(byte[] data, int offset)
	{
		return offset + Fsb5MagicLength <= data.Length &&
			data[offset] == 0x46 &&
			data[offset + 1] == 0x53 &&
			data[offset + 2] == 0x42 &&
			data[offset + 3] == 0x35;
	}
}