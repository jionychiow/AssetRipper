using System.Text;
using AssetRipper.Export.UnityProjects.AddressablesPathFix.Models;
using AssetRipper.Import.Logging;

namespace AssetRipper.Export.UnityProjects.AddressablesPathFix.Reporting;

public sealed class FixReportGenerator
{
	public void Generate(MoveResult result, MovePlan plan, CatalogData catalog, NaninovelConfig config, FullMatchReport fullMatchReport, string assetsPath, FileSystem fileSystem)
	{
		IReadOnlyList<string> missingDirs = VerifyDirectoryStructure(config, assetsPath, fileSystem);
		IReadOnlyDictionary<string, IReadOnlyList<string>> unmappedGroups = GroupUnmappedResources(plan.UnmappedResources);

		string report = BuildReportMarkdown(result, catalog, config, fullMatchReport, missingDirs, unmappedGroups, assetsPath);

		string projectRoot = fileSystem.Path.GetDirectoryName(assetsPath)!;
		string reportPath = fileSystem.Path.Join(projectRoot, "addressables_full_match_report.md");

		try
		{
			fileSystem.File.WriteAllText(reportPath, report);
			Logger.Info(LogCategory.Export, $"报告已生成: {reportPath}");
		}
		catch (Exception ex)
		{
			Logger.Warning(LogCategory.Export, $"报告文件写入失败: {ex.Message}");
			Logger.Info(LogCategory.Export, report);
		}
	}

	private static IReadOnlyList<string> VerifyDirectoryStructure(NaninovelConfig config, string assetsPath, FileSystem fileSystem)
	{
		List<string> missing = new();
		string resourcesRoot = fileSystem.Path.Join(assetsPath, "Resources");

		foreach (string prefix in config.PathPrefixes)
		{
			string dirPath = fileSystem.Path.Join(resourcesRoot, prefix);
			if (!fileSystem.Directory.Exists(dirPath))
			{
				Logger.Error(LogCategory.Export, $"缺失目录: {dirPath}");
				missing.Add(dirPath);
				try
				{
					Directory.CreateDirectory(dirPath);
				}
				catch
				{
				}
			}
		}

		return missing;
	}

	private static IReadOnlyDictionary<string, IReadOnlyList<string>> GroupUnmappedResources(IReadOnlyList<UnmappedResource> unmapped)
	{
		Dictionary<string, List<string>> groups = new(StringComparer.OrdinalIgnoreCase);
		foreach (UnmappedResource r in unmapped)
		{
			if (!groups.TryGetValue(r.TypeDirectory, out List<string>? list))
			{
				list = [];
				groups[r.TypeDirectory] = list;
			}
			list.Add(r.SourcePath);
		}
		return groups.ToDictionary(kvp => kvp.Key, kvp => (IReadOnlyList<string>)kvp.Value, StringComparer.OrdinalIgnoreCase);
	}

