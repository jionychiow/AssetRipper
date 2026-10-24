using AssetRipper.Tools.NaninovelAuditor.Models;
using AssetRipper.Tools.NaninovelAuditor.Reporting;

namespace AssetRipper.Tools.NaninovelAuditor.Modules.C;

/// <summary>
/// C模块字段级对比与引用完整性核对（任务12，REQ-C1/C2/C4）：
/// 配置类型覆盖核对 + 字段级双向差与值差异（含"第13轮既有修复所致"分类防误报）+ 断链引用核对。
/// </summary>
internal sealed class ConfigurationFieldAuditor
{
	/// <summary>已知有意修复（第13轮及此前），防止误判为缺陷并误回退基线（design 1.2.5）。</summary>
	private static readonly (string Config, string FieldPrefix, string Intent)[] KnownIntentionalFixes =
	[
		("ResourceProviderConfiguration", "ProviderTypes", "RemoveAddressableResourceProvider：移除 AddressableResourceProvider 加入 LocalResourceProvider 回退（2026-09-16 有意差异）"),
		("ResourceProviderConfiguration", "ProjectRootPath", "ProjectRootPath 应为空（2026-09-26 有意差异）"),
		("StateConfiguration", "", "StateConfigurationFixer：BinarySaveFiles=0 等运行时兼容修复（有意差异）"),
		("EngineConfiguration", "AsyncExceptionLogType", "AsyncExceptionLogType 0→1：导出后修复静默吞异常问题（有意差异）"),
		("", "Loader.ProviderTypes", "各配置内嵌 Loader.ProviderTypes 移除 AddressableResourceProvider（2026-09-16 全配置统一修复，有意差异）"),
		("", ".ProviderTypes", "Provider 类型列表统一修复（Addressables 包缺失致 GetExportedTypes 崩溃，有意差异）"),
	];

	public FieldAuditResult Audit(
		Dictionary<string, Dictionary<string, string>> sourceFields,
		string exportedConfigDir)
	{
		List<FieldDiff> diffs = [];
		List<DiffRecord> diffRecords = [];
		Dictionary<string, Dictionary<string, string>> exportedAll = new(StringComparer.OrdinalIgnoreCase);
		int diffId = 1;

		// 导出侧解析（含 _0~_4 副本：仅统计本体，副本单独计数）
		Dictionary<string, List<string>> exportedFilesByConfig = new(StringComparer.OrdinalIgnoreCase);
		if (Directory.Exists(exportedConfigDir))
		{
			foreach (string file in Directory.EnumerateFiles(exportedConfigDir, "*.asset"))
			{
				string configName = Path.GetFileNameWithoutExtension(file);
				string baseName = configName;
				int underscore = baseName.LastIndexOf('_');
				bool isDuplicate = underscore > 0 && int.TryParse(baseName[(underscore + 1)..], out _);
				if (isDuplicate)
				{
					baseName = baseName[..underscore];
				}
				if (!exportedFilesByConfig.TryGetValue(baseName, out List<string>? list))
				{
					list = [];
					exportedFilesByConfig[baseName] = list;
				}
				list.Add(Path.GetFileName(file));
				if (!isDuplicate)
				{
					exportedAll[baseName] = UnityAssetYamlParser.Parse(file);
				}
			}
		}

		// REQ-C1 类型覆盖核对
		List<string> missingTypes = [];
		foreach (string configName in sourceFields.Keys)
		{
			if (!exportedAll.ContainsKey(configName))
			{
				missingTypes.Add(configName);
				diffRecords.Add(new DiffRecord
				{
					DiffId = diffId++,
					ExportedPath = $"(missing) Assets/Resources/naninovel/configuration/{configName}.asset",
					SourcePath = configName,
					DiffType = DiffType.QuantityMissing,
					Severity = Severity.Fatal,
					RelatedModule = AuditModule.C,
					Detail = "Configuration type exists in source but not exported.",
				});
			}
		}
		List<string> extraTypes = exportedFilesByConfig.Keys
			.Where(k => !sourceFields.ContainsKey(k))
			.ToList();

		// REQ-C2 字段级对比
		int comparedConfigs = 0;
		int identicalFields = 0;
		foreach (KeyValuePair<string, Dictionary<string, string>> sourcePair in sourceFields)
		{
			if (!exportedAll.TryGetValue(sourcePair.Key, out Dictionary<string, string>? exported))
			{
				continue;
			}
			comparedConfigs++;

			HashSet<string> allPaths = new(sourcePair.Value.Keys, StringComparer.Ordinal);
			allPaths.UnionWith(exported.Keys);

			foreach (string path in allPaths)
			{
				bool inSource = sourcePair.Value.TryGetValue(path, out string? sourceValue);
				bool inExported = exported.TryGetValue(path, out string? exportedValue);
				if (inSource && !inExported)
				{
					diffs.Add(new FieldDiff(sourcePair.Key, path, sourceValue ?? "", null, FieldDiffKind.SourceOnlyField));
				}
				else if (!inSource && inExported)
				{
					diffs.Add(new FieldDiff(sourcePair.Key, path, null, exportedValue ?? "", FieldDiffKind.ExportedOnlyField));
				}
				else if (inSource && inExported && NormalizeValue(sourceValue) != NormalizeValue(exportedValue))
				{
					FieldDiffKind kind = IsKnownIntentionalFix(sourcePair.Key, path)
						? FieldDiffKind.IntentionalFix
						: FieldDiffKind.ValueDifference;
					diffs.Add(new FieldDiff(sourcePair.Key, path, sourceValue, exportedValue, kind));
				}
				else
				{
					identicalFields++;
				}
			}
		}

		// REQ-C4 引用完整性核对（GUID → .meta 解析，预建全项目 GUID 索引一次）
		List<string> brokenReferences = [];
		HashSet<string> projectGuidIndex = BuildGuidIndex(exportedConfigDir);
		foreach (KeyValuePair<string, Dictionary<string, string>> pair in exportedAll)
		{
			foreach (KeyValuePair<string, string> field in pair.Value)
			{
				string guid = ExtractGuid(field.Value);
				if (guid is null)
				{
					continue;
				}
				if (!projectGuidIndex.Contains(guid))
				{
					brokenReferences.Add($"{pair.Key}.{field.Key}: guid={guid}");
				}
			}
		}

		return new FieldAuditResult(diffs, diffRecords, missingTypes, extraTypes,
			comparedConfigs, identicalFields, exportedFilesByConfig, brokenReferences);
	}

