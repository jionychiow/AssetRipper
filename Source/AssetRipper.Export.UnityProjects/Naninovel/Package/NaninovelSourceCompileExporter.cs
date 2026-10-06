using AssetRipper.Import.Logging;
using AssetRipper.IO.Files;
using System.Security.Cryptography;

namespace AssetRipper.Export.UnityProjects.Naninovel.Package;

/// <summary>
/// Extracts Runtime and Editor source code (.cs + .asmdef) from the official Naninovel unitypackage
/// into the export project, enabling Unity to compile the assemblies from source.
/// </summary>
public static class NaninovelSourceCompileExporter
{
	/// <summary>
	/// Extracts Runtime and Editor source files from the unitypackage entries into the export project.
	/// </summary>
	public static ExtractionReport Export(IReadOnlyDictionary<string, UnityPackageEntry> entries, string assetsPath, ExtractionManifest manifest, FileSystem fileSystem)
	{
		ExtractionReport report = new();

		int matchedCount = 0;
		int totalCount = 0;
		foreach (KeyValuePair<string, UnityPackageEntry> kvp in entries)
		{
			string pathName = kvp.Key;
			UnityPackageEntry entry = kvp.Value;
			totalCount++;


			if (!ShouldExtract(pathName, manifest))
			{
				continue;
			}
			matchedCount++;


		string? targetPath = MapToTargetPath(pathName, assetsPath);
		if (targetPath is null)
			{
				report.Add(new ExtractionRecord(pathName, "", ExtractionStatus.Failed, null, "Path traversal detected"));
				continue;
			}

			ExtractionRecord record = ExtractEntry(entry, targetPath, fileSystem);
			report.Add(record);
		}

		Logger.Info(LogCategory.Export, $"[NaninovelPackageExtractor] Source compile export: {report.SucceededCount} succeeded, {report.SkippedCount} skipped, {report.FailedCount} failed");
		return report;
	}

	private static bool ShouldExtract(string pathName, ExtractionManifest manifest)
	{
		pathName = pathName.Trim();
		bool isNaninovelAsset = pathName.StartsWith(manifest.NaninovelAssetPrefix, StringComparison.OrdinalIgnoreCase);

		if (!isNaninovelAsset)
		{
			return false;
		}

		string extension = Path.GetExtension(pathName);
		if (!manifest.ExtractedExtensions.Contains(extension))
		{
			return false;
		}

		foreach (string pattern in manifest.ExcludePatterns)
		{
			if (MatchesWildcard(pathName, pattern))
			{
				return false;
			}
		}

		return true;
	}

	private static string? MapToTargetPath(string pathName, string assetsPath)
	{
		string relativePath = pathName;
		if (relativePath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
		{
			relativePath = relativePath["Assets/".Length..];
		}

		if (relativePath.Contains("..", StringComparison.Ordinal))
		{
			return null;
		}

		return Path.Join(assetsPath, relativePath);
	}

	private static ExtractionRecord ExtractEntry(UnityPackageEntry entry, string targetPath, FileSystem fileSystem)
	{
		try
		{
			string? dir = Path.GetDirectoryName(targetPath);
			if (dir is not null && !fileSystem.Directory.Exists(dir))
			{
				Directory.CreateDirectory(dir);
			}

			string md5 = ComputeMD5(entry.AssetData);

			if (fileSystem.File.Exists(targetPath))
			{
				string existingMd5 = ComputeFileMD5(targetPath);
				if (existingMd5 == md5)
				{
					return new ExtractionRecord(entry.PathName, targetPath, ExtractionStatus.Skipped, md5);
				}
			}

			File.WriteAllBytes(targetPath, entry.AssetData);

			if (!string.IsNullOrEmpty(entry.AssetMeta))
			{
				string metaPath = targetPath + ".meta";
				string? metaDir = Path.GetDirectoryName(metaPath);
				if (metaDir is not null && !Directory.Exists(metaDir))
				{
					Directory.CreateDirectory(metaDir);
				}
				File.WriteAllText(metaPath, entry.AssetMeta);
			}

			return new ExtractionRecord(entry.PathName, targetPath, ExtractionStatus.Succeeded, md5);
		}
		catch (Exception ex)
		{
			return new ExtractionRecord(entry.PathName, targetPath, ExtractionStatus.Failed, null, ex.Message);
		}
	}

	private static bool MatchesWildcard(string path, string pattern)
	{
		string regex = "^" + System.Text.RegularExpressions.Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$";
		return System.Text.RegularExpressions.Regex.IsMatch(path, regex, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
	}

	private static string ComputeMD5(byte[] data)
	{
		using MD5 md5 = MD5.Create();
		byte[] hash = md5.ComputeHash(data);
		return Convert.ToHexString(hash);
	}

	private static string ComputeFileMD5(string filePath)
	{
		using MD5 md5 = MD5.Create();
		using FileStream stream = File.OpenRead(filePath);
		byte[] hash = md5.ComputeHash(stream);
		return Convert.ToHexString(hash);
	}
}