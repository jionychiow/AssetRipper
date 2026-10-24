namespace AssetRipper.Tools.NaninovelAuditor.Modules.C;

/// <summary>
/// C模块 ILSpy 反编译证据检索（任务13，REQ-C3/C5）：
/// 对颜色/字体相关字段差异在 NaninovelDecomp 反编译源中定位字段读取点。
/// 无 DLL 证据的结论一律标注"待验证假设"。
/// </summary>
internal sealed class DecompEvidenceSearcher
{
	private static readonly string[] ColorFontFieldKeywords =
	[
		"FontOptions", "DefaultFont", "Color", "Tint", "TextPrinter",
	];

	public List<EvidenceEntry> Search(string decompDir, IReadOnlyList<FieldDiff> diffs)
	{
		List<EvidenceEntry> entries = [];
		if (!Directory.Exists(decompDir))
		{
			Logger2.Warn($"[c-evidence] Decomp directory not found: {decompDir} — all color/font verdicts will be marked as '待验证假设'.");
			return entries;
		}

		// 只检索与颜色/字体相关的差异字段
		IReadOnlyList<FieldDiff> relevantDiffs = diffs
			.Where(d => ColorFontFieldKeywords.Any(k => d.Path.Contains(k, StringComparison.OrdinalIgnoreCase)))
			.ToList();

		if (relevantDiffs.Count == 0)
		{
			return entries;
		}

		// 建立反编译源行索引（一次遍历）
		Dictionary<string, List<(string File, int Line)>> keywordHits = new(StringComparer.OrdinalIgnoreCase);
		foreach (string keyword in ColorFontFieldKeywords.Distinct(StringComparer.OrdinalIgnoreCase))
		{
			keywordHits[keyword] = [];
		}

		foreach (string file in Directory.EnumerateFiles(decompDir, "*.cs", SearchOption.AllDirectories))
		{
			try
			{
				string[] lines = File.ReadAllLines(file);
				for (int i = 0; i < lines.Length; i++)
				{
					foreach (string keyword in keywordHits.Keys)
					{
						if (lines[i].Contains(keyword, StringComparison.Ordinal))
						{
							keywordHits[keyword].Add((Path.GetFileName(file), i + 1));
						}
					}
				}
			}
			catch
			{
			}
		}

		foreach (FieldDiff diff in relevantDiffs)
		{
			List<(string File, int Line)> best = [];
			foreach (string keyword in ColorFontFieldKeywords)
			{
				if (diff.Path.Contains(keyword, StringComparison.OrdinalIgnoreCase)
					&& keywordHits.TryGetValue(keyword, out List<(string, int)>? hits))
				{
					best.AddRange(hits.Take(5));
				}
			}

			entries.Add(new EvidenceEntry(
				diff.Config,
				diff.Path,
				best.Count > 0,
				best.Select(h => $"{h.File}:{h.Line}").ToList()));
		}

		return entries;
	}
}

internal sealed record EvidenceEntry(string Config, string FieldPath, bool HasEvidence, IReadOnlyList<string> Locations);

/// <summary>轻量日志（避免依赖 AssetRipper Logger 的模块内部用）。</summary>
internal static class Logger2
{
	public static void Warn(string message) => Console.WriteLine("[warn] " + message);
}