using AssetRipper.Export.Configuration;
using AssetRipper.Import.Logging;
using AssetRipper.Processing;

namespace AssetRipper.Export.UnityProjects.Project;

public sealed class ResourcePathCasingFixer : IPostExporter
{
	private const string ResourcesFolder = "Resources";
	private const string PathPrefixYamlKey = "PathPrefix:";

	public void DoPostExport(GameData gameData, FullConfiguration settings, FileSystem fileSystem)
	{
		string resourcesPath = fileSystem.Path.Join(settings.AssetsPath, ResourcesFolder);
		if (!fileSystem.Directory.Exists(resourcesPath))
		{
			return;
		}

		HashSet<string> pathPrefixes = CollectPathPrefixes(resourcesPath, fileSystem);
		if (pathPrefixes.Count == 0)
		{
			return;
		}

		int renamedCount = 0;
		foreach (string pathPrefix in pathPrefixes)
		{
			if (TryRenameFolderToMatchCasing(resourcesPath, pathPrefix, fileSystem))
			{
				renamedCount++;
			}
		}

		if (renamedCount > 0)
		{
			Logger.Info(LogCategory.Export, $"Fixed casing for {renamedCount} resource folder(s) to match PathPrefix values.");
		}
	}

	private static HashSet<string> CollectPathPrefixes(string resourcesPath, FileSystem fileSystem)
	{
		HashSet<string> result = new(StringComparer.OrdinalIgnoreCase);
		ScanForPathPrefixes(resourcesPath, fileSystem, result);
		return result;
	}

	private static void ScanForPathPrefixes(string directory, FileSystem fileSystem, HashSet<string> result)
	{
		foreach (string file in fileSystem.Directory.EnumerateFiles(directory, "*.asset"))
		{
			try
			{
				string content = fileSystem.File.ReadAllText(file);
				ExtractPathPrefixes(content, result);
			}
			catch
			{
			}
		}

		foreach (string subDir in fileSystem.Directory.EnumerateDirectories(directory))
		{
			ScanForPathPrefixes(subDir, fileSystem, result);
		}
	}

	private static void ExtractPathPrefixes(string content, HashSet<string> result)
	{
		string[] lines = content.Split('\n');
		foreach (string line in lines)
		{
			string trimmed = line.Trim();
			if (trimmed.StartsWith(PathPrefixYamlKey, StringComparison.Ordinal))
			{
				string value = trimmed.Substring(PathPrefixYamlKey.Length).Trim();
				if (!string.IsNullOrEmpty(value))
				{
					result.Add(value);
				}
			}
		}
	}

	private static bool TryRenameFolderToMatchCasing(string parentDirectory, string expectedFolderName, FileSystem fileSystem)
	{
		if (string.IsNullOrEmpty(expectedFolderName))
		{
			return false;
		}

		string? actualPath = FindFolderCaseInsensitive(parentDirectory, expectedFolderName, fileSystem);
		if (actualPath is null)
		{
			return false;
		}

		string actualName = fileSystem.Path.GetFileName(actualPath);
		if (string.Equals(actualName, expectedFolderName, StringComparison.Ordinal))
		{
			return false;
		}

		string expectedPath = fileSystem.Path.Join(parentDirectory, expectedFolderName);

		try
		{
			string tempPath = fileSystem.Path.Join(parentDirectory, expectedFolderName + "_temp_rename_" + Guid.NewGuid().ToString("N")[..8]);
			Directory.Move(actualPath, tempPath);
			Directory.Move(tempPath, expectedPath);
			Logger.Info(LogCategory.Export, $"Renamed resource folder '{actualName}' to '{expectedFolderName}' to match PathPrefix casing.");
			return true;
		}
		catch (Exception ex)
		{
			Logger.Warning(LogCategory.Export, $"Failed to rename resource folder '{actualName}' to '{expectedFolderName}': {ex.Message}");
			return false;
		}
	}

	private static string? FindFolderCaseInsensitive(string parentDirectory, string folderName, FileSystem fileSystem)
	{
		foreach (string dir in fileSystem.Directory.EnumerateDirectories(parentDirectory))
		{
			string name = fileSystem.Path.GetFileName(dir);
			if (string.Equals(name, folderName, StringComparison.OrdinalIgnoreCase))
			{
				return dir;
			}
		}
		return null;
	}
}
