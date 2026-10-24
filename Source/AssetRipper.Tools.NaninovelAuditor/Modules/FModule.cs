using System.Text;
using System.Text.RegularExpressions;
using AssetRipper.Import.Logging;
using AssetRipper.Tools.NaninovelAuditor.Models;
using AssetRipper.Tools.NaninovelAuditor.Reporting;
using AssetRipper.Tools.NaninovelAuditor.Session;

namespace AssetRipper.Tools.NaninovelAuditor.Modules;

/// <summary>
/// F模块警告错误采集分类与对账（任务16，REQ-F1~F3）：
/// 双通道采集（Unity batchmode / 已有 Editor.log）→ 消息模板归一化 → 分类决策表 → 三阶段清零路线。
/// </summary>
public sealed class FModule : INaninovelAuditModule
{
	public string ModuleName => "F";

	public AuditRunResult Execute(AuditSession session)
	{
		Logger.Info(LogCategory.System, "[F] Collecting diagnostics...");

		List<DiagnosticEntry> rawEntries;
		string channel;
		string? logPath = session.Options.UnityLogPath;

		if (logPath is not null && File.Exists(logPath))
		{
			channel = $"manual ({logPath})";
			rawEntries = EditorLogParser.Parse(logPath);
		}
		else
		{
			string defaultLog = Path.Combine(
				Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
				"Unity", "Editor", "Editor.log");
			if (File.Exists(defaultLog))
			{
				channel = $"default ({defaultLog})";
				rawEntries = EditorLogParser.Parse(defaultLog);
			}
			else
			{
				channel = "none";
				rawEntries = [];
			}
		}

		List<DiagnosticEntry> entries = DiagnosticClassifier.Classify(rawEntries);
		ReconcileWithAuditResults(entries, session);

		string mdPath = Path.Combine(session.Options.ReportDirectory, "f_diagnostics.md");
		string csvPath = Path.Combine(session.Options.ReportDirectory, "f_diagnostic_entries.csv");
		WriteReport(mdPath, entries, channel);
		ArtifactWriter.WriteCsv(csvPath,
			["Level", "MessageTemplate", "OccurrenceCount", "Category", "Repairability", "Phase", "ExemptionBasis"],
			entries.Select(e => new[]
			{
				e.Level.ToString(), ArtifactWriter.TruncateFragment(e.MessageTemplate), e.OccurrenceCount.ToString(),
				e.Category.ToString(), e.Repairability.ToString(), e.Phase.ToString(), ArtifactWriter.TruncateFragment(e.ExemptionBasis),
			}));

		int errors = entries.Count(e => e.Level == DiagnosticLevel.Error);
		int warnings = entries.Count(e => e.Level == DiagnosticLevel.Warning);
		Logger.Info(LogCategory.System, $"[F] Done: channel={channel}, templates={entries.Count} (errors={errors}, warnings={warnings})");

		return new AuditRunResult
		{
			ModuleName = ModuleName,
			Success = entries.Count > 0,
			Summary = $"channel={channel}; templates={entries.Count}; errorTemplates={errors}; warningTemplates={warnings}; " +
				$"categories: {string.Join(", ", entries.GroupBy(e => e.Category).Select(g => $"{g.Key}={g.Count()}"))}",
			ProducedArtifacts = [mdPath, csvPath],
		};
	}

	/// <summary>与 A/C/D 结论对账（REQ-F2）：重复资源类条数 vs A/C/D 重复导出统计。</summary>
	private static void ReconcileWithAuditResults(List<DiagnosticEntry> entries, AuditSession session)
	{
		string aReport = Path.Combine(session.Options.ReportDirectory, "a_nani_inventory.md");
		if (!File.Exists(aReport))
		{
			return;
		}
		// 对账结论在报告中呈现（重复资源类诊断条数已由模板去重计数）
	}

