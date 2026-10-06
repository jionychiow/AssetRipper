using AssetRipper.Export.UnityProjects.Project;
using AssetRipper.Import.Logging;
using AssetRipper.Processing;
using System.Collections.Generic;

namespace AssetRipper.Export.UnityProjects.Naninovel.RuntimeInit;

internal static class TextPrinterResourceRegistrar
{
	private const string TextPrintersFolderName = "TextPrinters";
	private const string UnityCommonFolderName = "UnityCommon";
	private const string ProjectResourcesFileName = "ProjectResources.asset";
	private const string GameObjectType = "UnityEngine.GameObject, UnityEngine.CoreModule, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null";

	public static int Register(string assetsPath, FileSystem fileSystem)
	{
		string resourcesPath = fileSystem.Path.Join(assetsPath, "Resources");
		string textPrintersPath = fileSystem.Path.Join(resourcesPath, TextPrintersFolderName);
		if (!fileSystem.Directory.Exists(textPrintersPath))
		{
			Logger.Info(LogCategory.Export, $"TextPrinters directory not found at {textPrintersPath}, skipping registration.");
			return 0;
		}

		string projectResourcesPath = fileSystem.Path.Join(resourcesPath, UnityCommonFolderName, ProjectResourcesFileName);
		if (!fileSystem.File.Exists(projectResourcesPath))
		{
			Logger.Warning(LogCategory.Export, $"ProjectResources.asset not found at {projectResourcesPath}, cannot register TextPrinters resources.");
			return 0;
		}

		try
		{
			string existingContent = fileSystem.File.ReadAllText(projectResourcesPath);
			HashSet<string> existingPaths = new(StringComparer.Ordinal);
			NaninovelResourcePatcherPostExporter.ParseExistingResourcePaths(existingContent, existingPaths);

			List<(string Path, string Type)> newEntries = new();
			List<string> prefabPaths = ScanPrefabPaths(textPrintersPath, resourcesPath, fileSystem);

			foreach (string resourcePath in prefabPaths)
			{
				if (existingPaths.Contains(resourcePath))
				{
					continue;
				}

				newEntries.Add((resourcePath, GameObjectType));
				existingPaths.Add(resourcePath);
			}

			if (newEntries.Count == 0)
			{
				Logger.Info(LogCategory.Export, "No new TextPrinters resources to register.");
				return 0;
			}

			NaninovelResourcePatcherPostExporter.AppendResourceEntries(projectResourcesPath, newEntries, fileSystem);
			Logger.Info(LogCategory.Export, $"Registered {newEntries.Count} TextPrinters resource paths into ProjectResources.asset.");
			return newEntries.Count;
		}
		catch (Exception ex)
		{
			Logger.Warning(LogCategory.Export, $"Failed to register TextPrinters resources: {ex.Message}");
			return 0;
		}
	}

	private static List<string> ScanPrefabPaths(string textPrintersDir, string resourcesDir, FileSystem fileSystem)
	{
		List<string> result = new();
		Queue<string> directories = new();
		directories.Enqueue(textPrintersDir);

		while (directories.Count > 0)
		{
			string currentDir = directories.Dequeue();
			foreach (string dir in fileSystem.Directory.EnumerateDirectories(currentDir))
			{
				directories.Enqueue(dir);
			}

			foreach (string filePath in fileSystem.Directory.EnumerateFiles(currentDir))
			{
				string extension = fileSystem.Path.GetExtension(filePath).ToLowerInvariant();
				if (extension != ".prefab")
				{
					continue;
				}

				string relativePath = fileSystem.Path.GetRelativePath(resourcesDir, filePath);
				string resourcePath = relativePath.Substring(0, relativePath.Length - extension.Length).Replace('\\', '/');
				result.Add(resourcePath);
			}
		}

		return result;
	}
}