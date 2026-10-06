using AssetRipper.Import.Logging;
using AssetRipper.Processing;
using System.IO;

namespace AssetRipper.Export.UnityProjects.Naninovel.RuntimeInit;

/// <summary>
/// AsyncExceptionLogType=0 会静默吞没所有异步异常，导致资源加载失败不可诊断。
/// 设为 1（Normal）使异常被记录到 Editor.log。
/// </summary>
internal static class AsyncExceptionLogTypeFixer
{
	private const string TargetFileName = "EngineConfiguration.asset";
	private const string TargetLinePrefix = "AsyncExceptionLogType:";

	public static int Fix(string assetsPath, FileSystem fileSystem)
	{
		string configPath = fileSystem.Path.Join(assetsPath, "Resources", "naninovel", "configuration", TargetFileName);
		if (!fileSystem.File.Exists(configPath))
		{
			Logger.Warning(LogCategory.Export, $"EngineConfiguration.asset not found at {configPath}");
			return 0;
		}

		try
		{
			string content = File.ReadAllText(configPath);
			string[] lines = content.Split('\n');
			bool modified = false;

			for (int i = 0; i < lines.Length; i++)
			{
				string trimmed = lines[i].TrimStart();
				if (!trimmed.StartsWith(TargetLinePrefix))
				{
					continue;
				}

				string valueStr = trimmed[TargetLinePrefix.Length..].Trim();
				if (int.TryParse(valueStr, out int currentValue) && currentValue == 0)
				{
					string indent = lines[i][..lines[i].IndexOf(TargetLinePrefix)];
					lines[i] = $"{indent}{TargetLinePrefix} 1";
					modified = true;
					Logger.Info(LogCategory.Export, $"AsyncExceptionLogType changed from 0 to 1 (Normal) in EngineConfiguration.asset");
				}
				break;
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
			Logger.Warning(LogCategory.Export, $"Failed to fix AsyncExceptionLogType: {ex.Message}");
			return 0;
		}
	}
}