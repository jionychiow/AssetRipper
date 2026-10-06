using AssetRipper.Import.Logging;
using AssetRipper.Processing;
using System.IO;
using System.Text;

namespace AssetRipper.Export.UnityProjects.Naninovel.RuntimeInit;

/// <summary>
/// 将 ProjectResources.asset 中资源路径大小写调整为文件系统实际大小写，
/// 确保 Resources.Load 在所有平台（含大小写敏感文件系统）正常工作。
/// </summary>
internal static class ProjectResourcePathFilesystemCaseValidator
{
	private const string ProjectResourcesFileName = "ProjectResources.asset";
	private const string PathLinePrefix = "Path:";

	public static int Validate(string assetsPath, FileSystem fileSystem)
	{
		string projectResourcesPath = fileSystem.Path.Join(assetsPath, "Resources", "UnityCommon", ProjectResourcesFileName);
		if (!fileSystem.File.Exists(projectResourcesPath))
		{
			Logger.Warning(LogCategory.Export, $"ProjectResources.asset not found at {projectResourcesPath}");
			return 0;
		}

		string resourcesRoot = fileSystem.Path.Join(assetsPath, "Resources");

		try
		{
			string content = File.ReadAllText(projectResourcesPath);
			string[] lines = content.Split('\n');
			int modifiedCount = 0;

			for (int i = 0; i < lines.Length; i++)
			{
				string trimmed = lines[i].TrimStart();
				if (!trimmed.StartsWith(PathLinePrefix))
				{
					continue;
				}

				string registeredPath = trimmed[PathLinePrefix.Length..].Trim();
				if (string.IsNullOrEmpty(registeredPath))
				{
					continue;
				}

				string filesystemPath = FindFilesystemCase(resourcesRoot, registeredPath);
				if (filesystemPath == null || filesystemPath == registeredPath)
				{
					continue;
				}

				string indent = lines[i][..lines[i].IndexOf(PathLinePrefix)];
				lines[i] = $"{indent}{PathLinePrefix} {filesystemPath}";
				modifiedCount++;
			}

			if (modifiedCount > 0)
			{
				File.WriteAllText(projectResourcesPath, string.Join('\n', lines));
				Logger.Info(LogCategory.Export, $"Fixed {modifiedCount} resource path(s) to match filesystem case in ProjectResources.asset");
			}

			return modifiedCount;
		}
		catch (Exception ex)
		{
			Logger.Warning(LogCategory.Export, $"Failed to validate ProjectResources.asset paths: {ex.Message}");
			return 0;
		}
	}

	private static string FindFilesystemCase(string resourcesRoot, string registeredPath)
	{
		try
		{
			string[] parts = registeredPath.Replace('\\', '/').Split('/');
			string currentPath = resourcesRoot;

			for (int i = 0; i < parts.Length; i++)
			{
				if (string.IsNullOrEmpty(parts[i]))
				{
					continue;
				}

				if (i == parts.Length - 1)
				{
					int dotIndex = parts[i].LastIndexOf('.');
					string nameWithoutExt = dotIndex >= 0 ? parts[i][..dotIndex] : parts[i];
					string ext = dotIndex >= 0 ? parts[i][dotIndex..] : "";

					string foundFile = FindEntryCaseInsensitive(currentPath, nameWithoutExt + ext, true);
					if (foundFile == null)
					{
						return null;
					}
					currentPath = Path.Join(currentPath, foundFile);
				}
				else
				{
					string foundDir = FindEntryCaseInsensitive(currentPath, parts[i], false);
					if (foundDir == null)
					{
						return null;
					}
					currentPath = Path.Join(currentPath, foundDir);
				}
			}

			string fullPath = currentPath;
			if (!fullPath.StartsWith(resourcesRoot))
			{
				return null;
			}

			string relativePath = fullPath[resourcesRoot.Length..].Replace('\\', '/').TrimStart('/');
			return relativePath;
		}
		catch
		{
			return null;
		}
	}

	private static string FindEntryCaseInsensitive(string dir, string targetName, bool isFile)
	{
		try
		{
			if (isFile)
			{
				foreach (string filePath in Directory.EnumerateFiles(dir))
				{
					string fileName = Path.GetFileName(filePath);
					if (string.Equals(fileName, targetName, StringComparison.OrdinalIgnoreCase))
					{
						return fileName;
					}
				}
			}
			else
			{
				foreach (string dirPath in Directory.EnumerateDirectories(dir))
				{
					string dirName = Path.GetFileName(dirPath);
					if (string.Equals(dirName, targetName, StringComparison.OrdinalIgnoreCase))
					{
						return dirName;
					}
				}
			}
		}
		catch { }
		return null;
	}
}