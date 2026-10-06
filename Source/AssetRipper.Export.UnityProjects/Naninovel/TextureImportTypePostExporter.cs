using AssetRipper.Export.Configuration;
using AssetRipper.Import.Logging;
using AssetRipper.Processing;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace AssetRipper.Export.UnityProjects.Naninovel;

public sealed class TextureImportTypePostExporter : IPostExporter
{
	private const string UnityCommonFolder = "UnityCommon";
	private const string ProjectResourcesFile = "ProjectResources.asset";
	private const string ResourcesFolder = "Resources";
	private const string Texture2dType = "UnityEngine.Texture2D";
	private static readonly Regex PathLineRegex = new(@"^\s*- Path:\s*(.+)$", RegexOptions.Compiled);
	private static readonly Regex TypeLineRegex = new(@"^\s*Type:\s*(.+)$", RegexOptions.Compiled);
	private static readonly Regex TextureTypeRegex = new(@"^(\s*textureType:\s*)\d+\s*$", RegexOptions.Compiled | RegexOptions.Multiline);
	private static readonly Regex SpriteModeRegex = new(@"^(\s*spriteMode:\s*)\d+\s*$", RegexOptions.Compiled | RegexOptions.Multiline);


	public void DoPostExport(GameData gameData, FullConfiguration settings, FileSystem fileSystem)
	{
		Logger.Info(LogCategory.Export, "TextureImportTypePostExporter: setting texture import types to Sprite for Naninovel resource compatibility...");

		string projectResourcesPath = fileSystem.Path.Join(settings.AssetsPath, ResourcesFolder, UnityCommonFolder, ProjectResourcesFile);
		if (!fileSystem.File.Exists(projectResourcesPath))
		{
			Logger.Info(LogCategory.Export, $"ProjectResources.asset not found at: {projectResourcesPath}");
			return;
		}

		HashSet<string> texture2dPaths = ExtractTexture2DPaths(projectResourcesPath, fileSystem);
		Logger.Info(LogCategory.Export, $"TextureImportTypePostExporter: found {texture2dPaths.Count} Texture2D paths in ProjectResources.asset.");

		if (texture2dPaths.Count == 0)
		{
			Logger.Info(LogCategory.Export, "TextureImportTypePostExporter: no Texture2D paths found, skipping.");
			return;
		}

		int fixedCount = 0;
		int skippedCount = 0;
		int notFoundCount = 0;
		string resourcesRoot = fileSystem.Path.Join(settings.AssetsPath, ResourcesFolder);

		foreach (string resourcePath in texture2dPaths)
		{
			string[] candidates = BuildCandidateExtensions(resourcePath);
			bool found = false;

			foreach (string ext in candidates)
			{
				string metaPath = fileSystem.Path.Join(resourcesRoot, resourcePath + ext + ".meta");
				if (fileSystem.File.Exists(metaPath))
				{
					if (FixTextureMetaFile(metaPath, fileSystem))
					{
						fixedCount++;
					}
					else
					{
						skippedCount++;
					}
					found = true;
					break;
				}
			}

			if (!found)
			{
				string metaPath = fileSystem.Path.Join(resourcesRoot, resourcePath + ".meta");
				if (fileSystem.File.Exists(metaPath))
				{
					if (FixTextureMetaFile(metaPath, fileSystem))
					{
						fixedCount++;
					}
					else
					{
						skippedCount++;
					}
					found = true;
				}
			}

			if (!found)
			{
				notFoundCount++;
			}
		}

		Logger.Info(LogCategory.Export, $"TextureImportTypePostExporter: fixed {fixedCount} texture .meta files (skipped {skippedCount} already correct, {notFoundCount} not found).");
	}

	private static HashSet<string> ExtractTexture2DPaths(string projectResourcesPath, FileSystem fileSystem)
	{
		HashSet<string> result = new(StringComparer.OrdinalIgnoreCase);
		string content = fileSystem.File.ReadAllText(projectResourcesPath);
		string[] lines = content.Split('\n');

		for (int i = 0; i < lines.Length - 1; i++)
		{
			Match pathMatch = PathLineRegex.Match(lines[i]);
			if (!pathMatch.Success)
			{
				continue;
			}

			Match typeMatch = TypeLineRegex.Match(lines[i + 1]);
			if (!typeMatch.Success)
			{
				continue;
			}

			string pathValue = pathMatch.Groups[1].Value.Trim();
			string typeValue = typeMatch.Groups[1].Value.Trim();

			if (typeValue.StartsWith(Texture2dType, StringComparison.Ordinal))
			{
				result.Add(pathValue);
			}
		}

		return result;
	}

	private static string[] BuildCandidateExtensions(string resourcePath)
	{
		return [".png", ".jpg", ".jpeg", ".tga", ".tif", ".tiff", ".bmp", ".exr", ".psd"];
	}

	private static bool FixTextureMetaFile(string metaPath, FileSystem fileSystem)
	{
		string content = fileSystem.File.ReadAllText(metaPath);

		string newContent = content;
		newContent = TextureTypeRegex.Replace(newContent, "${1}8");
		newContent = SpriteModeRegex.Replace(newContent, "${1}1");

		if (newContent == content)
		{
			return false;
		}

		fileSystem.File.WriteAllText(metaPath, newContent);
		return true;
	}
}