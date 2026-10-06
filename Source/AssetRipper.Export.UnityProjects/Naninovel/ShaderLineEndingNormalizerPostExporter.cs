using AssetRipper.Export.Configuration;
using AssetRipper.Import.Logging;
using AssetRipper.Processing;
using System.IO;
using System.Text;
using System.Collections.Generic;

namespace AssetRipper.Export.UnityProjects.Naninovel;

public sealed class ShaderLineEndingNormalizerPostExporter : IPostExporter
{
	private const string ResourcesFolder = "Resources";
	private const string NaninovelShadersFolder = "naninovel";
	private const string ShadersFolder = "shaders";


	public void DoPostExport(GameData gameData, FullConfiguration settings, FileSystem fileSystem)
	{
		Logger.Info(LogCategory.Export, "ShaderLineEndingNormalizerPostExporter: normalizing shader line endings to CRLF...");

		string shadersPath = fileSystem.Path.Join(settings.AssetsPath, ResourcesFolder, NaninovelShadersFolder, ShadersFolder);
		if (!fileSystem.Directory.Exists(shadersPath))
		{
			Logger.Info(LogCategory.Export, $"Shader directory not found: {shadersPath}");
			return;
		}

		int fixedCount = 0;
		int scannedCount = 0;

		ProcessDirectory(shadersPath, fileSystem, ref scannedCount, ref fixedCount);

		Logger.Info(LogCategory.Export, $"ShaderLineEndingNormalizerPostExporter: scanned {scannedCount} shader files, fixed {fixedCount} with mixed line endings.");

		FixCorruptedPngFiles(settings.AssetsPath);
		FixPlaceholderFontTextures(settings.AssetsPath);
	}

	private static void FixPlaceholderFontTextures(string assetsPath)
	{
		string fontsPath = Path.Join(assetsPath, "Resources", "fonts & materials");
		if (!Directory.Exists(fontsPath))
		{
			return;
		}

		int fixedCount = 0;
		foreach (string pngFile in Directory.EnumerateFiles(fontsPath, "*.png", SearchOption.TopDirectoryOnly))
		{
			try
			{
				byte[] data = File.ReadAllBytes(pngFile);
				if (data.Length != 67)
				{
					continue;
				}

				bool is1x1Placeholder = data.Length >= 24 &&
					data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47 &&
					data[16] == 0x00 && data[17] == 0x00 && data[18] == 0x00 && data[19] == 0x01 &&
					data[20] == 0x00 && data[21] == 0x00 && data[22] == 0x00 && data[23] == 0x01;
				if (!is1x1Placeholder)
				{
					continue;
				}

				string baseName = Path.GetFileNameWithoutExtension(pngFile);
				string atlas0Path = Path.Join(fontsPath, baseName + "_0.png");
				if (!File.Exists(atlas0Path))
				{
					continue;
				}

				byte[] atlasData = File.ReadAllBytes(atlas0Path);
				File.WriteAllBytes(pngFile, atlasData);
				fixedCount++;
				Logger.Info(LogCategory.Export, $"FixPlaceholderFontTextures: replaced 1x1 placeholder '{Path.GetFileName(pngFile)}' with atlas data from '{baseName}_0.png' ({atlasData.Length} bytes).");
			}
			catch (Exception ex)
			{
				Logger.Warning(LogCategory.Export, $"FixPlaceholderFontTextures: failed to check/fix {pngFile}: {ex.Message}");
			}
		}

		if (fixedCount > 0)
		{
			Logger.Info(LogCategory.Export, $"FixPlaceholderFontTextures: fixed {fixedCount} placeholder font texture(s).");
		}
	}

