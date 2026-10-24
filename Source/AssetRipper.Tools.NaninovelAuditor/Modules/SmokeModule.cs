using System.Text;
using AssetRipper.Assets;
using AssetRipper.Assets.Collections;
using AssetRipper.Import.Logging;
using AssetRipper.Import.Structure.Assembly;
using AssetRipper.SourceGenerated.Classes.ClassID_114;
using AssetRipper.SourceGenerated.Classes.ClassID_115;
using AssetRipper.Tools.NaninovelAuditor.Session;

namespace AssetRipper.Tools.NaninovelAuditor.Modules;

/// <summary>
/// 冒烟模块（任务3）：资产枚举统计——各 collection 资产计数、TextAsset 总数、
/// Naninovel 相关 MonoBehaviour 初筛计数；产物 smoke_report.md。
/// </summary>
public sealed class SmokeModule : INaninovelAuditModule
{
	private const string TargetScriptClassName = "Script";
	private const string TargetScriptNamespace = "Naninovel";
	private const string TargetScriptAssembly = "Elringus.Naninovel.Runtime";

	public string ModuleName => "smoke";

	public AuditRunResult Execute(AuditSession session)
	{
		Logger.Info(LogCategory.System, "[smoke] Enumerating asset collections...");

		int totalAssets = 0;
		Dictionary<string, int> typeCounts = new(StringComparer.OrdinalIgnoreCase);
		List<CollectionStats> collectionStats = [];
		int naninovelScriptCount = 0;
		Dictionary<string, int> naninovelComponentCounts = new(StringComparer.OrdinalIgnoreCase);
		int monoBehaviourTotal = 0;
		int scriptPFailed = 0;

		foreach (SerializedAssetCollection collection in session.GameData.GameBundle.FetchAssetCollections().OfType<SerializedAssetCollection>())
		{
			int collectionCount = 0;
			foreach (IUnityObjectBase asset in collection)
			{
				totalAssets++;
				collectionCount++;
				string className = asset.ClassName;
				typeCounts[className] = typeCounts.GetValueOrDefault(className) + 1;

				if (asset is IMonoBehaviour monoBehaviour)
				{
					monoBehaviourTotal++;
					string? scriptInfo = TryGetScriptInfo(monoBehaviour);
					if (scriptInfo is null)
					{
						scriptPFailed++;
						continue;
					}
					string[] parts = scriptInfo.Split('|');
					string ns = parts[0];
					string classNameOfScript = parts[1];
					string asm = parts[2];

					if (classNameOfScript == TargetScriptClassName && ns == TargetScriptNamespace && asm == TargetScriptAssembly)
					{
						naninovelScriptCount++;
					}
					if (ns.Contains("Naninovel", StringComparison.OrdinalIgnoreCase) || asm.Contains("Naninovel", StringComparison.OrdinalIgnoreCase))
					{
						string key = $"{ns}/{classNameOfScript}";
						naninovelComponentCounts[key] = naninovelComponentCounts.GetValueOrDefault(key) + 1;
					}
				}
			}
			collectionStats.Add(new CollectionStats(collection.Name, ClassifyCollection(collection.Name), collectionCount));
		}

		string reportPath = Path.Combine(session.Options.ReportDirectory, "smoke_report.md");
		WriteReport(reportPath, totalAssets, typeCounts, collectionStats, naninovelScriptCount,
			naninovelComponentCounts, monoBehaviourTotal, scriptPFailed);

		Logger.Info(LogCategory.System, $"[smoke] Done: totalAssets={totalAssets}, collections={collectionStats.Count}, " +
			$"TextAsset={typeCounts.GetValueOrDefault("TextAsset")}, MonoBehaviour={monoBehaviourTotal}, " +
			$"NaninovelScript={naninovelScriptCount}, NaninovelComponents={naninovelComponentCounts.Count} kinds.");

		return new AuditRunResult
		{
			ModuleName = ModuleName,
			Success = true,
			Summary = $"totalAssets={totalAssets}; collections={collectionStats.Count}; TextAsset={typeCounts.GetValueOrDefault("TextAsset")}; " +
				$"MonoBehaviour={monoBehaviourTotal}; NaninovelScript={naninovelScriptCount}; naninovelComponentKinds={naninovelComponentCounts.Count}; scriptPFailed={scriptPFailed}",
			ProducedArtifacts = [reportPath],
		};
	}

	private static string? TryGetScriptInfo(IMonoBehaviour monoBehaviour)
	{
		try
		{
			IMonoScript? script = monoBehaviour.ScriptP;
			if (script is null)
			{
				return null;
			}
			return $"{script.Namespace.String}|{script.ClassName_R.String}|{script.GetValidAssemblyName()}";
		}
		catch
		{
			return null;
		}
	}

	/// <summary>数据源归类：主数据 / sharedassets / level场景 / Addressables bundle。</summary>
	private static string ClassifyCollection(string name)
	{
		if (name.StartsWith("cab-", StringComparison.OrdinalIgnoreCase))
		{
			return "Addressables bundle";
		}
		if (name.StartsWith("sharedassets", StringComparison.OrdinalIgnoreCase))
		{
			return "sharedassets";
		}
		if (name.StartsWith("level", StringComparison.OrdinalIgnoreCase))
		{
			return "level scene";
		}
		return "main data";
	}

	private static void WriteReport(string path, int totalAssets, Dictionary<string, int> typeCounts,
		List<CollectionStats> collectionStats, int naninovelScriptCount, Dictionary<string, int> naninovelComponentCounts,
		int monoBehaviourTotal, int scriptPFailed)
	{
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		StringBuilder sb = new();
		sb.AppendLine("# Naninovel Audit Smoke Report");
		sb.AppendLine();
		sb.AppendLine($"- Total assets: {totalAssets}");
		sb.AppendLine($"- Collections: {collectionStats.Count}");
		sb.AppendLine($"- MonoBehaviour total: {monoBehaviourTotal} (ScriptP unresolved: {scriptPFailed})");
		sb.AppendLine($"- Naninovel Script assets (Class={TargetScriptClassName}, NS={TargetScriptNamespace}, Asm={TargetScriptAssembly}): {naninovelScriptCount}");
		sb.AppendLine();

		sb.AppendLine("## Asset counts by type");
		sb.AppendLine();
		sb.AppendLine("| Type | Count |");
		sb.AppendLine("|------|-------|");
		foreach (KeyValuePair<string, int> pair in typeCounts.OrderByDescending(p => p.Value))
		{
			sb.AppendLine($"| {pair.Key} | {pair.Value} |");
		}
		sb.AppendLine();

		sb.AppendLine("## Asset counts by collection");
		sb.AppendLine();
		sb.AppendLine("| Collection | Category | Assets |");
		sb.AppendLine("|-----------|----------|--------|");
		foreach (CollectionStats stats in collectionStats.OrderByDescending(s => s.AssetCount))
		{
			sb.AppendLine($"| {stats.Name} | {stats.Category} | {stats.AssetCount} |");
		}
		sb.AppendLine();

		sb.AppendLine("## Naninovel MonoBehaviour kinds (namespace/class)");
		sb.AppendLine();
		sb.AppendLine("| Namespace/Class | Count |");
		sb.AppendLine("|----------------|-------|");
		foreach (KeyValuePair<string, int> pair in naninovelComponentCounts.OrderByDescending(p => p.Value))
		{
			sb.AppendLine($"| {pair.Key} | {pair.Value} |");
		}

		File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
	}

	private sealed record CollectionStats(string Name, string Category, int AssetCount);
}