using System.IO.Compression;

namespace AssetRipper.Export.UnityProjects.Naninovel.Package;

/// <summary>
/// Reads a DOTween .zip package and returns a mapping of entry name to file content.
/// </summary>
public static class DotTweenZipReader
{
	/// <summary>
	/// Reads all file entries from a .zip archive.
	/// </summary>
	/// <param name="zipPath">The path to the .zip file.</param>
	/// <returns>A dictionary mapping entry full name to file content bytes.</returns>
	/// <exception cref="FileNotFoundException">Thrown when the zip file does not exist.</exception>
	/// <exception cref="InvalidDataException">Thrown when the zip file is corrupted or invalid.</exception>
	public static IReadOnlyDictionary<string, byte[]> Read(string zipPath)
	{
		if (!File.Exists(zipPath))
		{
			throw new FileNotFoundException($"DOTween zip file not found: {zipPath}", zipPath);
		}

		Dictionary<string, byte[]> entries = new(StringComparer.Ordinal);

		try
		{
			using FileStream stream = File.OpenRead(zipPath);
			using ZipArchive archive = new(stream, ZipArchiveMode.Read);

			foreach (ZipArchiveEntry entry in archive.Entries)
			{
				if (entry.FullName.EndsWith('/') || entry.Length == 0)
				{
					continue;
				}

				using Stream entryStream = entry.Open();
				using MemoryStream memory = new();
				entryStream.CopyTo(memory);
				entries[entry.FullName.Replace('/', Path.DirectorySeparatorChar)] = memory.ToArray();
			}
		}
		catch (InvalidDataException)
		{
			throw;
		}

		return entries;
	}
}