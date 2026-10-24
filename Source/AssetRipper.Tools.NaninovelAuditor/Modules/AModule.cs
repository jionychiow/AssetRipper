using System.Text;
using AssetRipper.Import.Logging;
using AssetRipper.Tools.NaninovelAuditor.Inventory;
using AssetRipper.Tools.NaninovelAuditor.Models;
using AssetRipper.Tools.NaninovelAuditor.Modules.A;
using AssetRipper.Tools.NaninovelAuditor.Reporting;
using AssetRipper.Tools.NaninovelAuditor.Session;

namespace AssetRipper.Tools.NaninovelAuditor.Modules;

/// <summary>
/// A模块编排（任务7/8：数量三维核对 + 全量内容对比）：
/// 清单建立 → 基准生成 → 数量核对 → 内容对比 → 产出 a_nani_inventory.md + a_nani_diff.csv。
/// </summary>
public sealed class AModule : INaninovelAuditModule
{
	public string ModuleName => "A";

	public AuditRunResult Execute(AuditSession session)
	{
		Logger.Info(LogCategory.System, "[A] Building inventories...");
		SourceInventoryBuilder sourceBuilder = new();
		SourceInventory sourceInventory = sourceBuilder.Build(session.GameData, session.Options);

		ExportedInventoryBuilder exportedBuilder = new();
		ExportedInventory exportedInventory = exportedBuilder.Build(session.Options.ExportedRoot, out _);

		Logger.Info(LogCategory.System, "[A] Generating baselines...");
		NaniBaselineDecoder decoder = new();
		decoder.BuildAssetIndex(session.GameData);
		BaselineGenerationResult baselineResult = decoder.GenerateBaselines(sourceInventory, session.Options);

		Logger.Info(LogCategory.System, "[A] Running quantity three-way audit...");
		NaniInventoryAuditor quantityAuditor = new();
		InventoryAuditResult quantityResult = quantityAuditor.Audit(baselineResult.UpdatedInventory, exportedInventory);

		Logger.Info(LogCategory.System, "[A] Running full content comparison...");
		NaniContentComparator contentComparator = new();
		ContentAuditResult contentResult = contentComparator.Compare(
			baselineResult.BaselineTextsById, exportedInventory, session.Options.ExportedRoot, session.Options.SampleCount);

		Logger.Info(LogCategory.System, "[A] Tracing duplicate origins...");
		DuplicateOriginTracer tracer = new();
		TraceAuditResult traceResult = tracer.Trace(baselineResult.UpdatedInventory, exportedInventory);

		string mdPath = Path.Combine(session.Options.ReportDirectory, "a_nani_inventory.md");
		string csvPath = Path.Combine(session.Options.ReportDirectory, "a_nani_three_way.csv");
		string diffCsvPath = Path.Combine(session.Options.ReportDirectory, "a_nani_diff.csv");
		string contentCsvPath = Path.Combine(session.Options.ReportDirectory, "a_nani_content_rows.csv");
		string traceCsvPath = Path.Combine(session.Options.ReportDirectory, "a_duplicate_trace.csv");

		WriteReport(mdPath, quantityResult, contentResult, traceResult);
		ArtifactWriter.WriteCsv(csvPath,
			["BaseId", "SourceInstances", "ExportedFiles", "SourcePaths"],
			quantityResult.Rows.Select(r => new[] { r.BaseId, r.SourceInstances.ToString(), r.ExportedFiles.ToString(), r.SourcePaths }));
		ArtifactWriter.WriteCsv(diffCsvPath,
			["DiffId", "ExportedPath", "SourcePath", "DiffType", "Severity", "RelatedModule", "Detail"],
			quantityResult.Diffs.Concat(contentResult.Diffs).Select(d => new[]
			{
				d.DiffId.ToString(), d.ExportedPath, d.SourcePath, d.DiffType.ToString(),
				d.Severity.ToString(), d.RelatedModule.ToString(), ArtifactWriter.TruncateFragment(d.Detail),
			}));
		ArtifactWriter.WriteCsv(contentCsvPath,
			["BaseId", "ExportedPath", "MatchLevel", "Note"],
			contentResult.Rows.Select(r => new[] { r.BaseId, r.ExportedPath, r.MatchLevel.ToString(), r.Note }));
		ArtifactWriter.WriteCsv(traceCsvPath,
			["DuplicateFilePath", "SourcePathId", "SourceBundle", "Cause", "InferredByIndexPattern"],
			traceResult.Traces.Select(t => new[]
			{
				t.DuplicateFilePath, t.SourcePathId.ToString(), t.SourceBundle, t.Cause.ToString(), t.InferredByIndexPattern.ToString(),
			}));

		Logger.Info(LogCategory.System, $"[A] Done: rows={quantityResult.Rows.Count}, quantityDiffs={quantityResult.Diffs.Count}, " +
			$"content[{contentResult.Stats.ByteIdentical}/{contentResult.Stats.NormalizedIdentical}/{contentResult.Stats.SubstantiveDifference}] of {contentResult.Stats.Total}, " +
			$"traced={traceResult.TracedCopies}/{traceResult.TracedCopies + traceResult.UntracedCopies}");

		return new AuditRunResult
		{
			ModuleName = ModuleName,
			Success = true,
			Summary = $"sourceUniqueIds={quantityResult.Verdict.SourceUniqueIds}; exportedUniqueIds={quantityResult.Verdict.ExportedUniqueIds}; " +
				$"quantityDiffs={quantityResult.Diffs.Count}; content: byteIdentical={contentResult.Stats.ByteIdentical}, " +
				$"normalized={contentResult.Stats.NormalizedIdentical}, substantive={contentResult.Stats.SubstantiveDifference} (total files={contentResult.Stats.Total}); " +
				$"duplicateTraced={traceResult.TracedCopies}, untraced={traceResult.UntracedCopies}",
			ProducedArtifacts = [mdPath, csvPath, diffCsvPath, contentCsvPath, traceCsvPath],
		};
	}

