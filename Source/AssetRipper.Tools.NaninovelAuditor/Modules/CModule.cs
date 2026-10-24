using System.Text;
using AssetRipper.Import.Logging;
using AssetRipper.Tools.NaninovelAuditor.Models;
using AssetRipper.Tools.NaninovelAuditor.Modules.C;
using AssetRipper.Tools.NaninovelAuditor.Reporting;
using AssetRipper.Tools.NaninovelAuditor.Session;

namespace AssetRipper.Tools.NaninovelAuditor.Modules;

/// <summary>
/// C模块编排（任务11/12/13）：原始字段提取（Structure通道）→ 字段级对比（含既有修复分类）→
/// ILSpy 反编译证据检索 → 产出 c_config_fields.md + c_field_diff.csv + c_reference_check.csv。
/// </summary>
public sealed class CModule : INaninovelAuditModule
{
	public string ModuleName => "C";

	public AuditRunResult Execute(AuditSession session)
	{
		Logger.Info(LogCategory.System, "[C] Extracting source configuration fields...");
		MonoBehaviourFieldExtractor extractor = new();
		Dictionary<string, Dictionary<string, string>> sourceFields = extractor.ExtractAll(session.GameData);

		string exportedConfigDir = Path.Combine(
			AssetRipper.Tools.NaninovelAuditor.Inventory.InventoryScope.GetExportedProjectRoot(session.Options.ExportedRoot),
			"Assets", "Resources", "naninovel", "configuration");

		Logger.Info(LogCategory.System, "[C] Running field-level comparison...");
		ConfigurationFieldAuditor auditor = new();
		FieldAuditResult result = auditor.Audit(sourceFields, exportedConfigDir);

		Logger.Info(LogCategory.System, "[C] Searching decomp evidence...");
		DecompEvidenceSearcher searcher = new();
		List<EvidenceEntry> evidence = searcher.Search(session.Options.DecompDirectory, result.Diffs);

		string mdPath = Path.Combine(session.Options.ReportDirectory, "c_config_fields.md");
		string diffCsvPath = Path.Combine(session.Options.ReportDirectory, "c_field_diff.csv");
		string refCsvPath = Path.Combine(session.Options.ReportDirectory, "c_reference_check.csv");

		WriteReport(mdPath, result, evidence);
		ArtifactWriter.WriteCsv(diffCsvPath,
			["Config", "FieldPath", "Kind", "SourceValue", "ExportedValue"],
			result.Diffs.Select(d => new[]
			{
				d.Config, d.Path, d.Kind.ToString(),
				ArtifactWriter.TruncateFragment(d.SourceValue ?? "(absent)"),
				ArtifactWriter.TruncateFragment(d.ExportedValue ?? "(absent)"),
			}));
		ArtifactWriter.WriteCsv(refCsvPath,
			["BrokenReference"],
			result.BrokenReferences.Select(b => new[] { b }));

		int valueDiffs = result.Diffs.Count(d => d.Kind == FieldDiffKind.ValueDifference);
		int intentional = result.Diffs.Count(d => d.Kind == FieldDiffKind.IntentionalFix);
		Logger.Info(LogCategory.System, $"[C] Done: configs={result.ComparedConfigs}, identicalFields={result.IdenticalFields}, " +
			$"diffs={result.Diffs.Count} (value={valueDiffs}, intentional={intentional}), brokenRefs={result.BrokenReferences.Count}");

		return new AuditRunResult
		{
			ModuleName = ModuleName,
			Success = true,
			Summary = $"sourceConfigs={sourceFields.Count}; compared={result.ComparedConfigs}; identicalFields={result.IdenticalFields}; " +
				$"fieldDiffs={result.Diffs.Count} (value={valueDiffs}; sourceOnly={result.Diffs.Count(d => d.Kind == FieldDiffKind.SourceOnlyField)}; " +
				$"exportedOnly={result.Diffs.Count(d => d.Kind == FieldDiffKind.ExportedOnlyField)}; intentional={intentional}); " +
				$"missingTypes={result.MissingTypes.Count}; extraTypes={result.ExtraTypes.Count}; brokenRefs={result.BrokenReferences.Count}",
			ProducedArtifacts = [mdPath, diffCsvPath, refCsvPath],
		};
	}