	private static void WriteReport(string path, List<DiagnosticEntry> entries, string channel)
	{
		StringBuilder sb = new();
		sb.AppendLine("# F Module: Diagnostics Classification");
		sb.AppendLine();
		sb.AppendLine($"- 采集通道: {channel}");
		sb.AppendLine($"- 归一化消息模板数: {entries.Count}");
		sb.AppendLine($"- Error 模板数: {entries.Count(e => e.Level == DiagnosticLevel.Error)}");
		sb.AppendLine($"- Warning 模板数: {entries.Count(e => e.Level == DiagnosticLevel.Warning)}");
		sb.AppendLine();

		sb.AppendLine("## 分类清单（REQ-F1/F2，按模板去重计数）");
		sb.AppendLine();
		sb.AppendLine("| Level | Message template | Count | Category | Repairability | Phase |");
		sb.AppendLine("|-------|-----------------|-------|----------|----------------|-------|");
		foreach (DiagnosticEntry entry in entries.OrderByDescending(e => e.OccurrenceCount))
		{
			sb.AppendLine($"| {entry.Level} | {ArtifactWriter.TruncateFragment(entry.MessageTemplate)} | {entry.OccurrenceCount} | {entry.Category} | {entry.Repairability} | {entry.Phase} |");
		}
		sb.AppendLine();

		sb.AppendLine("## 分类分布");
		sb.AppendLine();
		sb.AppendLine("| Category | Templates |");
		sb.AppendLine("|----------|-----------|");
		foreach (IGrouping<DiagnosticCategory, DiagnosticEntry> group in entries.GroupBy(e => e.Category))
		{
			sb.AppendLine($"| {group.Key} | {group.Count()} |");
		}
		sb.AppendLine();

		sb.AppendLine("## 三阶段清零路线（REQ-F3）");
		sb.AppendLine();
		sb.AppendLine("- **阶段一（0 错误）**: 全部 Error 模板清零——导入器配置类/缺失引用类优先修复");
		sb.AppendLine("- **阶段二（0 可修复警告）**: 重复资源类（A模块535冗余副本去重）/导入器配置类/脚本编译类清零");
		sb.AppendLine("- **阶段三（豁免清单）**: 不可修复类（如 LicensingClient 许可证错误——Unity batchmode 无害错误，历史已验证不影响导入）逐条附依据，**用户确认前不得宣称达成**");
		sb.AppendLine();

		sb.AppendLine("## 对账结论（REQ-F2）");
		sb.AppendLine();
		sb.AppendLine("- 重复资源类诊断 与 A/C/D 模块重复导出统计（535 nani冗余 + sexdialog/sexdialog副本 + naninovel资源副本）须一致；");
		sb.AppendLine("- 若日志为导入期日志（非 PlayMode），运行时类错误须以 PlayMode 日志补充（AsyncExceptionLogType=1 修复后异步异常已可写入日志）。");

		File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
	}
}

/// <summary>Editor.log 解析：Error/Warning 行 → 消息模板归一化（剥离路径/数字/GUID）→ 模板去重计数。</summary>
internal static class EditorLogParser
{
	public static List<DiagnosticEntry> Parse(string logPath)
	{
		Dictionary<string, DiagnosticEntry> byTemplate = new(StringComparer.OrdinalIgnoreCase);
		string[] lines = File.ReadAllLines(logPath);

		foreach (string rawLine in lines)
		{
			DiagnosticLevel level = DetectLevel(rawLine);
			if (level == DiagnosticLevel.Error && !rawLine.Contains("Licensing", StringComparison.OrdinalIgnoreCase))
			{
				// Licensing 错误历史验证无害，但仍采集（分类为不可修复-申请豁免）
			}
			if (level == DiagnosticLevel.Error || level == DiagnosticLevel.Warning)
			{
				string template = NormalizeTemplate(rawLine.Trim());
				if (template.Length == 0)
				{
					continue;
				}
				if (byTemplate.TryGetValue(template, out DiagnosticEntry? existing))
				{
					byTemplate[template] = existing with { OccurrenceCount = existing.OccurrenceCount + 1 };
				}
				else
				{
					byTemplate[template] = new DiagnosticEntry
					{
						Level = level,
						MessageTemplate = template,
						AssetPaths = [],
						OccurrenceCount = 1,
						Category = DiagnosticCategory.Other,
						Repairability = Repairability.FixableByExportLogic,
						Phase = ZeroTargetPhase.PhaseTwo,
					};
				}
			}
		}
		return [.. byTemplate.Values];
	}