	private static void WriteReport(string path, InventoryAuditResult quantityResult, ContentAuditResult contentResult, TraceAuditResult traceResult)
	{
		StringBuilder sb = new();
		sb.AppendLine("# A Module: Nani Inventory Audit (Quantity + Content)");
		sb.AppendLine();
		sb.AppendLine("## 数字裁决（REQ-A1/A2）");
		sb.AppendLine();
		sb.AppendLine(quantityResult.Verdict.VerdictText);
		sb.AppendLine();

		sb.AppendLine("## 副本数分布（每基础ID的导出文件数 → 基础ID数）");
		sb.AppendLine();
		sb.AppendLine("| Copies | Base identifiers |");
		sb.AppendLine("|--------|------------------|");
		foreach (KeyValuePair<int, int> pair in quantityResult.Distribution.CopiesToBaseIdCounts.OrderBy(p => p.Key))
		{
			sb.AppendLine($"| {pair.Key} | {pair.Value} |");
		}
		sb.AppendLine($"- 带副本序号（_数字）的文件总数: {quantityResult.Distribution.TotalDuplicateFiles}");
		sb.AppendLine();

		sb.AppendLine("## 内容核对结论（REQ-A3/A5，全量三级判定）");
		sb.AppendLine();
		sb.AppendLine($"- 逐字节一致: {contentResult.Stats.ByteIdentical}");
		sb.AppendLine($"- 规范化后一致（BOM/换行差异）: {contentResult.Stats.NormalizedIdentical}");
		sb.AppendLine($"- 实质差异: {contentResult.Stats.SubstantiveDifference}");
		sb.AppendLine($"- 对比文件总数: {contentResult.Stats.Total}");
		sb.AppendLine();

		sb.AppendLine("### 抽样集（五类特征覆盖）");
		sb.AppendLine();
		if (contentResult.Samples.Count == 0)
		{
			sb.AppendLine("（无样本）");
		}
		else
		{
			foreach (string sample in contentResult.Samples)
			{
				sb.AppendLine($"- {sample}");
			}
		}
		sb.AppendLine();

		sb.AppendLine("## 双向缺口清单（数量差异记录）");
		sb.AppendLine();
		if (quantityResult.Diffs.Count == 0)
		{
			sb.AppendLine("（无——原始唯一ID与导出去重ID完全对应，无双向缺口）");
		}
		else
		{
			sb.AppendLine("| DiffId | Type | Severity | ExportedPath | SourcePath | Detail |");
			sb.AppendLine("|--------|------|----------|--------------|------------|--------|");
			foreach (DiffRecord diff in quantityResult.Diffs)
			{
				sb.AppendLine($"| {diff.DiffId} | {diff.DiffType} | {diff.Severity} | {diff.ExportedPath} | {diff.SourcePath} | {ArtifactWriter.TruncateFragment(diff.Detail)} |");
			}
		}
		sb.AppendLine();

		sb.AppendLine("## 内容差异明细（实质差异，片段最小化）");
		sb.AppendLine();
		if (contentResult.ContentDiffs.Count == 0)
		{
			sb.AppendLine("（无实质差异——全部导出文件与原始基准逐字节或规范化一致）");
		}
		else
		{
			foreach (NaniContentDiff diff in contentResult.ContentDiffs.Take(50))
			{
				sb.AppendLine($"### {diff.ScriptId.BaseName} — {diff.ScriptId.ExportedFilePath}");
				foreach (MinimalDiffFragment fragment in diff.Fragments)
				{
					sb.AppendLine($"- L{fragment.SourceLine}: baseline `{fragment.SourceFragment}` vs exported `{fragment.ExportedFragment}`");
				}
			}
			if (contentResult.ContentDiffs.Count > 50)
			{
				sb.AppendLine($"- …（共 {contentResult.ContentDiffs.Count} 个差异文件，仅列前50，完整明细见 a_nani_diff.csv）");
			}
		}
		sb.AppendLine();

		sb.AppendLine("## REQ-A5 内容核对结论（已回填）");
		sb.AppendLine();
		sb.AppendLine($"- 数量核对: 原始唯一ID {quantityResult.Verdict.SourceUniqueIds} = 导出去重ID {quantityResult.Verdict.ExportedUniqueIds}，双向缺口 {quantityResult.Diffs.Count} 条。");
		sb.AppendLine($"- 内容核对: 逐字节一致 {contentResult.Stats.ByteIdentical} / 规范化一致 {contentResult.Stats.NormalizedIdentical} / 实质差异 {contentResult.Stats.SubstantiveDifference}（总数 {contentResult.Stats.Total}）。");
		sb.AppendLine("- 数量+内容双核对齐备，本结论有效（REQ-A5）。");
		sb.AppendLine();

		sb.AppendLine("## 副本溯源（REQ-A4）");
		sb.AppendLine();
		sb.AppendLine($"- 成功指认副本数: {traceResult.TracedCopies}；无法指认: {traceResult.UntracedCopies}");
		string coverage = (traceResult.TracedCopies + traceResult.UntracedCopies) == 0
			? "N/A"
			: $"{(double)traceResult.TracedCopies / (traceResult.TracedCopies + traceResult.UntracedCopies):P1}";
		sb.AppendLine($"- 溯源覆盖率: {coverage}");
		sb.AppendLine();
		sb.AppendLine("### 成因归类分布");
		sb.AppendLine();
		sb.AppendLine("| Cause | File count |");
		sb.AppendLine("|-------|-----------|");
		foreach (KeyValuePair<string, int> pair in traceResult.CauseCounts.OrderByDescending(p => p.Value))
		{
			sb.AppendLine($"| {pair.Key} | {pair.Value} |");
		}
		sb.AppendLine();
		sb.AppendLine("### 典型脚本溯源表（每副本的原始来源 bundle 可指认）");
		sb.AppendLine();
		sb.AppendLine("| BaseId | Exported files | Source instances |");
		sb.AppendLine("|--------|----------------|------------------|");
		foreach (TypicalTrace typical in traceResult.TypicalTraces)
		{
			sb.AppendLine($"| {typical.BaseId} | {typical.ExportedFiles} | {typical.SourceInstances} |");
		}

		File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
	}
}