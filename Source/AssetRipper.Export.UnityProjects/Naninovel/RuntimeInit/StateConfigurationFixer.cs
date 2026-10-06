using AssetRipper.Import.Logging;
using AssetRipper.Processing;
using System.IO;

namespace AssetRipper.Export.UnityProjects.Naninovel.RuntimeInit;

internal static class StateConfigurationFixer
{
	private const string StateConfigFileName = "StateConfiguration.asset";
	private const string BinarySaveFilesKey = "BinarySaveFiles:";
	private const string BinarySaveFilesTargetValue = "0";

	public static int Fix(string assetsPath, FileSystem fileSystem)
	{
		string configPath = fileSystem.Path.Join(assetsPath, "Resources", "naninovel", "configuration", StateConfigFileName);
		if (!fileSystem.File.Exists(configPath))
		{
			Logger.Warning(LogCategory.Export, $"StateConfigurationFixer: {StateConfigFileName} not found at {configPath}");
			return 0;
		}

		try
		{
			string content = File.ReadAllText(configPath);
			string[] lines = content.Split('\n');
			bool modified = false;

			for (int i = 0; i < lines.Length; i++)
			{
				string trimmed = lines[i].Trim();
				if (trimmed.StartsWith(BinarySaveFilesKey))
				{
					string currentValue = trimmed[BinarySaveFilesKey.Length..].Trim();
					if (currentValue != BinarySaveFilesTargetValue)
					{
						string indent = lines[i][..lines[i].IndexOf(BinarySaveFilesKey)];
						lines[i] = $"{indent}{BinarySaveFilesKey} {BinarySaveFilesTargetValue}";
						modified = true;
						Logger.Info(LogCategory.Export, $"StateConfigurationFixer: BinarySaveFiles {currentValue} -> {BinarySaveFilesTargetValue}");
					}
					break;
				}
			}

			if (modified)
			{
				File.WriteAllText(configPath, string.Join('\n', lines));
				return 1;
			}

			return 0;
		}
		catch (Exception ex)
		{
			Logger.Warning(LogCategory.Export, $"StateConfigurationFixer failed: {ex.Message}");
			return 0;
		}
	}
}