	private static void FixCorruptedPngFiles(string assetsPath)
	{
		string resourcesPath = Path.Join(assetsPath, "Resources");
		if (!Directory.Exists(resourcesPath))
		{
			return;
		}

		int fixedCount = 0;
		foreach (string pngFile in Directory.EnumerateFiles(resourcesPath, "*.png", SearchOption.AllDirectories))
		{
			try
			{
				byte[] data = File.ReadAllBytes(pngFile);
				bool valid = data.Length >= 8 &&
					data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47 &&
					data.Length >= 25;
				if (!valid)
				{
					bool isBackground = pngFile.Contains("Backgrounds", StringComparison.OrdinalIgnoreCase);
					byte[] placeholder = isBackground ? CreateSizedPlaceholderPng(128, 128) : CreateSizedPlaceholderPng(1, 1);
					File.WriteAllBytes(pngFile, placeholder);
					fixedCount++;
					Logger.Info(LogCategory.Export, $"ShaderLineEndingNormalizerPostExporter: fixed corrupted PNG {(isBackground ? "128x128" : "1x1")} placeholder: {pngFile}");
				}
			}
			catch (Exception ex)
			{
				Logger.Warning(LogCategory.Export, $"Failed to check/fix PNG {pngFile}: {ex.Message}");
			}
		}

		if (fixedCount > 0)
		{
			Logger.Info(LogCategory.Export, $"ShaderLineEndingNormalizerPostExporter: fixed {fixedCount} corrupted PNG file(s) in final check.");
		}
	}

	private static byte[] CreateSizedPlaceholderPng(int width, int height)
	{
		using MemoryStream stream = new();
		using BinaryWriter writer = new(stream, System.Text.Encoding.ASCII, leaveOpen: true);
		writer.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
		byte[] ihdr = new byte[13];
		ihdr[0] = (byte)((width >> 24) & 0xFF); ihdr[1] = (byte)((width >> 16) & 0xFF);
		ihdr[2] = (byte)((width >> 8) & 0xFF); ihdr[3] = (byte)(width & 0xFF);
		ihdr[4] = (byte)((height >> 24) & 0xFF); ihdr[5] = (byte)((height >> 16) & 0xFF);
		ihdr[6] = (byte)((height >> 8) & 0xFF); ihdr[7] = (byte)(height & 0xFF);
		ihdr[8] = 8; ihdr[9] = 6; ihdr[10] = 0; ihdr[11] = 0; ihdr[12] = 0;
		WritePngChunk(writer, "IHDR", ihdr);
		WritePngChunk(writer, "IDAT", CreateSizedIdatData(width, height));
		WritePngChunk(writer, "IEND", []);
		return stream.ToArray();
	}

	private static byte[] CreateSizedIdatData(int width, int height)
	{
		using MemoryStream rawStream = new();
		using BinaryWriter rawWriter = new(rawStream, System.Text.Encoding.ASCII, leaveOpen: true);
		for (int y = 0; y < height; y++)
		{
			rawWriter.Write((byte)0);
			for (int x = 0; x < width; x++)
			{
				rawWriter.Write((byte)128); rawWriter.Write((byte)128);
				rawWriter.Write((byte)128); rawWriter.Write((byte)255);
			}
		}
		byte[] rawData = rawStream.ToArray();
		using MemoryStream compressedStream = new();
		using (System.IO.Compression.DeflateStream deflateStream = new(compressedStream, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true))
		{
			deflateStream.Write(rawData, 0, rawData.Length);
		}
		byte[] deflateData = compressedStream.ToArray();
		byte[] zlibData = new byte[2 + deflateData.Length + 4];
		zlibData[0] = 0x78; zlibData[1] = 0x9C;
		Buffer.BlockCopy(deflateData, 0, zlibData, 2, deflateData.Length);
		uint adler32 = ComputeAdler32(rawData);
		zlibData[^4] = (byte)((adler32 >> 24) & 0xFF);
		zlibData[^3] = (byte)((adler32 >> 16) & 0xFF);
		zlibData[^2] = (byte)((adler32 >> 8) & 0xFF);
		zlibData[^1] = (byte)(adler32 & 0xFF);
		return zlibData;
	}

