using AssetRipper.Import.Logging;
using AssetRipper.Processing;
using System.IO;
using System.Text;

namespace AssetRipper.Export.UnityProjects.Naninovel.RuntimeInit;

internal static class ProjectResourcePathNormalizer
{
	private const string UnityCommonFolderName = "UnityCommon";
	private const string ProjectResourcesFileName = "ProjectResources.asset";
	private const string PathPrefix = "  - Path: ";

	public static int Normalize(string assetsPath, FileSystem fileSystem)
	{
		string projectResourcesPath = fileSystem.Path.Join(assetsPath, "Resources", UnityCommonFolderName, ProjectResourcesFileName);
		if (!fileSystem.File.Exists(projectResourcesPath))
		{
			Logger.Warning(LogCategory.Export, $"ProjectResources.asset not found at {projectResourcesPath}");
			return 0;
		}

		try
		{
			string content = File.ReadAllText(projectResourcesPath);
			string[] lines = content.Split('\n');
			int normalizedCount = 0;

			for (int i = 0; i < lines.Length; i++)
			{
				string trimmed = lines[i].TrimStart();
				if (!trimmed.StartsWith(PathPrefix.TrimStart()))
				{
					continue;
				}

				int prefixIndex = lines[i].IndexOf(PathPrefix);
				if (prefixIndex < 0)
				{
					continue;
				}

				string originalPath = lines[i][(prefixIndex + PathPrefix.Length)..];
				string normalizedPath = NormalizePath(originalPath);

				if (normalizedPath != originalPath)
				{
					lines[i] = $"{lines[i][..(prefixIndex + PathPrefix.Length)]}{normalizedPath}";
					normalizedCount++;
				}
			}

			if (normalizedCount > 0)
			{
				File.WriteAllText(projectResourcesPath, string.Join('\n', lines));
				Logger.Info(LogCategory.Export, $"Normalized {normalizedCount} resource paths in ProjectResources.asset.");
			}

			return normalizedCount;
		}
		catch (Exception ex)
		{
			Logger.Warning(LogCategory.Export, $"Failed to normalize ProjectResources paths: {ex.Message}");
			return 0;
		}
	}

	private static string NormalizePath(string path)
	{
		path = path.Trim();
		path = path.Replace('\\', '/');
		path = path.TrimStart('/');
		return path;
	}
}