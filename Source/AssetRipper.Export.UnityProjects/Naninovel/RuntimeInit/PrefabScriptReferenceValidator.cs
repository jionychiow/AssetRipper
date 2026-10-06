using AssetRipper.Import.Logging;
using AssetRipper.Processing;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace AssetRipper.Export.UnityProjects.Naninovel.RuntimeInit;

internal static class PrefabScriptReferenceValidator
{
	private static readonly Regex GuidRegex = new(@"guid:\s*([0-9a-fA-F]{32})", RegexOptions.Compiled);

	public static int Validate(string assetsPath, FileSystem fileSystem, params string[] relativePrefabPaths)
	{
		try
		{
			HashSet<string> validGuids = CollectValidGuids(assetsPath, fileSystem);
			int totalInvalid = 0;

			foreach (string relativePath in relativePrefabPaths)
			{
				string prefabPath = fileSystem.Path.Join(assetsPath, relativePath);
				if (!fileSystem.File.Exists(prefabPath))
				{
					Logger.Warning(LogCategory.Export, $"Prefab not found: {prefabPath}");
					continue;
				}

				HashSet<string> scriptGuids = ExtractScriptGuids(prefabPath);
				List<string> invalidGuids = CompareGuids(scriptGuids, validGuids);

				if (invalidGuids.Count > 0)
				{
					Logger.Warning(LogCategory.Export, $"Prefab '{relativePath}' has {invalidGuids.Count} invalid script GUID(s): {string.Join(", ", invalidGuids)}");
					totalInvalid += invalidGuids.Count;
				}
			}

			if (totalInvalid == 0)
			{
				Logger.Info(LogCategory.Export, "All prefab script references are valid.");
			}

			return totalInvalid;
		}
		catch (Exception ex)
		{
			Logger.Warning(LogCategory.Export, $"Failed to validate prefab script references: {ex.Message}");
			return -1;
		}
	}

	private static HashSet<string> ExtractScriptGuids(string prefabPath)
	{
		string content = File.ReadAllText(prefabPath);
		HashSet<string> guids = new(StringComparer.OrdinalIgnoreCase);

		foreach (Match match in GuidRegex.Matches(content))
		{
			guids.Add(match.Groups[1].Value);
		}

		return guids;
	}

	private static HashSet<string> CollectValidGuids(string assetsPath, FileSystem fileSystem)
	{
		HashSet<string> validGuids = new(StringComparer.OrdinalIgnoreCase);

		try
		{
			Queue<string> directories = new();
			directories.Enqueue(assetsPath);

			while (directories.Count > 0)
			{
				string currentDir = directories.Dequeue();
				foreach (string dir in fileSystem.Directory.EnumerateDirectories(currentDir))
				{
					directories.Enqueue(dir);
				}

				foreach (string filePath in fileSystem.Directory.EnumerateFiles(currentDir))
				{
					if (!filePath.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
					{
						continue;
					}

					string content = File.ReadAllText(filePath);
					Match match = GuidRegex.Match(content);
					if (match.Success)
					{
						validGuids.Add(match.Groups[1].Value);
					}
				}
			}
		}
		catch (Exception ex)
		{
			Logger.Warning(LogCategory.Export, $"Failed to collect valid GUIDs: {ex.Message}");
		}

		return validGuids;
	}

	private static List<string> CompareGuids(HashSet<string> scriptGuids, HashSet<string> validGuids)
	{
		List<string> invalid = new();
		foreach (string guid in scriptGuids)
		{
			if (!validGuids.Contains(guid))
			{
				invalid.Add(guid);
			}
		}
		return invalid;
	}
}