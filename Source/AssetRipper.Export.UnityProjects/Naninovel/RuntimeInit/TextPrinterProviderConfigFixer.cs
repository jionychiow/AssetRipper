using AssetRipper.Import.Logging;
using AssetRipper.Processing;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace AssetRipper.Export.UnityProjects.Naninovel.RuntimeInit;

internal static class TextPrinterProviderConfigFixer
{
	private const string LocalResourceProviderMarker = "Naninovel.LocalResourceProvider";
	private const string LocalResourceProviderFullType = "Naninovel.LocalResourceProvider, Elringus.Naninovel.Runtime, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null";

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
					if (!content.Contains("ProviderTypes"))
					{
						continue;
					}

					if (content.Contains(LocalResourceProviderMarker))
					{
						continue;
					}

					string modifiedContent = AddLocalResourceProviderToAllProviderTypes(content);
					if (modifiedContent == content)
					{
						continue;
					}

					File.WriteAllText(configPath, modifiedContent);
					modifiedCount++;
				}
				catch (Exception ex)
				{
					Logger.Warning(LogCategory.Export, $"Failed to fix provider config in {configPath}: {ex.Message}");
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
			Logger.Info(LogCategory.Export, $"Added LocalResourceProvider fallback to {modifiedCount} configuration file(s).");
		}
		return modifiedCount;
	}

	private static string AddLocalResourceProviderToAllProviderTypes(string content)
	{
		string[] lines = content.Split('\n');
		StringBuilder sb = new();
		bool modified = false;
		bool inProviderTypes = false;
		int providerTypeIndent = -1;

		for (int i = 0; i < lines.Length; i++)
		{
			string trimmed = lines[i].TrimStart();

			if (trimmed.StartsWith("ProviderTypes:"))
			{
				inProviderTypes = true;
				providerTypeIndent = lines[i].IndexOf("ProviderTypes:");
				sb.Append(lines[i]);
				if (i < lines.Length - 1)
				{
					sb.Append('\n');
				}
				continue;
			}

			if (inProviderTypes)
			{
				if (trimmed.StartsWith("- "))
				{
					sb.Append(lines[i]);
					if (i < lines.Length - 1)
					{
						sb.Append('\n');
					}
					continue;
				}

				if (!string.IsNullOrEmpty(trimmed))
				{
					// 缩进必须与 ProviderTypes: 同级（providerTypeIndent），而非 providerTypeIndent + 2，否则 YAML 解析为子列表项
					string indent = new string(' ', providerTypeIndent);
					sb.Append(indent).Append("- ").Append(LocalResourceProviderFullType);
					sb.Append('\n');
					modified = true;
					inProviderTypes = false;

					sb.Append(lines[i]);
					if (i < lines.Length - 1)
					{
						sb.Append('\n');
					}
					continue;
				}

				if (i == lines.Length - 1 && string.IsNullOrEmpty(trimmed))
				{
					string indent = new string(' ', providerTypeIndent);
					sb.Append(indent).Append("- ").Append(LocalResourceProviderFullType);
					sb.Append('\n');
					modified = true;
					inProviderTypes = false;
					continue;
				}
			}

			sb.Append(lines[i]);
			if (i < lines.Length - 1)
			{
				sb.Append('\n');
			}
		}

		if (inProviderTypes && !modified)
		{
			string indent = new string(' ', providerTypeIndent);
			sb.Append(indent).Append("- ").Append(LocalResourceProviderFullType);
			modified = true;
		}

		return modified ? sb.ToString() : content;
	}
}
