using AssetRipper.Assets;
using AssetRipper.Assets.Collections;
using AssetRipper.Import.AssetCreation;
using AssetRipper.Import.Logging;
using AssetRipper.Import.Structure.Assembly.Serializable;
using AssetRipper.IO.Endian;
using AssetRipper.Export.Modules.Naninovel;
using AssetRipper.Processing;
using AssetRipper.SourceGenerated.Classes.ClassID_114;
using AssetRipper.SourceGenerated.Classes.ClassID_49;
using AssetRipper.Tools.NaninovelAuditor.Models;
using AssetRipper.Tools.NaninovelAuditor.Reporting;

namespace AssetRipper.Tools.NaninovelAuditor.Modules.A;

/// <summary>
/// nani 解码基准生成器（任务6）：复用 AssetRipper.Export.Modules.Naninovel 解码管线
/// （与导出同一解码器，隔离解码器差异），对原始资产实例生成 nani 基准文本与哈希。
/// 对比语义为"同一解码器双实例输出一致性"（design 1.2.4：原始数据不存在 .nani 明文）。
/// </summary>
internal sealed class NaniBaselineDecoder
{
	public const string ExportFailedMarker = ";[NaninovelExport] Failed to export";

	private readonly Dictionary<(string Collection, long PathId), IUnityObjectBase> assetIndex = new();

	/// <summary>建立 (collection 名, PathID) → 资产实例索引（供清单条目找回原始实例）。</summary>
	public void BuildAssetIndex(GameData gameData)
	{
		foreach (SerializedAssetCollection collection in gameData.GameBundle.FetchAssetCollections().OfType<SerializedAssetCollection>())
		{
			foreach (IUnityObjectBase asset in collection)
			{
				assetIndex[(collection.Name, asset.PathID)] = asset;
			}
		}
		Logger.Info(LogCategory.System, $"[baseline] Asset index built: {assetIndex.Count} entries.");
	}

	/// <summary>对全部 nani 清单条目生成基准文本哈希并回填；同时保留文本字典供内容对比（仅内存，不落盘全文）。</summary>
	public BaselineGenerationResult GenerateBaselines(SourceInventory inventory, AuditOptions options)
	{
		List<SourceInventoryEntry> updatedEntries = [];
		Dictionary<string, string> baselineTexts = new(StringComparer.OrdinalIgnoreCase);
		Dictionary<string, List<string>> baselineTextsById = new(StringComparer.OrdinalIgnoreCase);
		List<string> failures = [];
		List<(string Id, string Text)> samples = [];
		int sampleCount = Math.Max(1, Math.Min(options.SampleCount, 5));

		List<SourceInventoryEntry> naniEntries = inventory.Entries.Where(e => e.AssetType == NaninovelAssetType.NaniScript).ToList();
		List<string> sampleCandidates = SelectSamples(naniEntries, sampleCount);

		foreach (SourceInventoryEntry entry in naniEntries)
		{
			try
			{
				string? baselineText = GenerateBaselineText(entry, options.SourceDirectory);
				if (baselineText is null)
				{
					failures.Add($"{entry.AssetIdentifier} ({entry.DataSourcePath}): asset instance not found or empty");
					updatedEntries.Add(entry);
					continue;
				}
				string textSha = ArtifactWriter.ComputeSha256(System.Text.Encoding.UTF8.GetBytes(baselineText));
				updatedEntries.Add(entry with { BaselineTextSha256 = textSha });
				baselineTexts[entry.AssetIdentifier] = textSha;
				if (!baselineTextsById.TryGetValue(entry.AssetIdentifier, out List<string>? texts))
				{
					texts = [];
					baselineTextsById[entry.AssetIdentifier] = texts;
				}
				texts.Add(baselineText);
				if (sampleCandidates.Contains(entry.AssetIdentifier, StringComparer.OrdinalIgnoreCase))
				{
					samples.Add((entry.AssetIdentifier, baselineText));
				}
			}
			catch (Exception ex)
			{
				failures.Add($"{entry.AssetIdentifier} ({entry.DataSourcePath}): {ex.Message}");
				updatedEntries.Add(entry);
			}
		}

		// 非脚本条目原样保留
		updatedEntries.AddRange(inventory.Entries.Where(e => e.AssetType != NaninovelAssetType.NaniScript));

		return new BaselineGenerationResult(
			new SourceInventory { Entries = updatedEntries },
			baselineTexts,
			baselineTextsById,
			failures,
			samples);
	}

