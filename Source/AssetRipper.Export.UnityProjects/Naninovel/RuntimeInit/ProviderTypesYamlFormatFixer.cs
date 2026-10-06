using AssetRipper.Import.Logging;
using AssetRipper.Processing;
using System.IO;
using System.Text;

namespace AssetRipper.Export.UnityProjects.Naninovel.RuntimeInit;

/// <summary>
/// 修复 ProviderTypes 列表中 LocalResourceProvider 条目 YAML 缩进不一致问题，
/// 确保所有 provider 被 Naninovel ResourceProviderManager 识别为有效 provider。
/// </summary>
internal static class ProviderTypesYamlFormatFixer
{
	private const string ProviderTypesMarker = "ProviderTypes:";
	private const string ListItemPrefix = "- ";

	public static int Fix(string assetsPath, FileSystem fileSystem)
	{
		string configDir = fileSystem.Path.Join(assetsPath, "Resources", "naninovel", "configuration");
		if (!fileSystem.Directory.Exists(configDir))
		{
			Logger.Warning(LogCategory.Export, $"Configuration directory not found at {configDir}");
			return 0;
		}

		int modifiedCount = 0;
		try
		{
			foreach (string configPath in fileSystem.Directory.EnumerateFiles(configDir, "*.asset", SearchOption.TopDirectoryOnly))
			{
				try
				{
					string content = File.ReadAllText(configPath);
					if (!content.Contains(ProviderTypesMarker))
					{
						continue;
					}

					string modifiedContent = FixProviderTypesIndentation(content);
					if (modifiedContent == content)
					{
						continue;
					}

					File.WriteAllText(configPath, modifiedContent);
					modifiedCount++;
				}
				catch (Exception ex)
				{
					Logger.Warning(LogCategory.Export, $"Failed to fix ProviderTypes YAML format in {configPath}: {ex.Message}");
				}
			}
		}
		catch (Exception ex)
		{
			Logger.Warning(LogCategory.Export, $"Failed to enumerate configuration files: {ex.Message}");
			return 0;
		}

		if (modifiedCount > 0)
		{
			Logger.Info(LogCategory.Export, $"Fixed ProviderTypes YAML indentation in {modifiedCount} configuration file(s).");
		}
		return modifiedCount;
	}

	private static string FixProviderTypesIndentation(string content)
	{
		string[] lines = content.Split('\n');
		StringBuilder sb = new();
		bool modified = false;
		bool inProviderTypes = false;
		int baseIndent = -1;

		for (int i = 0; i < lines.Length; i++)
		{
			string trimmed = lines[i].TrimStart();

			if (trimmed.StartsWith(ProviderTypesMarker))
			{
				inProviderTypes = true;
				baseIndent = -1;
				sb.Append(lines[i]);
				if (i < lines.Length - 1)
				{
					sb.Append('\n');
				}
				continue;
			}

			if (inProviderTypes)
			{
				if (trimmed.StartsWith(ListItemPrefix))
				{
					int currentIndent = lines[i].Length - trimmed.Length;
					if (baseIndent == -1)
					{
						baseIndent = currentIndent;
					}
					else if (currentIndent > baseIndent)
					{
						string fixedLine = new string(' ', baseIndent) + trimmed;
						sb.Append(fixedLine);
						if (i < lines.Length - 1)
						{
							sb.Append('\n');
						}
						modified = true;
						continue;
					}

					sb.Append(lines[i]);
					if (i < lines.Length - 1)
					{
						sb.Append('\n');
					}
					continue;
				}

				if (!string.IsNullOrEmpty(trimmed))
				{
					inProviderTypes = false;
				}
			}

			sb.Append(lines[i]);
			if (i < lines.Length - 1)
			{
				sb.Append('\n');
			}
		}

		return modified ? sb.ToString() : content;
	}
}