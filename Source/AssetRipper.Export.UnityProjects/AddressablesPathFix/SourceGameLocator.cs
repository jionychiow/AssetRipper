using AssetRipper.Export.Configuration;
using AssetRipper.Import.Logging;

namespace AssetRipper.Export.UnityProjects.AddressablesPathFix;

public static class SourceGameLocator
{
	public static string? LocateResources(FullConfiguration settings, FileSystem fileSystem)
	{
		string sourceDataPath = settings.SourceDataPath;
		if (string.IsNullOrEmpty(sourceDataPath))
		{
			Logger.Warning(LogCategory.Export, "SourceDataPath 为空，无法定位原始游戏 Resources 目录");
			return null;
		}

		List<string> candidates = new()
		{
			fileSystem.Path.Join(sourceDataPath, "Resources"),
			fileSystem.Path.Join(sourceDataPath, "TomieWGM_Data", "Resources"),
		};

		string? parentDir = fileSystem.Path.GetDirectoryName(sourceDataPath);
		if (parentDir is not null)
		{
			candidates.Add(fileSystem.Path.Join(parentDir, "TomieWGM_Data", "Resources"));
		}

		foreach (string candidate in candidates)
		{
			if (fileSystem.Directory.Exists(candidate))
			{
				bool hasSubDirs = false;
				try
				{
					foreach (string _ in fileSystem.Directory.EnumerateDirectories(candidate))
					{
						hasSubDirs = true;
						break;
					}
				}
				catch { }

				if (hasSubDirs)
				{
					Logger.Info(LogCategory.Export, $"定位到原始游戏 Resources 目录: {candidate}");
					return candidate;
				}
			}
		}

		Logger.Warning(LogCategory.Export, $"未找到原始游戏 Resources 目录，候选: {string.Join(", ", candidates)}");
		return null;
	}
}