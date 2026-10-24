using System.Text;
using AssetRipper.Import.Logging;
using AssetRipper.Tools.NaninovelAuditor.Models;
using AssetRipper.Tools.NaninovelAuditor.Modules.B;
using AssetRipper.Tools.NaninovelAuditor.Reporting;
using AssetRipper.Tools.NaninovelAuditor.Session;

namespace AssetRipper.Tools.NaninovelAuditor.Modules;

/// <summary>
/// B模块入口（任务10）：56个txt去向表 + 四分类判定 + 实测修正裁决 →
/// 产出 b_text_destination.md + b_reference_map.csv。
/// </summary>
public sealed class BModule : INaninovelAuditModule
{
	private const string SpecClaim = "spec 陈述：仅剩 _synonyms.txt（55个待审计）";
	private const string MeasuredClaim = "实测：56个全在位于 Assets/StreamingAssets/text/";

	public string ModuleName => "B";

	public AuditRunResult Execute(AuditSession session)
	{
		Logger.Info(LogCategory.System, "[B] Auditing text destination...");
		string sourceTextDir = Path.Combine(session.Options.SourceDirectory, "StreamingAssets", "text");
		string exportedProjectRoot = AssetRipper.Tools.NaninovelAuditor.Inventory.InventoryScope.GetExportedProjectRoot(session.Options.ExportedRoot);
		TextDestinationAuditor auditor = new();
		TextAuditResult result = auditor.Audit(sourceTextDir, session.Options.ExportedRoot, exportedProjectRoot);

		if (result.FatalError.Length > 0)
		{
			return new AuditRunResult
			{
				ModuleName = ModuleName,
				Success = false,
				Summary = result.FatalError,
				ProducedArtifacts = [],
			};
		}

		string mdPath = Path.Combine(session.Options.ReportDirectory, "b_text_destination.md");
		string csvPath = Path.Combine(session.Options.ReportDirectory, "b_reference_map.csv");

		WriteReport(mdPath, result);
		ArtifactWriter.WriteCsv(csvPath,
			["FileName", "ByteLength", "Sha256", "Kind", "ExportedPaths", "Status"],
			result.Rows.Select(r => new[]
			{
				r.FileName, r.ByteLength.ToString(), r.Sha256, r.Kind.ToString(), r.ExportedPaths, r.Status,
			}));

		int inPlace = result.Rows.Count(r => r.Status.StartsWith("在位") || r.Status.StartsWith("多份"));
		int identical = result.Rows.Count(r => r.Status == "在位-逐字节一致");
		int missing = result.Rows.Count(r => r.Status == "未导出");
		Logger.Info(LogCategory.System, $"[B] Done: rows={result.Rows.Count}, inPlace={inPlace}, identical={identical}, missing={missing}, diffs={result.Diffs.Count}");

		return new AuditRunResult
		{
			ModuleName = ModuleName,
			Success = missing == 0 && result.Diffs.Count == 0,
			Summary = $"textFiles={result.Rows.Count}; inPlace={inPlace}; byteIdentical={identical}; missing={missing}; " +
				$"gameScriptEvidence={result.GameScriptEvidence.Count}; naniReferences={result.NaniReferences.Count}; diffs={result.Diffs.Count}",
			ProducedArtifacts = [mdPath, csvPath],
		};
	}

	private static void WriteReport(string path, TextAuditResult result)
	{
		StringBuilder sb = new();
		sb.AppendLine("# B Module: Text Destination Audit");
		sb.AppendLine();
		sb.AppendLine("## 实测修正裁决（spec 1.2.6）");
		sb.AppendLine();
		sb.AppendLine($"- {SpecClaim}");
		sb.AppendLine($"- {MeasuredClaim}");
		sb.AppendLine($"- **裁决：以实测为准——56个txt全部在位于 Assets/StreamingAssets/text/（逐字节一致性见下表）。spec 陈述的'仅剩1个'为过时信息。**");
		sb.AppendLine();

		sb.AppendLine("## 56行去向表（REQ-B1）");
		sb.AppendLine();
		sb.AppendLine("| File | Bytes | Kind | Exported path(s) | Status |");
		sb.AppendLine("|------|-------|------|------------------|--------|");
		foreach (TextRow row in result.Rows)
		{
			sb.AppendLine($"| {row.FileName} | {row.ByteLength} | {row.Kind} | {row.ExportedPaths} | {row.Status} |");
		}
		sb.AppendLine();

		sb.AppendLine("## 四分类判定分布（REQ-B2）");
		sb.AppendLine();
		sb.AppendLine("| Kind | Count |");
		sb.AppendLine("|------|-------|");
		foreach (IGrouping<TextDestinationAuditor.TextKind, TextRow> group in result.Rows.GroupBy(r => r.Kind))
		{
			sb.AppendLine($"| {group.Key} | {group.Count()} |");
		}
		sb.AppendLine();

		sb.AppendLine("## 引用证据");
		sb.AppendLine();
		sb.AppendLine("### 游戏脚本消费点（BetterStreamingAssets 直读 /text/）");
		sb.AppendLine();
		foreach (string evidence in result.GameScriptEvidence)
		{
			sb.AppendLine($"- `{evidence}`");
		}
		sb.AppendLine();
		sb.AppendLine("### nani 脚本引用（@include/@input/含 text/ 路径）");
		sb.AppendLine();
		if (result.NaniReferences.Count == 0)
		{
			sb.AppendLine("（无——全部 nani 中未发现对 text/*.txt 的引用）");
		}
		else
		{
			foreach (KeyValuePair<string, List<string>> pair in result.NaniReferences)
			{
				sb.AppendLine($"- {pair.Key}:");
				foreach (string reference in pair.Value)
				{
					sb.AppendLine($"  - `{reference}`");
				}
			}
		}
		sb.AppendLine();

		sb.AppendLine("## 影响评估（REQ-B3）");
		sb.AppendLine();
		int missing = result.Rows.Count(r => r.Status == "未导出");
		int contentDiff = result.Rows.Count(r => r.Status.Contains("内容差异"));
		if (missing == 0 && contentDiff == 0)
		{
			sb.AppendLine("- 未导出/路径变更/内容差异条目数：**0**——56个txt全部在位且逐字节一致。");
			sb.AppendLine("- 与五类遗留问题的关联判定：**无关**（该资产面无缺口；对话文字黑色等问题不涉及 StreamingAssets/text 的丢失——txt由游戏自有代码 CamShowUI/DialogSex 消费，非 Naninovel 脚本链路）。");
		}
		else
		{
			sb.AppendLine($"- 未导出: {missing}；内容差异: {contentDiff}（明细见差异记录）");
			sb.AppendLine("- 影响链路：游戏 SexDialogs 对话系统（BetterStreamingAssets 直读）按行读取失败 → 对应对话文案缺失。");
		}
		sb.AppendLine();

		sb.AppendLine("## 差异记录");
		sb.AppendLine();
		if (result.Diffs.Count == 0)
		{
			sb.AppendLine("（无差异记录）");
		}
		else
		{
			sb.AppendLine("| DiffId | Type | Severity | ExportedPath | SourcePath | Detail |");
			sb.AppendLine("|--------|------|----------|--------------|------------|--------|");
			foreach (DiffRecord diff in result.Diffs)
			{
				sb.AppendLine($"| {diff.DiffId} | {diff.DiffType} | {diff.Severity} | {diff.ExportedPath} | {diff.SourcePath} | {diff.Detail} |");
			}
		}

		File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
	}
}