using AssetRipper.Export.Configuration;
using AssetRipper.Import.Logging;
using AssetRipper.Processing;
using System.IO;
using System.Text.RegularExpressions;

namespace AssetRipper.Export.UnityProjects.Naninovel.Dedup;

public sealed class NaninovelResourceDedupPostExporter : IPostExporter
{
	private const string ResourcesPath = "Resources";
	private const string NaniExtension = ".nani";
	private const string MetaExtension = ".meta";
	private static readonly Regex SuffixRegex = new(@"_(\d+)(\.[^.]+)$", RegexOptions.Compiled);

	public void DoPostExport(GameData gameData, FullConfiguration settings, FileSystem fileSystem)
	{
		Logger.Info(LogCategory.Export, "NaninovelResourceDedupPostExporter: ensuring non-suffixed resource copies...");

		string resourcesDir = fileSystem.Path.Join(settings.AssetsPath, ResourcesPath);
		if (!fileSystem.Directory.Exists(resourcesDir))
		{
			Logger.Info(LogCategory.Export, $"Resources directory not found: {resourcesDir}");
			return;
		}

		int createdCount = 0;
		foreach (string filePath in EnumerateTargetFiles(resourcesDir, fileSystem))
		{
			if (TryCreateNonSuffixedCopy(filePath, fileSystem))
			{
				createdCount++;
			}
		}

		Logger.Info(LogCategory.Export, $"NaninovelResourceDedupPostExporter: created {createdCount} non-suffixed copy/copies.");
	}

	private static IEnumerable<string> EnumerateTargetFiles(string rootDir, FileSystem fileSystem)
	{
		foreach (string file in fileSystem.Directory.EnumerateFiles(rootDir, "*" + NaniExtension, SearchOption.AllDirectories))
		{
			yield return file;
		}

		string naninovelDir = fileSystem.Path.Join(rootDir, "naninovel");
		if (fileSystem.Directory.Exists(naninovelDir))
		{
			foreach (string file in fileSystem.Directory.EnumerateFiles(naninovelDir, "*", SearchOption.AllDirectories))
			{
				if (file.EndsWith(MetaExtension, StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}
				yield return file;
			}
		}
	}

	private static bool TryCreateNonSuffixedCopy(string filePath, FileSystem fileSystem)
	{
		string fileName = Path.GetFileName(filePath);
		Match match = SuffixRegex.Match(fileName);
		if (!match.Success)
		{
			return false;
		}

		string nonSuffixedName = fileName[..match.Index] + match.Groups[2].Value;
		string dirPath = Path.GetDirectoryName(filePath)!;
		string nonSuffixedPath = Path.Join(dirPath, nonSuffixedName);

		if (fileSystem.File.Exists(nonSuffixedPath))
		{
			return false;
		}

		try
		{
			File.Copy(filePath, nonSuffixedPath, overwrite: false);
			string metaPath = filePath + MetaExtension;
			if (File.Exists(metaPath))
			{
				string newMetaPath = nonSuffixedPath + MetaExtension;
				string metaContent = File.ReadAllText(metaPath);
				string newGuid = System.Guid.NewGuid().ToString("N");
				metaContent = ReplaceGuidInMeta(metaContent, newGuid);
				File.WriteAllText(newMetaPath, metaContent);
			}
			Logger.Info(LogCategory.Export, $"Created non-suffixed copy: {nonSuffixedPath} (from {filePath})");
			return true;
		}
		catch (Exception ex)
		{
			Logger.Warning(LogCategory.Export, $"Failed to create non-suffixed copy of {filePath}: {ex.Message}");
			return false;
		}
	}

	private static string ReplaceGuidInMeta(string metaContent, string newGuid)
	{
		int guidIndex = metaContent.IndexOf("guid: ", StringComparison.Ordinal);
		if (guidIndex < 0)
		{
			return metaContent;
		}
		int valueStart = guidIndex + 6;
		int valueEnd = valueStart;
		while (valueEnd < metaContent.Length && metaContent[valueEnd] != '\n' && metaContent[valueEnd] != '\r')
		{
			valueEnd++;
		}
		return metaContent[..valueStart] + newGuid + metaContent[valueEnd..];
	}
}