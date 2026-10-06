using System.IO;
using System.Text.RegularExpressions;
using AssetRipper.Export.UnityProjects.AddressablesPathFix.Models;
using AssetRipper.Import.Logging;

namespace AssetRipper.Export.UnityProjects.AddressablesPathFix;

public sealed class MetaFileValidator
{
	private static readonly Regex GuidRegex = new(@"guid:\s*([0-9a-fA-F]{32})", RegexOptions.IgnoreCase | RegexOptions.Compiled);

	public MetaValidationResult Validate(string directoryRoot, FileSystem fileSystem)
	{
		List<string> missingMetaFiles = [];
		List<string> invalidGuidFiles = [];
		Dictionary<string, List<string>> guidToFiles = new(StringComparer.OrdinalIgnoreCase);
		int totalResourceFiles = 0;
		int totalMetaFiles = 0;

		if (!Directory.Exists(directoryRoot))
		{
			return new MetaValidationResult
			{
				MissingMetaFiles = missingMetaFiles,
				InvalidGuidFiles = invalidGuidFiles,
				DuplicateGuids = new Dictionary<string, IReadOnlyList<string>>(),
				TotalResourceFiles = 0,
				TotalMetaFiles = 0,
			};
		}

		IEnumerable<string> allFiles = Directory.EnumerateFiles(directoryRoot, "*", SearchOption.AllDirectories);

		foreach (string file in allFiles)
		{
			if (file.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
			{
				totalMetaFiles++;
				continue;
			}

			totalResourceFiles++;
			string metaPath = file + ".meta";

			if (!File.Exists(metaPath))
			{
				missingMetaFiles.Add(file);
				continue;
			}

			try
			{
				string content = File.ReadAllText(metaPath);
				Match match = GuidRegex.Match(content);
				if (!match.Success)
				{
					invalidGuidFiles.Add(metaPath);
					continue;
				}

				string guid = match.Groups[1].Value;
				if (!guidToFiles.TryGetValue(guid, out List<string>? list))
				{
					list = [];
					guidToFiles[guid] = list;
				}
				list.Add(file);
			}
			catch
			{
				invalidGuidFiles.Add(metaPath);
			}
		}

		Dictionary<string, IReadOnlyList<string>> duplicateGuids = new();
		foreach (KeyValuePair<string, List<string>> kvp in guidToFiles)
		{
			if (kvp.Value.Count > 1)
			{
				duplicateGuids[kvp.Key] = kvp.Value;
			}
		}

		MetaValidationResult result = new()
		{
			MissingMetaFiles = missingMetaFiles,
			InvalidGuidFiles = invalidGuidFiles,
			DuplicateGuids = duplicateGuids,
			TotalResourceFiles = totalResourceFiles,
			TotalMetaFiles = totalMetaFiles,
		};

		if (result.IsValid)
		{
			Logger.Info(LogCategory.Export, $".meta 验证通过: 资源文件={totalResourceFiles}, .meta文件={totalMetaFiles}");
		}
		else
		{
			Logger.Warning(LogCategory.Export, $".meta 验证失败: 缺失={missingMetaFiles.Count}, 无效GUID={invalidGuidFiles.Count}, 重复GUID={duplicateGuids.Count}");
		}

		return result;
	}
}