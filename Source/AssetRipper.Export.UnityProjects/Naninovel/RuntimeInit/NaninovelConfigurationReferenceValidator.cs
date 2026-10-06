using AssetRipper.Export.Configuration;
using AssetRipper.Import.Logging;
using AssetRipper.Processing;
using System.IO;
using System.Text.RegularExpressions;

namespace AssetRipper.Export.UnityProjects.Naninovel.RuntimeInit;

internal static class NaninovelConfigurationReferenceValidator
{
	private static readonly string[] ReferenceFieldNames =
	[
		"CustomCameraPrefab",
		"CustomUICameraPrefab",
		"CustomInitializationUI",
		"CustomTextureShader",
		"CustomSpriteShader",
		"RenderTexture",
		"EditorCustomStyleSheet",
		"GraphCustomStyleSheet",
	];

	private static readonly Regex ReferenceLineRegex = new(
		@"^(\s*)(\w+):\s*\{fileID:\s*(\d+),\s*guid:\s*([0-9a-fA-F]+),\s*type:\s*(\d+)\}",
		RegexOptions.Compiled);

	public static int FixInvalidReferences(string assetsPath, FileSystem fileSystem)
	{
		string configDir = fileSystem.Path.Join(assetsPath, "Resources", "naninovel", "configuration");
		if (!fileSystem.Directory.Exists(configDir))
		{
			return 0;
		}

		HashSet<string> allGuids = CollectAllGuids(assetsPath, fileSystem);

		int fixedCount = 0;
		foreach (string assetFile in fileSystem.Directory.EnumerateFiles(configDir, "*.asset", SearchOption.TopDirectoryOnly))
		{
			string fileName = Path.GetFileName(assetFile);
			if (fileName.Contains('_'))
			{
				continue;
			}

			try
			{
				string content = File.ReadAllText(assetFile);
				string modified = FixReferencesInContent(content, allGuids, out int count);
				if (count > 0)
				{
					File.WriteAllText(assetFile, modified);
					fixedCount += count;
					Logger.Info(LogCategory.Export, $"Fixed {count} invalid references in {fileName}");
				}
			}
			catch (Exception ex)
			{
				Logger.Warning(LogCategory.Export, $"Failed to fix references in {assetFile}: {ex.Message}");
			}
		}

		return fixedCount;
	}

	private static HashSet<string> CollectAllGuids(string assetsPath, FileSystem fileSystem)
	{
		HashSet<string> guids = [];
		try
		{
			foreach (string metaFile in fileSystem.Directory.EnumerateFiles(assetsPath, "*.meta", SearchOption.AllDirectories))
			{
				try
				{
					string content = File.ReadAllText(metaFile);
					int guidIndex = content.IndexOf("guid: ", StringComparison.Ordinal);
					if (guidIndex >= 0)
					{
						int start = guidIndex + 6;
						int end = start;
						while (end < content.Length && content[end] != '\n' && content[end] != '\r' && content[end] != ' ')
						{
							end++;
						}
						if (end > start)
						{
							guids.Add(content[start..end]);
						}
					}
				}
				catch
				{
				}
			}
		}
		catch (Exception ex)
		{
			Logger.Warning(LogCategory.Export, $"Failed to collect GUIDs: {ex.Message}");
		}
		return guids;
	}

	private static string FixReferencesInContent(string content, HashSet<string> validGuids, out int fixedCount)
	{
		fixedCount = 0;
		string[] lines = content.Split('\n');
		for (int i = 0; i < lines.Length; i++)
		{
			Match match = ReferenceLineRegex.Match(lines[i]);
			if (!match.Success)
			{
				continue;
			}

			string fieldName = match.Groups[2].Value;
			string guid = match.Groups[4].Value;
			string fileID = match.Groups[3].Value;

			if (!ReferenceFieldNames.Contains(fieldName))
			{
				continue;
			}

			if (fileID == "0")
			{
				continue;
			}

			if (validGuids.Contains(guid))
			{
				continue;
			}

			string indent = match.Groups[1].Value;
			lines[i] = $"{indent}{fieldName}: {{fileID: 0}}";
			fixedCount++;
			Logger.Info(LogCategory.Export, $"  Nullified invalid reference: {fieldName} (guid: {guid})");
		}
		return string.Join('\n', lines);
	}
}