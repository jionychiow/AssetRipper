using System.IO;
using AssetRipper.Import.Logging;

namespace AssetRipper.Export.UnityProjects.Video;

public static class WebmEbmlRepairer
{
	private static readonly byte[] EbmlMagic = [0x1A, 0x45, 0xDF, 0xA3];

	public static bool IsValidEbml(string filePath)
	{
		try
		{
			using FileStream fs = File.OpenRead(filePath);
			if (fs.Length < 4)
			{
				return false;
			}
			Span<byte> header = stackalloc byte[4];
			fs.ReadExactly(header);
			return header[0] == EbmlMagic[0] && header[1] == EbmlMagic[1] && header[2] == EbmlMagic[2] && header[3] == EbmlMagic[3];
		}
		catch
		{
			return false;
		}
	}

	public static int RepairFromResourceFile(string assetsPath, string resourceFilePath)
	{
		if (!File.Exists(resourceFilePath))
		{
			Logger.Warning(LogCategory.Export, $"WebM repair: resource file not found: {resourceFilePath}");
			return 0;
		}

		List<(long Offset, long Size)> ebmlVideos = FindAllEbmlHeaders(resourceFilePath);
		if (ebmlVideos.Count == 0)
		{
			Logger.Warning(LogCategory.Export, "WebM repair: no EBML headers found in resource file");
			return 0;
		}

		Dictionary<long, string> webmBySize = [];
		Dictionary<long, string> ressBySize = [];
		Queue<string> dirs = new();
		dirs.Enqueue(assetsPath);
		while (dirs.Count > 0)
		{
			string dir = dirs.Dequeue();
			try
			{
				foreach (string d in Directory.EnumerateDirectories(dir))
				{
					dirs.Enqueue(d);
				}
				foreach (string f in Directory.EnumerateFiles(dir, "*.webm"))
				{
					long sz = new FileInfo(f).Length;
				 webmBySize.TryAdd(sz, f);
				}
				foreach (string f in Directory.EnumerateFiles(dir, "*.resS"))
				{
					long sz = new FileInfo(f).Length;
					ressBySize.TryAdd(sz, f);
				}
			}
			catch { }
		}

		int fixedCount = 0;
		using FileStream resStream = File.OpenRead(resourceFilePath);
		foreach ((long offset, long size) in ebmlVideos)
		{
			if (webmBySize.TryGetValue(size, out string? webmPath))
			{
				if (IsValidEbml(webmPath))
				{
					continue;
				}
				resStream.Seek(offset, SeekOrigin.Begin);
				byte[] data = new byte[size];
				resStream.ReadExactly(data);
				File.WriteAllBytes(webmPath, data);
				fixedCount++;
			}
			if (ressBySize.TryGetValue(size, out string? ressPath))
			{
				resStream.Seek(offset, SeekOrigin.Begin);
				byte[] data = new byte[size];
				resStream.ReadExactly(data);
				File.WriteAllBytes(ressPath, data);
			}
		}

		return fixedCount;
	}

	private static List<(long Offset, long Size)> FindAllEbmlHeaders(string filePath)
	{
		List<long> offsets = [];
		long fileLength = new FileInfo(filePath).Length;
		const int chunkSize = 64 * 1024 * 1024;
		const int overlap = 3;

		using FileStream fs = File.OpenRead(filePath);
		long pos = 0;
		byte[] prevTail = [];
		while (pos < fileLength)
		{
			int readSize = chunkSize;
			byte[] buffer = new byte[readSize + prevTail.Length];
			if (prevTail.Length > 0)
			{
				Buffer.BlockCopy(prevTail, 0, buffer, 0, prevTail.Length);
			}
			int bytesRead = fs.Read(buffer, prevTail.Length, readSize);
			if (bytesRead == 0)
			{
				break;
			}
			int searchLength = prevTail.Length + bytesRead;

			for (int i = 0; i <= searchLength - 4; i++)
			{
				if (buffer[i] == EbmlMagic[0] && buffer[i + 1] == EbmlMagic[1] && buffer[i + 2] == EbmlMagic[2] && buffer[i + 3] == EbmlMagic[3])
				{
					long absPos = pos - prevTail.Length + i;
					offsets.Add(absPos);
				}
			}

			if (searchLength > overlap)
			{
				prevTail = new byte[overlap];
				Buffer.BlockCopy(buffer, searchLength - overlap, prevTail, 0, overlap);
			}
			pos += bytesRead;
		}

		List<(long Offset, long Size)> result = [];
		for (int i = 0; i < offsets.Count; i++)
		{
			long size = i < offsets.Count - 1 ? offsets[i + 1] - offsets[i] : fileLength - offsets[i];
			result.Add((offsets[i], size));
		}

		Logger.Info(LogCategory.Export, $"WebM repair: found {offsets.Count} EBML headers in {filePath}");
		return result;
	}
}