	private string? GenerateBaselineText(SourceInventoryEntry entry, string sourceDirectory)
	{
		if (entry.PathId == 0)
		{
			// 文件系统 mod 明文：直接读磁盘
			string relativePath = entry.DataSourcePath.Replace('/', Path.DirectorySeparatorChar);
			string fullPath = Path.Combine(sourceDirectory, relativePath);
			if (!File.Exists(fullPath))
			{
				return null;
			}
			return File.ReadAllText(fullPath, System.Text.Encoding.UTF8);
		}

		(string collection, long pathId) = ParseDataSourcePath(entry.DataSourcePath);
		if (!assetIndex.TryGetValue((collection, pathId), out IUnityObjectBase? asset))
		{
			return null;
		}

		if (asset is ITextAsset textAsset && asset.ClassName == "TextAsset")
		{
			return System.Text.Encoding.UTF8.GetString(textAsset.Script_C49.Data.ToArray());
		}

		if (asset is IMonoBehaviour monoBehaviour)
		{
			byte[]? structureData = GetMonoBehaviourStructureData(monoBehaviour);
			if (structureData is null || structureData.Length == 0)
			{
				return null;
			}
			return DecodeNaniText(structureData, monoBehaviour.Collection.EndianType);
		}

		return null;
	}

	private static string DecodeNaniText(byte[] structureData, EndianType endianType)
	{
		EndianSpanReader reader = new EndianSpanReader(structureData, endianType);
		reader.Align();
		int linesSize = reader.ReadInt32();
		List<int> lineRids = new(linesSize);
		for (int i = 0; i < linesSize; i++)
		{
			lineRids.Add(reader.ReadInt32());
		}

		NaniTypeLayoutTable layoutTable = new();
		CommandAliasMap commandAliasMap = new();
		ParameterEncoder parameterEncoder = new();
		CommandDecoder commandDecoder = new(commandAliasMap, layoutTable, parameterEncoder);
		NaniReferenceRegistryReader registryReader = new(layoutTable);

		Dictionary<long, NaniManagedReference> referenceMap = registryReader.Read(ref reader);

		ScriptLineDecoder lineDecoder = new(referenceMap, commandDecoder);
		NaniTextGenerator textGenerator = new(lineDecoder);

		byte[] naniBytes = textGenerator.Generate(lineRids, referenceMap);
		return System.Text.Encoding.UTF8.GetString(naniBytes);
	}

	private static byte[]? GetMonoBehaviourStructureData(IMonoBehaviour monoBehaviour)
	{
		if (SerializeReferenceDataCache.TryGetData(monoBehaviour, out byte[]? cachedData, out _))
		{
			return cachedData;
		}
		if (monoBehaviour.Structure is UnloadedStructure unloaded)
		{
			return unloaded.StructureData.ToArray();
		}
		return null;
	}

	private static (string collection, long pathId) ParseDataSourcePath(string dataSourcePath)
	{
		int hashIndex = dataSourcePath.LastIndexOf('#');
		if (hashIndex <= 0)
		{
			return (dataSourcePath, 0);
		}
		string collection = dataSourcePath[..hashIndex];
		long pathId = long.TryParse(dataSourcePath[(hashIndex + 1)..], out long parsed) ? parsed : 0;
		return (collection, pathId);
	}

	private static List<string> SelectSamples(List<SourceInventoryEntry> entries, int count)
	{
		// 覆盖特征：mod_ 开头 / 含富文本或复杂脚本 / 普通
		List<string> samples = [];
		List<SourceInventoryEntry> mods = entries.Where(e => e.AssetIdentifier.StartsWith("mod_", StringComparison.OrdinalIgnoreCase)).ToList();
		foreach (SourceInventoryEntry mod in mods.Take(count / 3))
		{
			samples.Add(mod.AssetIdentifier);
		}
		foreach (SourceInventoryEntry entry in entries.Where(e => e.ByteLength > 4096 && !samples.Contains(e.AssetIdentifier)))
		{
			if (samples.Count >= count)
			{
				break;
			}
			samples.Add(entry.AssetIdentifier);
		}
		return samples;
	}
}

internal sealed record BaselineGenerationResult(
	SourceInventory UpdatedInventory,
	Dictionary<string, string> BaselineTextSha256s,
	Dictionary<string, List<string>> BaselineTextsById,
	IReadOnlyList<string> Failures,
	IReadOnlyList<(string Id, string Text)> Samples);