	private static void WritePngChunk(BinaryWriter writer, string type, byte[] data)
	{
		int len = data.Length;
		writer.Write((byte)((len >> 24) & 0xFF));
		writer.Write((byte)((len >> 16) & 0xFF));
		writer.Write((byte)((len >> 8) & 0xFF));
		writer.Write((byte)(len & 0xFF));
		byte[] typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
		writer.Write(typeBytes);
		writer.Write(data);
		uint crc = ComputeCrc32(typeBytes, data);
		writer.Write((byte)((crc >> 24) & 0xFF));
		writer.Write((byte)((crc >> 16) & 0xFF));
		writer.Write((byte)((crc >> 8) & 0xFF));
		writer.Write((byte)(crc & 0xFF));
	}

	private static uint ComputeCrc32(byte[] type, byte[] data)
	{
		uint[] table = new uint[256];
		for (uint i = 0; i < 256; i++)
		{
			uint c = i;
			for (int j = 0; j < 8; j++) c = (c & 1) != 0 ? (0xEDB88320 ^ (c >> 1)) : (c >> 1);
			table[i] = c;
		}
		uint crc = 0xFFFFFFFF;
		for (int i = 0; i < type.Length; i++) crc = table[(crc ^ type[i]) & 0xFF] ^ (crc >> 8);
		for (int i = 0; i < data.Length; i++) crc = table[(crc ^ data[i]) & 0xFF] ^ (crc >> 8);
		return crc ^ 0xFFFFFFFF;
	}

	private static uint ComputeAdler32(byte[] data)
	{
		const uint MOD = 65521;
		uint a = 1, b = 0;
		for (int i = 0; i < data.Length; i++)
		{
			a = (a + data[i]) % MOD;
			b = (b + a) % MOD;
		}
		return (b << 16) | a;
	}

	private static void ProcessDirectory(string directoryPath, FileSystem fileSystem, ref int scannedCount, ref int fixedCount)
	{
		foreach (string filePath in fileSystem.Directory.EnumerateFiles(directoryPath, "*.shader"))
		{
			scannedCount++;
			if (NormalizeLineEndings(filePath, fileSystem))
			{
				fixedCount++;
			}
		}

		foreach (string subDir in fileSystem.Directory.EnumerateDirectories(directoryPath))
		{
			ProcessDirectory(subDir, fileSystem, ref scannedCount, ref fixedCount);
		}
	}

	private static bool NormalizeLineEndings(string filePath, FileSystem fileSystem)
	{
		byte[] bytes = fileSystem.File.ReadAllBytes(filePath);

		bool hasCRLF = false;
		bool hasLF = false;
		bool hasCR = false;

		for (int i = 0; i < bytes.Length; i++)
		{
			if (bytes[i] == 0x0D)
			{
				if (i + 1 < bytes.Length && bytes[i + 1] == 0x0A)
				{
					hasCRLF = true;
					i++;
				}
				else
				{
					hasCR = true;
				}
			}
			else if (bytes[i] == 0x0A)
			{
				hasLF = true;
			}
		}

		bool needsFix = (hasLF && hasCRLF) || hasCR || (hasLF && !hasCRLF);
		if (!needsFix)
		{
			return false;
		}

		List<byte> result = new(bytes.Length);
		for (int i = 0; i < bytes.Length; i++)
		{
			if (bytes[i] == 0x0D)
			{
				result.Add((byte)0x0D);
				result.Add((byte)0x0A);
				if (i + 1 < bytes.Length && bytes[i + 1] == 0x0A)
				{
					i++;
				}
			}
			else if (bytes[i] == 0x0A)
			{
				result.Add((byte)0x0D);
				result.Add((byte)0x0A);
			}
			else
			{
				result.Add(bytes[i]);
			}
		}

		fileSystem.File.WriteAllBytes(filePath, result.ToArray());

		if (!VerifyLineEndings(filePath, fileSystem))
		{
			Logger.Error(LogCategory.Export, $"ShaderLineEndingNormalizer: verification failed for {filePath}");
			return false;
		}

		return true;
	}

	private static bool VerifyLineEndings(string filePath, FileSystem fileSystem)
	{
		byte[] bytes = fileSystem.File.ReadAllBytes(filePath);
		for (int i = 0; i < bytes.Length; i++)
		{
			if (bytes[i] == 0x0A && (i == 0 || bytes[i - 1] != 0x0D))
			{
				return false;
			}
		}
		return true;
	}
}