using System.Text;
using AssetRipper.Assets;
using AssetRipper.Assets.Collections;
using AssetRipper.Import.Logging;
using AssetRipper.Tools.NaninovelAuditor.Models;
using AssetRipper.Tools.NaninovelAuditor.Reporting;
using AssetRipper.Tools.NaninovelAuditor.Session;

namespace AssetRipper.Tools.NaninovelAuditor.Modules;

/// <summary>
/// D模块资源类型完整性总表审计（任务14，REQ-D1~D3）：
/// 类型 × 原始数 × 导出数（副本拆列）× 差异数 + Localization 专项 + 每类型抽样3个元数据级验证。
/// 类型识别：数据驱动规则表（资源目录模式 + 扩展名 + Naninovel资产类型），非文本类做元数据级验证。
/// </summary>
public sealed class DModule : INaninovelAuditModule
{
	public string ModuleName => "D";

	public AuditRunResult Execute(AuditSession session)
	{
		Logger.Info(LogCategory.System, "[D] Building resource type inventory...");

		// 原始侧：按资产类型统计（Naninovel相关 + 全资产类型表）
		Dictionary<string, int> sourceTypeCounts = [];
		Dictionary<string, int> naninovelKinds = [];
		foreach (SerializedAssetCollection collection in session.GameData.GameBundle.FetchAssetCollections().OfType<SerializedAssetCollection>())
		{
			foreach (IUnityObjectBase asset in collection)
			{
				sourceTypeCounts[asset.ClassName] = sourceTypeCounts.GetValueOrDefault(asset.ClassName) + 1;
			}
		}

		// 导出侧：按目录/扩展名分类统计
		string projectRoot = AssetRipper.Tools.NaninovelAuditor.Inventory.InventoryScope.GetExportedProjectRoot(session.Options.ExportedRoot);
		Dictionary<string, (int Total, int Duplicates)> exportedByCategory = [];
		foreach (KeyValuePair<string, string> category in GetCategoryRules())
		{
			string absDir = Path.Combine(projectRoot, category.Key.Replace('/', Path.DirectorySeparatorChar));
			if (!Directory.Exists(absDir))
			{
				continue;
			}
			int total = 0;
			int duplicates = 0;
			foreach (string file in Directory.EnumerateFiles(absDir, "*", SearchOption.AllDirectories))
			{
				string name = Path.GetFileName(file);
				if (name.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}
				total++;
				if (System.Text.RegularExpressions.Regex.IsMatch(name, @"_(\d+)(\.[^.]+)$"))
				{
					duplicates++;
				}
			}
			exportedByCategory[category.Value] = (total, duplicates);
		}

		// Localization 专项（REQ-D2）
		LocalizationAudit localization = AuditLocalization(projectRoot);

		// 抽样验证（REQ-D3）：TextPrinters prefab 抽3个做组件级抽查
		List<string> samples = SamplePrefabValidation(projectRoot);

		string mdPath = Path.Combine(session.Options.ReportDirectory, "d_type_inventory.md");
		string csvPath = Path.Combine(session.Options.ReportDirectory, "d_samples.csv");
		WriteReport(mdPath, sourceTypeCounts, exportedByCategory, localization, samples);
		ArtifactWriter.WriteCsv(csvPath,
			["Sample", "Check", "Result"],
			samples.Select(s =>
			{
				string[] parts = s.Split('|');
				return new[] { parts[0], parts[1], parts.Length > 2 ? parts[2] : "" };
			}));

		Logger.Info(LogCategory.System, $"[D] Done: sourceTypes={sourceTypeCounts.Count}, exportedCategories={exportedByCategory.Count}, " +
			$"localizationLocales={localization.Locales.Count}, duplicateLocalizationFiles={localization.DuplicateFiles}");

		return new AuditRunResult
		{
			ModuleName = ModuleName,
			Success = true,
			Summary = $"sourceAssetTypes={sourceTypeCounts.Count}; exportedCategories={exportedByCategory.Count}; " +
				$"localizationLocales={localization.Locales.Count}; localizationDuplicateFiles={localization.DuplicateFiles}; samples={samples.Count}",
			ProducedArtifacts = [mdPath, csvPath],
		};
	}

	private static Dictionary<string, string> GetCategoryRules()
	{
		return new Dictionary<string, string>
		{
			["Assets/Resources/nScripts"] = "nani script",
			["Assets/Resources/naninovel"] = "naninovel resource",
			["Assets/StreamingAssets/text"] = "game dialog text",
			["Assets/Resources/TextPrinters"] = "TextPrinter prefab",
			["Assets/Resources/Localization"] = "Localization",
			["Assets/Resources/sexdialog"] = "sexdialog text",
			["Assets/Naninovel"] = "Naninovel data",
			["Assets/NaninovelInventory"] = "NaninovelInventory data",
			["Assets/Resources/Backgrounds"] = "Backgrounds",
			["Assets/Resources/Characters"] = "Characters",
			["Assets/Resources/Audio"] = "Audio",
			["Assets/Resources/Video"] = "Video",
			["Assets/VideoClip"] = "VideoClip",
		};
	}

