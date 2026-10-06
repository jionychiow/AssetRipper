using System.Text.RegularExpressions;
using AssetRipper.Export.UnityProjects.AddressablesPathFix.Models;
using AssetRipper.Import.Logging;

namespace AssetRipper.Export.UnityProjects.AddressablesPathFix.Parsers;

public sealed class NaninovelConfigReader
{
	private const string PathPrefixKey = "PathPrefix:";
	private const string MNameKey = "m_Name:";
	private const string UseAddressablesKey = "UseAddressables:";
	private const string ProviderTypesKey = "ProviderTypes:";
	private const string IdsKey = "ids:";

	public NaninovelConfig Read(string configurationDirectory, FileSystem fileSystem)
	{
		if (!fileSystem.Directory.Exists(configurationDirectory))
		{
			throw new DirectoryNotFoundException($"Naninovel configuration directory not found: {configurationDirectory}");
		}

		List<NaninovelModuleConfig> modules = new(24);
		bool useAddressables = false;

		IEnumerable<string> assetFiles = fileSystem.Directory.EnumerateFiles(configurationDirectory, "*.asset");
		foreach (string filePath in assetFiles)
		{
			try
			{
				string content = fileSystem.File.ReadAllText(filePath);
				NaninovelModuleConfig? module = ParseSingleConfig(filePath, content, fileSystem);
				if (module is not null)
				{
					modules.Add(module);
				}

				if (fileSystem.Path.GetFileName(filePath) == "ResourceProviderConfiguration.asset")
				{
					useAddressables = ReadUseAddressables(content);
				}
			}
			catch (Exception ex)
			{
				Logger.Warning(LogCategory.Export, $"Failed to parse Naninovel config {filePath}: {ex.Message}");
			}
		}

		HashSet<string> pathPrefixes = new(StringComparer.OrdinalIgnoreCase);
		foreach (NaninovelModuleConfig module in modules)
		{
			foreach (string prefix in module.AllPathPrefixes)
			{
				pathPrefixes.Add(prefix);
			}
		}

		if (useAddressables)
		{
			Logger.Warning(LogCategory.Export, "UseAddressables=1，路径修复可能无效，Naninovel 将使用 Addressables 加载资源");
		}

		Logger.Info(LogCategory.Export, $"Naninovel 配置解析完成: {modules.Count} 个模块, {pathPrefixes.Count} 个 PathPrefix");

		return new NaninovelConfig
		{
			Modules = modules,
			PathPrefixes = pathPrefixes,
			UseAddressables = useAddressables,
		};
	}

	private static NaninovelModuleConfig? ParseSingleConfig(string filePath, string content, FileSystem fileSystem)
	{
		string moduleName = ExtractValue(content, MNameKey) ?? fileSystem.Path.GetFileNameWithoutExtension(filePath);
		string defaultPathPrefix = ExtractValue(content, PathPrefixKey) ?? string.Empty;
		List<string> providerTypes = ExtractListItems(content, ProviderTypesKey);
		List<string> metadataIds = ExtractListItems(content, IdsKey);
		List<string> metadataPathPrefixes = ExtractAllPathPrefixes(content);

		return new NaninovelModuleConfig
		{
			ModuleName = moduleName,
			DefaultPathPrefix = defaultPathPrefix,
			ProviderTypes = providerTypes,
			MetadataIds = metadataIds,
			MetadataPathPrefixes = metadataPathPrefixes,
		};
	}

	private static bool ReadUseAddressables(string content)
	{
		string? value = ExtractValue(content, UseAddressablesKey);
		if (value is not null && value.Trim() == "1")
		{
			return true;
		}
		return false;
	}

	private static string? ExtractValue(string content, string key)
	{
		int keyIndex = content.IndexOf(key, StringComparison.Ordinal);
		if (keyIndex < 0)
		{
			return null;
		}

		int valueStart = keyIndex + key.Length;
		int lineEnd = content.IndexOf('\n', valueStart);
		if (lineEnd < 0)
		{
			lineEnd = content.Length;
		}

		return content[valueStart..lineEnd].Trim();
	}

	private static List<string> ExtractListItems(string content, string key)
	{
		List<string> items = new();
		int keyIndex = content.IndexOf(key, StringComparison.Ordinal);
		if (keyIndex < 0)
		{
			return items;
		}

		int lineEnd = content.IndexOf('\n', keyIndex);
		if (lineEnd < 0)
		{
			return items;
		}

		int currentPos = lineEnd + 1;
		while (currentPos < content.Length)
		{
			int nextLineEnd = content.IndexOf('\n', currentPos);
			if (nextLineEnd < 0)
			{
				nextLineEnd = content.Length;
			}

			string line = content[currentPos..nextLineEnd];
			string trimmed = line.TrimStart();

			if (trimmed.StartsWith('-'))
			{
				string item = trimmed[1..].Trim();
				if (!string.IsNullOrEmpty(item))
				{
					items.Add(item);
				}
				currentPos = nextLineEnd + 1;
			}
			else if (trimmed.Length == 0)
			{
				currentPos = nextLineEnd + 1;
			}
			else
			{
				break;
			}
		}

		return items;
	}

	private static List<string> ExtractAllPathPrefixes(string content)
	{
		List<string> prefixes = new();
		MatchCollection matches = Regex.Matches(content, @"PathPrefix:\s*(\S+)");
		foreach (Match match in matches)
		{
			string value = match.Groups[1].Value;
			if (!string.IsNullOrEmpty(value) && value != "0")
			{
				prefixes.Add(value);
			}
		}
		return prefixes;
	}
}