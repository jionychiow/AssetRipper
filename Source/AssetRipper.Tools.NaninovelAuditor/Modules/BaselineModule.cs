using AssetRipper.Import.Logging;
using AssetRipper.Tools.NaninovelAuditor.Inventory;
using AssetRipper.Tools.NaninovelAuditor.Models;
using AssetRipper.Tools.NaninovelAuditor.Modules.A;
using AssetRipper.Tools.NaninovelAuditor.Reporting;
using AssetRipper.Tools.NaninovelAuditor.Session;

namespace AssetRipper.Tools.NaninovelAuditor.Modules;

/// <summary>
/// 任务6入口：原始清单 → nani 解码基准生成 → 回填 BaselineTextSha256 →
/// 输出 source_inventory_baseline.json + baseline_texts.json + baseline_samples/。
/// </summary>
public sealed class BaselineModule : INaninovelAuditModule
{
	public string ModuleName => "baseline";

	public AuditRunResult Execute(AuditSession session)
	{
		Logger.Info(LogCategory.System, "[baseline] Building source inventory...");
		SourceInventoryBuilder inventoryBuilder = new();
		SourceInventory inventory = inventoryBuilder.Build(session.GameData, session.Options);

		Logger.Info(LogCategory.System, "[baseline] Building asset index...");
		NaniBaselineDecoder decoder = new();
		decoder.BuildAssetIndex(session.GameData);

		Logger.Info(LogCategory.System, "[baseline] Generating baseline texts...");
		BaselineGenerationResult result = decoder.GenerateBaselines(inventory, session.Options);

		string invPath = Path.Combine(session.Options.ReportDirectory, "source_inventory_baseline.json");
		string textsPath = Path.Combine(session.Options.ReportDirectory, "baseline_texts.json");
		string samplesDir = Path.Combine(session.Options.ReportDirectory, "baseline_samples");

		ArtifactWriter.WriteJson(invPath, result.UpdatedInventory);
		ArtifactWriter.WriteJson(textsPath, result.BaselineTextSha256s);

		Directory.CreateDirectory(samplesDir);
		foreach ((string id, string text) in result.Samples)
		{
			string safeName = string.Concat(id.Split(Path.GetInvalidFileNameChars()));
			File.WriteAllText(Path.Combine(samplesDir, $"{safeName}.baseline.nani"), text, System.Text.Encoding.UTF8);
		}

		int generated = result.BaselineTextSha256s.Count;
		int total = result.UpdatedInventory.Entries.Count(e => e.AssetType == NaninovelAssetType.NaniScript);
		Logger.Info(LogCategory.System, $"[baseline] Done: generated={generated}/{total}, failures={result.Failures.Count}, samples={result.Samples.Count}");

		return new AuditRunResult
		{
			ModuleName = ModuleName,
			Success = result.Failures.Count == 0,
			Summary = $"baselineGenerated={generated}/{total}; failures={result.Failures.Count}; samples={result.Samples.Count}",
			ProducedArtifacts = [invPath, textsPath, samplesDir],
		};
	}
}