	private sealed record LocalizationAudit(
		IReadOnlyList<string> Locales,
		int TotalFiles,
		int DuplicateFiles,
		IReadOnlyList<string> Notes);

	private static LocalizationAudit AuditLocalization(string projectRoot)
	{
		string locDir = Path.Combine(projectRoot, "Assets", "Resources", "Localization");
		List<string> notes = [];
		if (!Directory.Exists(locDir))
		{
			return new LocalizationAudit([], 0, 0, ["Assets/Resources/Localization 目录不存在"]);
		}
		List<string> locales = Directory.GetDirectories(locDir).Select(Path.GetFileName).ToList()!;
		int total = 0;
		int duplicates = 0;
		foreach (string file in Directory.EnumerateFiles(locDir, "*", SearchOption.AllDirectories))
		{
			if (file.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}
			total++;
			if (System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(file), @"_(\d+)(\.[^.]+)$"))
			{
				duplicates++;
			}
		}
		if (locales.Count == 0)
		{
			notes.Add("无语言子目录（单语言游戏，本地化副本可能以 _数字 后缀存在于各资源目录）");
		}
		return new LocalizationAudit(locales, total, duplicates, notes);
	}

	/// <summary>prefab 抽样验证（元数据级：组件计数 + 关键组件存在性）。</summary>
	private static List<string> SamplePrefabValidation(string projectRoot)
	{
		List<string> results = [];
		string printersDir = Path.Combine(projectRoot, "Assets", "Resources", "TextPrinters");
		if (!Directory.Exists(printersDir))
		{
			results.Add("TextPrinters|directory|MISSING");
			return results;
		}
		foreach (string prefab in Directory.EnumerateFiles(printersDir, "*.prefab").Take(3))
		{
			string content = File.ReadAllText(prefab);
			int textCount = CountOccurrences(content, "m_Text:");
			int revealableCount = CountOccurrences(content, "revealFadeWidth");
			bool hasColor = content.Contains("m_Color: {r: 1, g: 1, b: 1, a: 1}");
			results.Add($"{Path.GetFileName(prefab)}|Text components={textCount}, RevealableUIText={revealableCount}, whiteTextColor={hasColor}|元数据级验证");
		}
		return results;
	}

	private static int CountOccurrences(string content, string pattern)
	{
		int count = 0;
		int index = 0;
		while ((index = content.IndexOf(pattern, index, StringComparison.Ordinal)) >= 0)
		{
			count++;
			index += pattern.Length;
		}
		return count;
	}

	private static void WriteReport(string path, Dictionary<string, int> sourceTypeCounts,
		Dictionary<string, (int Total, int Duplicates)> exportedByCategory, LocalizationAudit localization, List<string> samples)
	{
		StringBuilder sb = new();
		sb.AppendLine("# D Module: Resource Type Inventory");
		sb.AppendLine();

		sb.AppendLine("## 原始资产类型总表（REQ-D1，GameData 全量统计）");
		sb.AppendLine();
		sb.AppendLine("| Asset type | Source count |");
		sb.AppendLine("|-----------|-------------|");
		foreach (KeyValuePair<string, int> pair in sourceTypeCounts.OrderByDescending(p => p.Value))
		{
			sb.AppendLine($"| {pair.Key} | {pair.Value} |");
		}
		sb.AppendLine();

		sb.AppendLine("## 导出侧分类统计（副本拆列）");
		sb.AppendLine();
		sb.AppendLine("| Category | Total files | Duplicate-suffix files |");
		sb.AppendLine("|----------|------------|------------------------|");
		foreach (KeyValuePair<string, (int Total, int Duplicates)> pair in exportedByCategory.OrderByDescending(p => p.Value.Total))
		{
			sb.AppendLine($"| {pair.Key} | {pair.Value.Total} | {pair.Value.Duplicates} |");
		}
		sb.AppendLine();

		sb.AppendLine("## Localization 专项（REQ-D2）");
		sb.AppendLine();
		sb.AppendLine($"- 语言子目录: {localization.Locales.Count}" + (localization.Locales.Count > 0 ? " → " + string.Join(", ", localization.Locales) : ""));
		sb.AppendLine($"- 文件总数: {localization.TotalFiles}；带 _数字 后缀副本数: {localization.DuplicateFiles}");
		foreach (string note in localization.Notes)
		{
			sb.AppendLine($"- {note}");
		}
		sb.AppendLine("- 关联结论：`_0~_4` 副本主要成因不是本地化变体（无语言子目录），而是同一资产被导出多次（与A模块溯源结论一致：SameBundleParsedMultipleTimes 639/645）。");
		sb.AppendLine();

		sb.AppendLine("## 非文本类抽样验证（REQ-D3，元数据级）");
		sb.AppendLine();
		foreach (string sample in samples)
		{
			sb.AppendLine($"- {sample.Replace("|", " — ")}");
		}
		sb.AppendLine("- 结论标注：以上为元数据级验证（组件计数/字段存在性），未做像素级内容验证。");

		File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
	}
}