	private static HashSet<string> BuildGuidIndex(string exportedConfigDir)
	{
		HashSet<string> index = new(StringComparer.OrdinalIgnoreCase);
		string projectRoot = Path.GetFullPath(Path.Combine(exportedConfigDir, "..", "..", ".."));
		if (!Directory.Exists(projectRoot))
		{
			return index;
		}
		foreach (string meta in Directory.EnumerateFiles(projectRoot, "*.meta", SearchOption.AllDirectories))
		{
			try
			{
				foreach (string line in File.ReadLines(meta))
				{
					if (line.StartsWith("guid:", StringComparison.OrdinalIgnoreCase))
					{
						index.Add(line["guid:".Length..].Trim());
						break;
					}
				}
			}
			catch
			{
			}
		}
		return index;
	}

	private static bool IsKnownIntentionalFix(string configName, string fieldPath)
	{
		foreach ((string Config, string FieldPrefix, string Intent) fix in KnownIntentionalFixes)
		{
			if (configName.Equals(fix.Config, StringComparison.OrdinalIgnoreCase)
				&& fieldPath.StartsWith(fix.FieldPrefix, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}
		return false;
	}

	private static string NormalizeValue(string? value)
	{
		if (string.IsNullOrEmpty(value))
		{
			return "";
		}
		return value.Trim().Trim('"').Replace("{", "").Replace("}", "").Replace(" ", "");
	}

	private static string? ExtractGuid(string? value)
	{
		if (value is null)
		{
			return null;
		}
		int index = value.IndexOf("guid: ", StringComparison.Ordinal);
		if (index < 0)
		{
			return null;
		}
		string rest = value[(index + 6)..];
		int end = rest.IndexOfAny([',', '}', ' ', '\n']);
		return end > 0 ? rest[..end] : rest;
	}

}

internal enum FieldDiffKind
{
	SourceOnlyField,
	ExportedOnlyField,
	ValueDifference,
	IntentionalFix,
}

internal sealed record FieldDiff(string Config, string Path, string? SourceValue, string? ExportedValue, FieldDiffKind Kind);

internal sealed record FieldAuditResult(
	IReadOnlyList<FieldDiff> Diffs,
	IReadOnlyList<DiffRecord> DiffRecords,
	IReadOnlyList<string> MissingTypes,
	IReadOnlyList<string> ExtraTypes,
	int ComparedConfigs,
	int IdenticalFields,
	IReadOnlyDictionary<string, List<string>> ExportedFilesByConfig,
	IReadOnlyList<string> BrokenReferences);