	private static void WriteReport(string path, FieldAuditResult result, List<EvidenceEntry> evidence)
	{
		StringBuilder sb = new();
		sb.AppendLine("# C Module: Configuration Field Audit");
		sb.AppendLine();
		sb.AppendLine("## 类型覆盖核对（REQ-C1）");
		sb.AppendLine();
		sb.AppendLine($"- 原始配置类型数: {result.ComparedConfigs + result.MissingTypes.Count}");
		sb.AppendLine($"- 已对比（原始+导出双侧存在）: {result.ComparedConfigs}");
		sb.AppendLine($"- 原始存在但导出缺失: {result.MissingTypes.Count}" + (result.MissingTypes.Count > 0 ? " → " + string.Join(", ", result.MissingTypes) : ""));
		sb.AppendLine($"- 导出多出（原始侧无对应实例）: {result.ExtraTypes.Count}" + (result.ExtraTypes.Count > 0 ? " → " + string.Join(", ", result.ExtraTypes) : ""));
		sb.AppendLine();
		sb.AppendLine("### 导出文件副本统计（含 _0~_4 冗余）");
		sb.AppendLine();
		sb.AppendLine("| Config | Exported files |");
		sb.AppendLine("|--------|---------------|");
		foreach (KeyValuePair<string, List<string>> pair in result.ExportedFilesByConfig.OrderBy(p => p.Key))
		{
			sb.AppendLine($"| {pair.Key} | {string.Join(", ", pair.Value)} |");
		}
		sb.AppendLine();

		sb.AppendLine("## 字段级对比结论（REQ-C2）");
		sb.AppendLine();
		sb.AppendLine($"- 完全一致字段数: {result.IdenticalFields}");
		sb.AppendLine($"- 差异字段总数: {result.Diffs.Count}");
		sb.AppendLine($"  - 值差异（疑似导出缺陷）: {result.Diffs.Count(d => d.Kind == FieldDiffKind.ValueDifference)}");
		sb.AppendLine($"  - 原始有导出无（丢字段）: {result.Diffs.Count(d => d.Kind == FieldDiffKind.SourceOnlyField)}");
		sb.AppendLine($"  - 导出有原始无（多余字段）: {result.Diffs.Count(d => d.Kind == FieldDiffKind.ExportedOnlyField)}");
		sb.AppendLine($"  - 已知有意修复所致（基线保护，非缺陷）: {result.Diffs.Count(d => d.Kind == FieldDiffKind.IntentionalFix)}");
		sb.AppendLine();

		sb.AppendLine("### 差异明细（值差异与丢字段/多余字段；有意修复列意图）");
		sb.AppendLine();
		if (result.Diffs.Count == 0)
		{
			sb.AppendLine("（无差异——全部字段一致）");
		}
		else
		{
			sb.AppendLine("| Config | Field | Kind | Source | Exported |");
			sb.AppendLine("|--------|-------|------|--------|----------|");
			foreach (FieldDiff diff in result.Diffs.Take(100))
			{
				sb.AppendLine($"| {diff.Config} | {diff.Path} | {diff.Kind} | {ArtifactWriter.TruncateFragment(diff.SourceValue ?? "(absent)")} | {ArtifactWriter.TruncateFragment(diff.ExportedValue ?? "(absent)")} |");
			}
			if (result.Diffs.Count > 100)
			{
				sb.AppendLine($"| … | … | … | …（共 {result.Diffs.Count} 条，完整见 c_field_diff.csv） | … |");
			}
		}
		sb.AppendLine();

		sb.AppendLine("## 重点必查字段结论（UIConfiguration / TextPrintersConfiguration 颜色字体）");
		sb.AppendLine();
		List<FieldDiff> uiDiffs = result.Diffs
			.Where(d => d.Config.Contains("UI", StringComparison.OrdinalIgnoreCase)
				|| d.Config.Contains("TextPrinter", StringComparison.OrdinalIgnoreCase))
			.ToList();
		if (uiDiffs.Count == 0)
		{
			sb.AppendLine("- UIConfiguration 与 TextPrintersConfiguration：**全部字段与原始一致（含 FontOptions/DefaultFont 及颜色字段）**。");
			sb.AppendLine("- **对话文字黑色问题的配置侧结论：配置字段无差异，配置资产侧排除为根因。**");
		}
		else
		{
			sb.AppendLine("- 存在差异（明细如下，须结合反编译证据判读）：");
			foreach (FieldDiff diff in uiDiffs)
			{
				sb.AppendLine($"  - {diff.Config}.{diff.Path}: source=`{ArtifactWriter.TruncateFragment(diff.SourceValue ?? "(absent)")}` exported=`{ArtifactWriter.TruncateFragment(diff.ExportedValue ?? "(absent)")}` ({diff.Kind})");
			}
		}
		sb.AppendLine();

		sb.AppendLine("## 反编译证据（REQ-C3/C5 强制证据化）");
		sb.AppendLine();
		if (evidence.Count == 0)
		{
			sb.AppendLine("- 无颜色/字体相关字段差异需要取证。");
		}
		else
		{
			sb.AppendLine("| Config | Field | Has evidence | Locations |");
			sb.AppendLine("|--------|-------|-------------|-----------|");
			foreach (EvidenceEntry entry in evidence)
			{
				sb.AppendLine($"| {entry.Config} | {entry.FieldPath} | {(entry.HasEvidence ? "YES" : "NO → 待验证假设")} | {string.Join("; ", entry.Locations.Take(5))} |");
			}
		}
		sb.AppendLine();

		sb.AppendLine("## 引用完整性（REQ-C4）");
		sb.AppendLine();
		if (result.BrokenReferences.Count == 0)
		{
			sb.AppendLine("- 断链引用数：**0**（全部配置声明的 GUID 引用在导出项目中可解析）。");
		}
		else
		{
			sb.AppendLine($"- 断链引用数: {result.BrokenReferences.Count}（明细见 c_reference_check.csv）");
			foreach (string broken in result.BrokenReferences.Take(20))
			{
				sb.AppendLine($"  - {broken}");
			}
		}

		File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
	}
}