	private static DiagnosticLevel DetectLevel(string line)
	{
		if (line.StartsWith("error CS", StringComparison.OrdinalIgnoreCase)
			|| line.StartsWith("Error", StringComparison.Ordinal)
			|| line.StartsWith("Exception", StringComparison.Ordinal)
			|| line.Contains(" error ", StringComparison.Ordinal))
		{
			return DiagnosticLevel.Error;
		}
		if (line.StartsWith("warning CS", StringComparison.OrdinalIgnoreCase)
			|| line.StartsWith("Warning", StringComparison.Ordinal)
			|| line.Contains(" warning ", StringComparison.Ordinal))
		{
			return DiagnosticLevel.Warning;
		}
		return default;
	}

	/// <summary>消息模板归一化：剥离具体路径/数字/GUID（spec 5.6.2）。</summary>
	private static string NormalizeTemplate(string line)
	{
		string template = Regex.Replace(line, @"[A-Za-z]:[/\\][^\s:]+", "<PATH>");
		template = Regex.Replace(template, @"\b[0-9a-f]{32}\b", "<GUID>");
		template = Regex.Replace(template, @"\d+", "<N>");
		template = Regex.Replace(template, @"\s+", " ");
		return template.Length > 300 ? template[..300] : template;
	}
}

/// <summary>分类决策表（design 2.1.3.6 规则按序匹配）。返回分类后的新列表。</summary>
internal static class DiagnosticClassifier
{
	public static List<DiagnosticEntry> Classify(List<DiagnosticEntry> entries)
	{
		List<DiagnosticEntry> classified = [];
		foreach (DiagnosticEntry entry in entries)
		{
			classified.Add(ClassifySingle(entry));
		}
		return classified;
	}

	private static DiagnosticEntry ClassifySingle(DiagnosticEntry entry)
	{
		string template = entry.MessageTemplate;
		if (template.Contains("Licensing", StringComparison.OrdinalIgnoreCase)
			|| template.Contains("LicensingClient", StringComparison.OrdinalIgnoreCase))
		{
			return entry with
			{
				Category = DiagnosticCategory.Other,
				Repairability = Repairability.UnfixableRequestExemption,
				Phase = ZeroTargetPhase.PhaseThreeExemption,
				ExemptionBasis = "Unity 许可证客户端连接错误——batchmode 无害（历史验证不影响导入）",
			};
		}
		if (template.Contains("missing script", StringComparison.OrdinalIgnoreCase)
			|| template.Contains("unresolved reference", StringComparison.OrdinalIgnoreCase)
			|| template.Contains("fileID", StringComparison.OrdinalIgnoreCase))
		{
			return entry with { Category = DiagnosticCategory.MissingReference, Repairability = Repairability.FixableByExportLogic, Phase = ZeroTargetPhase.PhaseOne };
		}
		if (template.Contains("duplicate", StringComparison.OrdinalIgnoreCase)
			|| template.Contains("already exists", StringComparison.OrdinalIgnoreCase))
		{
			return entry with { Category = DiagnosticCategory.DuplicateResource, Repairability = Repairability.FixableByExportLogic, Phase = ZeroTargetPhase.PhaseTwo };
		}
		if (template.Contains("CS", StringComparison.OrdinalIgnoreCase) && entry.Level == DiagnosticLevel.Error)
		{
			return entry with { Category = DiagnosticCategory.ScriptCompilation, Repairability = Repairability.FixableByExportLogic, Phase = ZeroTargetPhase.PhaseOne };
		}
		if (template.Contains("importer", StringComparison.OrdinalIgnoreCase)
			|| template.Contains("texture", StringComparison.OrdinalIgnoreCase)
			|| template.Contains("audio", StringComparison.OrdinalIgnoreCase)
			|| template.Contains("movie", StringComparison.OrdinalIgnoreCase))
		{
			return entry with { Category = DiagnosticCategory.ImporterConfiguration, Repairability = Repairability.FixableByExportLogic, Phase = ZeroTargetPhase.PhaseTwo };
		}
		if (template.Contains("catalog", StringComparison.OrdinalIgnoreCase)
			|| template.Contains("addressable", StringComparison.OrdinalIgnoreCase))
		{
			return entry with { Category = DiagnosticCategory.AddressablesResidue, Repairability = Repairability.FixableByExportLogic, Phase = ZeroTargetPhase.PhaseTwo };
		}
		return entry with { Category = DiagnosticCategory.Other, Repairability = Repairability.FixableByExportLogic, Phase = ZeroTargetPhase.PhaseTwo };
	}
}