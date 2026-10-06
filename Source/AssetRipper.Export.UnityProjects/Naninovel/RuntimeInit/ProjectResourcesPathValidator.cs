using AssetRipper.Export.Configuration;
using AssetRipper.Import.Logging;
using AssetRipper.Processing;
using System.IO;

namespace AssetRipper.Export.UnityProjects.Naninovel.RuntimeInit;

internal static class ProjectResourcesPathValidator
{
	private static readonly string[] CommonExtensions =
	[
		"", ".png", ".jpg", ".prefab", ".asset", ".nani", ".tga", ".wav", ".webm",
		".mp4", ".txt", ".bytes", ".json", ".xml", ".mat", ".shader", ".fbx", ".ogg",
	];

	public static int CleanMissingPaths(string assetsPath, FileSystem fileSystem)
	{
		string projectResourcesPath = fileSystem.Path.Join(assetsPath, "Resources", "UnityCommon", "ProjectResources.asset");
		if (!fileSystem.File.Exists(projectResourcesPath))
		{
			Logger.Warning(LogCategory.Export, $"ProjectResources.asset not found at {projectResourcesPath}");
			return 0;
		}

		try
		{
			string content = File.ReadAllText(projectResourcesPath);
			string resourcesDir = fileSystem.Path.Join(assetsPath, "Resources");

			List<(int pathLineIndex, int typeLineIndex, string path)> invalidEntries = [];
			string[] lines = content.Split('\n');

			for (int i = 0; i < lines.Length - 1; i++)
			{
				if (!lines[i].Contains("Path: "))
				{
					continue;
				}

				int pathStart = lines[i].IndexOf("Path: ") + 6;
				string path = lines[i][pathStart..].Trim();
				if (string.IsNullOrEmpty(path))
				{
					continue;
				}

				if (i + 1 < lines.Length && lines[i + 1].Contains("Type: "))
				{
					if (!ValidateResourcePath(resourcesDir, path))
					{
						invalidEntries.Add((i, i + 1, path));
					}
				}
			}

			if (invalidEntries.Count == 0)
			{
				Logger.Info(LogCategory.Export, "ProjectResources.asset: all resource paths valid");
				return 0;
			}

			HashSet<int> linesToRemove = [];
			foreach (var entry in invalidEntries)
			{
				linesToRemove.Add(entry.pathLineIndex);
				linesToRemove.Add(entry.typeLineIndex);
				Logger.Info(LogCategory.Export, $"  Removing invalid path: {entry.path}");
			}

			string[] remainingLines = lines
				.Select((line, index) => (line, index))
				.Where(x => !linesToRemove.Contains(x.index))
				.Select(x => x.line)
				.ToArray();

			File.WriteAllText(projectResourcesPath, string.Join('\n', remainingLines));
			Logger.Info(LogCategory.Export, $"ProjectResources.asset: removed {invalidEntries.Count} invalid resource paths");
			return invalidEntries.Count;
		}
		catch (Exception ex)
		{
			Logger.Warning(LogCategory.Export, $"Failed to clean ProjectResources paths: {ex.Message}");
			return 0;
		}
	}

	private static bool ValidateResourcePath(string resourcesDir, string path)
	{
		foreach (string ext in CommonExtensions)
		{
			string fullPath = Path.Join(resourcesDir, path + ext);
			if (File.Exists(fullPath))
			{
				return true;
			}
		}
		return false;
	}
}