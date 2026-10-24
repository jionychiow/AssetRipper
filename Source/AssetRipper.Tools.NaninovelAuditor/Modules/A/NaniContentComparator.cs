using System.Text;
using AssetRipper.Tools.NaninovelAuditor.Inventory;
using AssetRipper.Tools.NaninovelAuditor.Models;
using AssetRipper.Tools.NaninovelAuditor.Reporting;

namespace AssetRipper.Tools.NaninovelAuditor.Modules.A;

/// <summary>
/// A模块全量内容对比（任务8）：原始基准文本 vs 磁盘 .nani 文件内容，
/// 三级判定（逐字节一致 / 规范化后一致 / 实质差异）+ 抽样验证 + 空文件/占位检测（REQ-A3/A5）。
/// </summary>
internal sealed class NaniContentComparator
{
	private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];

	public ContentAuditResult Compare(
		Dictionary<string, List<string>> baselineTextsById,
		ExportedInventory exportedInventory,
		string exportedRoot,
		int sampleCount)
	{
		string scriptsDir = InventoryScope.GetExportedScriptsDir(exportedRoot);

		List<(string Id, string Text)> allBaselines = [];
		foreach (KeyValuePair<string, List<string>> pair in baselineTextsById)
		{
			allBaselines.AddRange(pair.Value.Select(text => (pair.Key, text)));
		}

		List<ContentComparisonRow> rows = [];
		List<DiffRecord> diffs = [];
		List<NaniContentDiff> contentDiffs = [];
		int diffId = 1;

		foreach (IGrouping<string, ExportedInventoryEntry> group in exportedInventory.Entries
			.Where(e => e.ExportedPath.EndsWith(".nani", StringComparison.OrdinalIgnoreCase))
			.GroupBy(e => e.BaseAssetIdentifier, StringComparer.OrdinalIgnoreCase))
		{
			List<string>? baselines = baselineTextsById.TryGetValue(group.Key, out List<string>? texts) ? texts : null;

			foreach (ExportedInventoryEntry entry in group)
			{
				string fullPath = Path.Combine(exportedRoot, InventoryScope.ExportedProjectName, entry.ExportedPath.Replace('/', Path.DirectorySeparatorChar));
				if (!File.Exists(fullPath))
				{
					rows.Add(new ContentComparisonRow(group.Key, entry.ExportedPath, ContentMatchLevel.SubstantiveDifference, "file not found"));
					diffs.Add(new DiffRecord
					{
						DiffId = diffId++,
						ExportedPath = entry.ExportedPath,
						SourcePath = "(baseline)",
						DiffType = DiffType.QuantityMissing,
						Severity = Severity.Fatal,
						RelatedModule = AuditModule.A,
						Detail = "Exported nani file listed in inventory but missing on disk.",
					});
					continue;
				}

				byte[] exportedBytes = File.ReadAllBytes(fullPath);

				// 空文件检测（spec 5.1.3-2：最高严重级）
				if (exportedBytes.Length == 0)
				{
					rows.Add(new ContentComparisonRow(group.Key, entry.ExportedPath, ContentMatchLevel.SubstantiveDifference, "empty file (0 bytes)"));
					diffs.Add(new DiffRecord
					{
						DiffId = diffId++,
						ExportedPath = entry.ExportedPath,
						SourcePath = baselines is not null ? $"baseline x{baselines.Count}" : "(no baseline)",
						DiffType = DiffType.EmptyFile,
						Severity = Severity.Fatal,
						RelatedModule = AuditModule.A,
						Detail = "Exported nani file is empty (0 bytes).",
					});
					continue;
				}

				string exportedText = Encoding.UTF8.GetString(exportedBytes);

				// 占位标记检测（与导出失败占位同源）
				if (exportedText.Contains(NaniBaselineDecoder.ExportFailedMarker, StringComparison.Ordinal))
				{
					rows.Add(new ContentComparisonRow(group.Key, entry.ExportedPath, ContentMatchLevel.SubstantiveDifference, "export-failed placeholder marker"));
					diffs.Add(new DiffRecord
					{
						DiffId = diffId++,
						ExportedPath = entry.ExportedPath,
						SourcePath = baselines is not null ? $"baseline x{baselines.Count}" : "(no baseline)",
						DiffType = DiffType.EmptyFile,
						Severity = Severity.Fatal,
						RelatedModule = AuditModule.A,
						Detail = "Exported nani file contains export-failure placeholder marker.",
					});
					continue;
				}

				if (baselines is null || baselines.Count == 0)
				{
					rows.Add(new ContentComparisonRow(group.Key, entry.ExportedPath, ContentMatchLevel.SubstantiveDifference, "no baseline generated"));
					continue;
				}

				// 三级判定：逐字节 → 规范化 → 实质差异
				byte[] baselineBytes = Encoding.UTF8.GetBytes(baselines[0]);
				if (exportedBytes.AsSpan().SequenceEqual(baselineBytes.AsSpan()) || MatchesAnyBaseline(exportedBytes, baselines))
				{
					rows.Add(new ContentComparisonRow(group.Key, entry.ExportedPath, ContentMatchLevel.ByteIdentical, ""));
					continue;
				}

				string normalizedExported = Normalize(exportedText);
				bool normalizedMatch = false;
				string matchedReason = "";
				foreach (string baseline in baselines)
				{
					if (Normalize(baseline) == normalizedExported)
					{
						normalizedMatch = true;
						matchedReason = "normalized (BOM/line-ending)";
						break;
					}
				}
				if (normalizedMatch)
				{
					rows.Add(new ContentComparisonRow(group.Key, entry.ExportedPath, ContentMatchLevel.NormalizedIdentical, matchedReason));
					continue;
				}

				// 实质差异：最小差异片段
				List<MinimalDiffFragment> fragments = ExtractMinimalFragments(baselines[0], exportedText);
				rows.Add(new ContentComparisonRow(group.Key, entry.ExportedPath, ContentMatchLevel.SubstantiveDifference, $"{fragments.Count} fragment(s)"));
				contentDiffs.Add(new NaniContentDiff
				{
					ScriptId = new NaniScriptId { BaseName = group.Key, ExportedFilePath = entry.ExportedPath },
					MatchLevel = ContentMatchLevel.SubstantiveDifference,
					Fragments = fragments,
					NormalizationReason = "",
				});
				diffs.Add(new DiffRecord
				{
					DiffId = diffId++,
					ExportedPath = entry.ExportedPath,
					SourcePath = $"baseline:{group.Key}",
					DiffType = DiffType.ContentDifference,
					Severity = Severity.Severe,
					RelatedModule = AuditModule.A,
					Detail = $"Substantive difference: {fragments.Count} fragment(s), first at source line {fragments[0].SourceLine}.",
				});
			}
		}

		List<string> samples = SelectSamples(rows, exportedInventory, exportedRoot, sampleCount);
		ContentStats stats = BuildStats(rows);

		return new ContentAuditResult(rows, diffs, contentDiffs, samples, stats);
	}

	private static bool MatchesAnyBaseline(byte[] exportedBytes, List<string> baselines)
	{
		foreach (string baseline in baselines)
		{
			if (exportedBytes.AsSpan().SequenceEqual(Encoding.UTF8.GetBytes(baseline).AsSpan()))
			{
				return true;
			}
		}
		return false;
	}

	private static string Normalize(string text)
	{
		string noBom = text.Length > 0 && text[0] == '\uFEFF' ? text[1..] : text;
		return noBom.Replace("\r\n", "\n").Replace("\r", "\n");
	}

	private static List<MinimalDiffFragment> ExtractMinimalFragments(string baseline, string exported, int maxFragments = 5)
	{
		List<MinimalDiffFragment> fragments = [];
		string[] baselineLines = Normalize(baseline).Split('\n');
		string[] exportedLines = Normalize(exported).Split('\n');

		int maxLines = Math.Max(baselineLines.Length, exportedLines.Length);
		for (int i = 0; i < maxLines && fragments.Count < maxFragments; i++)
		{
			string baselineLine = i < baselineLines.Length ? baselineLines[i] : "(missing)";
			string exportedLine = i < exportedLines.Length ? exportedLines[i] : "(missing)";
			if (baselineLine != exportedLine)
			{
				fragments.Add(new MinimalDiffFragment
				{
					SourceLine = i + 1,
					ExportedLine = i + 1,
					SourceFragment = ArtifactWriter.TruncateFragment(baselineLine),
					ExportedFragment = ArtifactWriter.TruncateFragment(exportedLine),
				});
			}
		}
		return fragments;
	}

	/// <summary>抽样集构造（REQ-A3）：覆盖五类特征（富文本/@style/@include/mod_/普通）。</summary>
	private static List<string> SelectSamples(List<ContentComparisonRow> rows, ExportedInventory inventory, string exportedRoot, int sampleCount)
	{
		Dictionary<string, List<string>> categories = new()
		{
			["richtext"] = [],
			["style"] = [],
			["include"] = [],
			["mod"] = [],
			["normal"] = [],
		};

		foreach (ContentComparisonRow row in rows)
		{
			string fullRelative = row.ExportedPath;
			if (!fullRelative.EndsWith(".nani", StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}
			string fullPath = Path.Combine(exportedRoot, InventoryScope.ExportedProjectName, fullRelative.Replace('/', Path.DirectorySeparatorChar));
			if (!File.Exists(fullPath))
			{
				continue;
			}
			string content = File.ReadAllText(fullPath, Encoding.UTF8);
			string id = row.BaseId;
			if (id.StartsWith("mod_", StringComparison.OrdinalIgnoreCase) || content.Contains("#Readme", StringComparison.Ordinal))
			{
				categories["mod"].Add(id);
			}
			else if (content.Contains("<color=", StringComparison.OrdinalIgnoreCase) || content.Contains("[br]", StringComparison.Ordinal))
			{
				categories["richtext"].Add(id);
			}
			else if (content.Contains("@style", StringComparison.Ordinal))
			{
				categories["style"].Add(id);
			}
			else if (content.Contains("@include", StringComparison.OrdinalIgnoreCase))
			{
				categories["include"].Add(id);
			}
			else
			{
				categories["normal"].Add(id);
			}
		}

		List<string> samples = [];
		int perCategory = Math.Max(1, sampleCount / 5);
		foreach (KeyValuePair<string, List<string>> category in categories)
		{
			foreach (string id in category.Value.Distinct(StringComparer.OrdinalIgnoreCase).Take(perCategory))
			{
				samples.Add($"{id} (category={category.Key})");
			}
		}
		return samples;
	}

	private static ContentStats BuildStats(List<ContentComparisonRow> rows)
	{
		int byteIdentical = rows.Count(r => r.MatchLevel == ContentMatchLevel.ByteIdentical);
		int normalized = rows.Count(r => r.MatchLevel == ContentMatchLevel.NormalizedIdentical);
		int substantive = rows.Count(r => r.MatchLevel == ContentMatchLevel.SubstantiveDifference);
		return new ContentStats(byteIdentical, normalized, substantive, rows.Count);
	}
}

internal sealed record ContentComparisonRow(string BaseId, string ExportedPath, ContentMatchLevel MatchLevel, string Note);

internal sealed record ContentStats(int ByteIdentical, int NormalizedIdentical, int SubstantiveDifference, int Total);

internal sealed record ContentAuditResult(
	IReadOnlyList<ContentComparisonRow> Rows,
	IReadOnlyList<DiffRecord> Diffs,
	IReadOnlyList<NaniContentDiff> ContentDiffs,
	IReadOnlyList<string> Samples,
	ContentStats Stats);