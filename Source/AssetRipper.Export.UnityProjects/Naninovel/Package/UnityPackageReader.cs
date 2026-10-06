using System.Formats.Tar;
using System.IO.Compression;
using System.Text;

namespace AssetRipper.Export.UnityProjects.Naninovel.Package;

/// <summary>
/// Reads a Unity .unitypackage (tar.gz) file and returns a mapping of pathname to entry data.
/// </summary>
public static class UnityPackageReader
{
	/// <summary>
	/// Reads all entries from a .unitypackage file.
	/// </summary>
	/// <param name="packagePath">The path to the .unitypackage file.</param>
	/// <returns>A dictionary mapping pathname to UnityPackageEntry.</returns>
	/// <exception cref="FileNotFoundException">Thrown when the package file does not exist.</exception>
	/// <exception cref="InvalidDataException">Thrown when the package file is corrupted or invalid.</exception>
	public static IReadOnlyDictionary<string, UnityPackageEntry> Read(string packagePath)
	{
		if (!File.Exists(packagePath))
		{
			throw new FileNotFoundException($"Unity package file not found: {packagePath}", packagePath);
		}

		Dictionary<string, UnityPackageEntry> entries = new(StringComparer.Ordinal);

		using FileStream fileStream = File.OpenRead(packagePath);
		using GZipStream gzipStream = new(fileStream, CompressionMode.Decompress);
		using TarReader tarReader = new(gzipStream);

		Dictionary<string, Dictionary<string, (byte[] Data, string Name)>> guidEntries = new(StringComparer.Ordinal);

		while (tarReader.GetNextEntry() is { } tarEntry)
		{
			if (tarEntry.EntryType is TarEntryType.Directory)
			{
				continue;
			}

			string name = tarEntry.Name;
			if (name.StartsWith("./", StringComparison.Ordinal))
			{
				name = name[2..];
			}

			int separatorIndex = name.IndexOf('/');
			if (separatorIndex <= 0)
			{
				continue;
			}

			string guid = name[..separatorIndex];
			string fileName = name[(separatorIndex + 1)..];

			byte[] data = new byte[tarEntry.Length];
			using Stream? dataStream = tarEntry.DataStream;
			if (dataStream is not null && tarEntry.Length > 0)
			{
				int totalRead = 0;
				int toRead = (int)tarEntry.Length;
				while (totalRead < toRead)
				{
					int read = dataStream.Read(data, totalRead, toRead - totalRead);
					if (read <= 0)
					{
						break;
					}
					totalRead += read;
				}
			}

			if (!guidEntries.TryGetValue(guid, out Dictionary<string, (byte[] Data, string Name)>? guidFiles))
			{
				guidFiles = new Dictionary<string, (byte[] Data, string Name)>(StringComparer.Ordinal);
				guidEntries[guid] = guidFiles;
			}

			guidFiles[fileName] = (data, name);
		}

		foreach (KeyValuePair<string, Dictionary<string, (byte[] Data, string Name)>> kvp in guidEntries)
		{
			Dictionary<string, (byte[] Data, string Name)> guidFiles = kvp.Value;

			if (!guidFiles.TryGetValue("pathname", out (byte[] Data, string Name) pathnameTuple))
			{
				continue;
			}

			if (!guidFiles.TryGetValue("asset", out (byte[] Data, string Name) assetTuple))
			{
				continue;
			}

			string pathName = Encoding.UTF8.GetString(pathnameTuple.Data);
			int newlineIdx = pathName.IndexOfAny(['\n', '\r', '\0']);
			if (newlineIdx >= 0)
			{
				pathName = pathName[..newlineIdx];
			}
			pathName = pathName.Trim();
			if (string.IsNullOrEmpty(pathName))
			{
				continue;
			}

			guidFiles.TryGetValue("asset.meta", out (byte[] Data, string Name) metaTuple);
			string assetMeta = metaTuple.Data is not null ? Encoding.UTF8.GetString(metaTuple.Data).TrimEnd('\0') : string.Empty;

			entries[pathName] = new UnityPackageEntry(kvp.Key, pathName, assetTuple.Data, assetMeta);
		}

		return entries;
	}
}
