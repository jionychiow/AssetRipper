using AssetRipper.Export.Configuration;
using AssetRipper.Import.Logging;
using AssetRipper.Processing;
using System.IO;

namespace AssetRipper.Export.UnityProjects.Naninovel.Dedup;

public sealed class DuplicateNaniRemoverPostExporter : IPostExporter
{
	private const string NaninovelResourcesPath = "Naninovel/Resources";
	private const string GameResourcesPath = "Resources";
	private const string NaniExtension = ".nani";

	public void DoPostExport(GameData gameData, FullConfiguration settings, FileSystem fileSystem)
	{
		Logger.Info(LogCategory.Export, "DuplicateNaniRemoverPostExporter: scanning for duplicate .nani files...");

		List<string> duplicates = FindDuplicateNaniFiles(settings.AssetsPath, fileSystem);
		if (duplicates.Count == 0)
		{
			Logger.Info(LogCategory.Export, "DuplicateNaniRemoverPostExporter: no duplicate .nani files found.");
			return;
		}

		int removedCount = 0;
		foreach (string filePath in duplicates)
		{
			if (RemoveFile(filePath, fileSystem))
			{
				removedCount++;
			}
		}

		Logger.Info(LogCategory.Export, $"Removed {removedCount} duplicate .nani file(s).");
	}

	private static List<string> FindDuplicateNaniFiles(string assetsPath, FileSystem fileSystem)
	{
		string naninovelResourcesDir = fileSystem.Path.Join(assetsPath, NaninovelResourcesPath);
		string gameResourcesDir = fileSystem.Path.Join(assetsPath, GameResourcesPath);

		if (!fileSystem.Directory.Exists(naninovelResourcesDir))
		{
			Logger.Info(LogCategory.Export, $"Naninovel resources directory not found: {naninovelResourcesDir}");
			return [];
		}

		if (!fileSystem.Directory.Exists(gameResourcesDir))
		{
			Logger.Info(LogCategory.Export, $"Game resources directory not found: {gameResourcesDir}");
			return [];
		}

		Dictionary<string, string> naninovelFiles = new(StringComparer.Ordinal);
		foreach ((string fullPath, string relativePath) in EnumerateNaniFiles(naninovelResourcesDir, naninovelResourcesDir, fileSystem))
		{
			naninovelFiles[relativePath] = fullPath;
		}

		Dictionary<string, string> gameFiles = new(StringComparer.Ordinal);
		foreach ((string fullPath, string relativePath) in EnumerateNaniFiles(gameResourcesDir, gameResourcesDir, fileSystem))
		{
			gameFiles[relativePath] = fullPath;
		}

		List<string> result = new();
		foreach (KeyValuePair<string, string> entry in naninovelFiles)
		{
			if (gameFiles.ContainsKey(entry.Key))
			{
				result.Add(entry.Value);
			}
		}

		return result;
	}

	private static IEnumerable<(string FullPath, string RelativePath)> EnumerateNaniFiles(string rootDir, string baseDir, FileSystem fileSystem)
	{
		foreach (string file in fileSystem.Directory.EnumerateFiles(rootDir, "*" + NaniExtension, SearchOption.AllDirectories))
		{
			string relativePath = fileSystem.Path.GetRelativePath(baseDir, file);
			yield return (file, relativePath.Replace('\\', '/'));
		}
	}

	private static bool RemoveFile(string filePath, FileSystem fileSystem)
	{
		try
		{
			if (fileSystem.File.Exists(filePath))
			{
				File.Delete(filePath);
			}

			string metaPath = filePath + ".meta";
			if (fileSystem.File.Exists(metaPath))
			{
				File.Delete(metaPath);
			}

			Logger.Info(LogCategory.Export, $"Removed duplicate: {filePath}");
			return true;
		}
		catch (Exception ex)
		{
			Logger.Warning(LogCategory.Export, $"Failed to remove duplicate {filePath}: {ex.Message}");
			return false;
		}
	}
}