	private static string BuildReportMarkdown(
		MoveResult result,
		CatalogData catalog,
		NaninovelConfig config,
		FullMatchReport fullMatchReport,
		IReadOnlyList<string> missingDirs,
		IReadOnlyDictionary<string, IReadOnlyList<string>> unmappedGroups,
		string assetsPath)
	{
		StringBuilder sb = new();

		sb.AppendLine("# Addressables 资源路径 100% 匹配报告");
		sb.AppendLine();
		sb.AppendLine($"生成时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
		sb.AppendLine();

		sb.AppendLine("## 执行摘要");
		sb.AppendLine();
		int total = result.Successes.Count + result.Skipped.Count + result.Failures.Count;
		sb.AppendLine($"| 指标 | 值 |");
		sb.AppendLine($"|------|-----|");
		sb.AppendLine($"| 总资源 | {total} |");
		sb.AppendLine($"| 成功移动 | {result.Successes.Count} |");
		sb.AppendLine($"| 跳过 | {result.Skipped.Count} |");
		sb.AppendLine($"| 失败 | {result.Failures.Count} |");
		sb.AppendLine($"| 冲突 | {result.Conflicts.Count} |");
		sb.AppendLine($"| 占位目录 | {result.CreatedPlaceholders.Count} |");
		sb.AppendLine();

		sb.AppendLine("## 映射策略统计");
		sb.AppendLine();
		sb.AppendLine($"| 策略 | 匹配数 |");
		sb.AppendLine($"|------|--------|");
		sb.AppendLine($"| D (PathID→地址) | {fullMatchReport.MatchMethodStats.PathIdAddressCount} |");
		sb.AppendLine($"| A (原始GUID) | {fullMatchReport.MatchMethodStats.OriginalGuidCount} |");
		sb.AppendLine($"| B (PathID+名称) | {fullMatchReport.MatchMethodStats.PathIdCount} |");
		sb.AppendLine($"| C (名称) | {fullMatchReport.MatchMethodStats.NameCount} |");
		sb.AppendLine($"| **合计** | **{fullMatchReport.MatchMethodStats.Total}** |");
		sb.AppendLine();

		sb.AppendLine("## 100% 匹配达成状态");
		sb.AppendLine();
		sb.AppendLine($"| 指标 | 值 |");
		sb.AppendLine($"|------|-----|");
		sb.AppendLine($"| Naninovel 范围内总地址数 | {fullMatchReport.TotalResources} |");
		sb.AppendLine($"| 已匹配地址数 | {fullMatchReport.MatchedResources} |");
		sb.AppendLine($"| 未匹配地址数 | {fullMatchReport.TotalResources - fullMatchReport.MatchedResources} |");
		sb.AppendLine($"| 整体匹配率 | {fullMatchReport.OverallMatchRate:F1}% |");
		sb.AppendLine($"| 100% 达成 | {(fullMatchReport.FullMatchAchieved ? "✅ 是" : "❌ 否")} |");
		sb.AppendLine();

		if (!fullMatchReport.FullMatchAchieved && fullMatchReport.UnmappedAddresses.Count > 0)
		{
			sb.AppendLine("### 未匹配地址清单");
			sb.AppendLine();
			int showCount = Math.Min(fullMatchReport.UnmappedAddresses.Count, 200);
			foreach (string addr in fullMatchReport.UnmappedAddresses.Take(showCount))
			{
				sb.AppendLine($"- `{addr}`");
			}
			if (fullMatchReport.UnmappedAddresses.Count > showCount)
			{
				sb.AppendLine($"- ... 共 {fullMatchReport.UnmappedAddresses.Count} 个未匹配地址");
			}
			sb.AppendLine();
		}

		sb.AppendLine("## 目录结构验证");
		sb.AppendLine();
		if (missingDirs.Count == 0)
		{
			sb.AppendLine("所有 PathPrefix 目录均存在。");
		}
		else
		{
			sb.AppendLine($"缺失 {missingDirs.Count} 个目录（已尝试重建）:");
			foreach (string d in missingDirs)
			{
				sb.AppendLine($"- `{d}`");
			}
		}
		sb.AppendLine();

		sb.AppendLine("## 按 PathPrefix 匹配率统计");
		sb.AppendLine();
		sb.AppendLine($"| PathPrefix | 期望 | 已匹配 | 未匹配 | 匹配率 | 达标 |");
		sb.AppendLine($"|------------|------|--------|--------|--------|------|");
		foreach (MatchRateStatistics stat in fullMatchReport.PathPrefixStats)
		{
			string achieved = stat.MatchRate >= 100.0 ? "✅" : "❌";
			sb.AppendLine($"| {stat.PathPrefix} | {stat.TotalAddresses} | {stat.MatchedCount} | {stat.UnmatchedCount} | {stat.MatchRate:F1}% | {achieved} |");
		}
		sb.AppendLine();

		sb.AppendLine("## 未映射资源清单");
		sb.AppendLine();
		if (unmappedGroups.Count == 0)
		{
			sb.AppendLine("无未映射资源。");
		}
		else
		{
			foreach (KeyValuePair<string, IReadOnlyList<string>> kvp in unmappedGroups)
			{
				sb.AppendLine($"### {kvp.Key} ({kvp.Value.Count} 个)");
				foreach (string path in kvp.Value)
				{
					sb.AppendLine($"- `{path}`");
				}
				sb.AppendLine();
			}
		}

		sb.AppendLine("## 冲突处理记录");
		sb.AppendLine();
		if (result.Conflicts.Count == 0)
		{
			sb.AppendLine("无冲突。");
		}
		else
		{
			foreach (MoveConflict c in result.Conflicts)
			{
				sb.AppendLine($"- `{c.Item.SourcePath}` -> `{c.Item.TargetPath}`: {c.Strategy} ({c.Resolution})");
			}
		}
		sb.AppendLine();

		sb.AppendLine("## 失败详情");
		sb.AppendLine();
		if (result.Failures.Count == 0)
		{
			sb.AppendLine("无失败。");
		}
		else
		{
			foreach (MoveFailure f in result.Failures)
			{
				sb.AppendLine($"- `{f.Item.SourcePath}` -> `{f.Item.TargetPath}`: {f.Stage} - {f.Exception}");
			}
		}

		return sb.ToString();
	}
}
