using AssetRipper.Assets;
using AssetRipper.Assets.Collections;
using AssetRipper.Import.AssetCreation;
using AssetRipper.Import.Logging;
using AssetRipper.Import.Structure.Assembly;
using AssetRipper.Import.Structure.Assembly.Serializable;
using AssetRipper.Processing;
using AssetRipper.SourceGenerated.Classes.ClassID_114;
using AssetRipper.SourceGenerated.Classes.ClassID_115;
using AssetRipper.SourceGenerated.Classes.ClassID_49;
using AssetRipper.Tools.NaninovelAuditor.Models;
using AssetRipper.Tools.NaninovelAuditor.Reporting;

namespace AssetRipper.Tools.NaninovelAuditor.Inventory;

/// <summary>
/// 原始权威清单构建器（任务4）：从 GameData 枚举全部 Naninovel 资产（nani 脚本 / ManagedText 文档 /
/// Configuration 配置），逐条记录数据源路径、字节长度、SHA-256、权威版本标记；
/// 并纳入文件系统 mod_*.nani 磁盘明文。
/// </summary>
internal sealed class SourceInventoryBuilder
{
	private const string ResourcesPrefix = "Assets/Resources/";

	public SourceInventory Build(GameData gameData, AuditOptions options)
	{
		List<SourceInventoryEntry> entries = [];
		List<string> bundleParseFailures = [];

		foreach (SerializedAssetCollection collection in gameData.GameBundle.FetchAssetCollections().OfType<SerializedAssetCollection>())
		{
			try
			{
				foreach (IUnityObjectBase asset in collection)
				{
					ProcessAsset(asset, collection, entries);
				}
			}
			catch (Exception ex)
			{
				bundleParseFailures.Add($"{collection.Name}: {ex.Message}");
			}
		}

		AddFilesystemScripts(options, entries);

		MarkAuthoritativeVersions(entries);

		return new SourceInventory { Entries = entries };
	}

	private static void ProcessAsset(IUnityObjectBase asset, SerializedAssetCollection collection, List<SourceInventoryEntry> entries)
	{
		string dataSourcePath = $"{collection.Name}#{asset.PathID}";

		// 注意：ITextAsset 接口被所有带 m_Script 字段的类实现（TemplateAsset/VisualTreeAsset 等），
		// 必须以 ClassName 精确限定真正的 TextAsset（ClassID_49）。
		if (asset is ITextAsset textAsset && asset.ClassName == "TextAsset")
		{
			byte[] data = textAsset.Script_C49.Data.ToArray();
			string name = (asset as INamed)?.Name ?? string.Empty;
			string originalPath = asset.OriginalPath ?? string.Empty;
			NaninovelAssetType assetType = name.EndsWith(".nani", StringComparison.OrdinalIgnoreCase)
				|| originalPath.EndsWith(".nani", StringComparison.OrdinalIgnoreCase)
				? NaninovelAssetType.NaniScript
				: NaninovelAssetType.ManagedText;
			entries.Add(new SourceInventoryEntry
			{
				AssetType = assetType,
				AssetIdentifier = StripExtension(name),
				DataSourcePath = dataSourcePath,
				ByteLength = data.Length,
				Sha256 = ArtifactWriter.ComputeSha256(data),
				IsAuthoritative = false,
				AuthorityBasis = ClassifyAuthority(asset),
				PathId = asset.PathID,
			});
			return;
		}

		if (asset is IMonoBehaviour monoBehaviour)
		{
			string? scriptInfo = TryGetScriptInfo(monoBehaviour);
			if (scriptInfo is null)
			{
				return;
			}
			string[] parts = scriptInfo.Split('|');
			string ns = parts[0];
			string scriptClassName = parts[1];
			string asm = parts[2];

			bool isNaninovelScript = scriptClassName == InventoryScope.TargetScriptClassName
				&& ns == InventoryScope.TargetScriptNamespace
				&& asm == InventoryScope.TargetScriptAssembly;
			bool isConfiguration = ns == InventoryScope.TargetScriptNamespace
				&& scriptClassName.EndsWith("Configuration", StringComparison.Ordinal)
				&& asm == InventoryScope.TargetScriptAssembly;

			if (!isNaninovelScript && !isConfiguration)
			{
				return;
			}

			byte[]? rawData = TryGetMonoBehaviourRawData(monoBehaviour, out bool rawDataAvailable);
			entries.Add(new SourceInventoryEntry
			{
				AssetType = isNaninovelScript ? NaninovelAssetType.NaniScript : NaninovelAssetType.Configuration,
				AssetIdentifier = (asset as INamed)?.Name ?? string.Empty,
				DataSourcePath = dataSourcePath,
				ByteLength = rawData?.Length ?? 0,
				Sha256 = rawData is not null ? ArtifactWriter.ComputeSha256(rawData) : "",
				IsAuthoritative = false,
				AuthorityBasis = rawDataAvailable ? ClassifyAuthority(asset) : "raw data unavailable",
				PathId = asset.PathID,
			});
		}
	}

	private static byte[]? TryGetMonoBehaviourRawData(IMonoBehaviour monoBehaviour, out bool rawDataAvailable)
	{
		if (SerializeReferenceDataCache.TryGetData(monoBehaviour, out byte[]? cachedData, out _))
		{
			rawDataAvailable = cachedData is not null && cachedData.Length > 0;
			return cachedData;
		}
		if (monoBehaviour.Structure is UnloadedStructure unloaded)
		{
			byte[] data = unloaded.StructureData.ToArray();
			rawDataAvailable = data.Length > 0;
			return data;
		}
		rawDataAvailable = false;
		return null;
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

	private static void AddFilesystemScripts(AuditOptions options, List<SourceInventoryEntry> entries)
	{
		string scriptsDir = Path.Combine(options.SourceDirectory, InventoryScope.RelativeFilesystemScriptsDir.Replace('/', Path.DirectorySeparatorChar));
		if (!Directory.Exists(scriptsDir))
		{
			Logger.Warning(LogCategory.System, $"[source-inventory] Filesystem scripts directory not found: {scriptsDir}");
			return;
		}
		foreach (string file in Directory.EnumerateFiles(scriptsDir, "*.nani"))
		{
			byte[] data = File.ReadAllBytes(file);
			entries.Add(new SourceInventoryEntry
			{
				AssetType = NaninovelAssetType.NaniScript,
				AssetIdentifier = Path.GetFileNameWithoutExtension(file),
				DataSourcePath = $"{InventoryScope.RelativeFilesystemScriptsDir}/{Path.GetFileName(file)}",
				ByteLength = data.Length,
				Sha256 = ArtifactWriter.ComputeSha256(data),
				IsAuthoritative = false,
				AuthorityBasis = "file system source",
				PathId = 0,
			});
		}
	}

	/// <summary>权威版本标记：同名变体全部保留；判定优先级：Resources 路径命中 > bundle 内嵌（catalog 键命中待后续增强）。</summary>
	private static void MarkAuthoritativeVersions(List<SourceInventoryEntry> entries)
	{
		Dictionary<string, List<int>> groups = new(StringComparer.OrdinalIgnoreCase);
		for (int i = 0; i < entries.Count; i++)
		{
			string key = entries[i].AssetType == NaninovelAssetType.NaniScript
				? entries[i].AssetIdentifier
				: $"{entries[i].AssetType}:{entries[i].AssetIdentifier}";
			if (!groups.TryGetValue(key, out List<int>? indices))
			{
				indices = [];
				groups[key] = indices;
			}
			indices.Add(i);
		}

		foreach (KeyValuePair<string, List<int>> group in groups)
		{
			List<int> indices = group.Value;
			if (indices.Count == 1)
			{
				entries[indices[0]] = entries[indices[0]] with { IsAuthoritative = true, AuthorityBasis = AppendAuthority(entries[indices[0]].AuthorityBasis, "sole instance") };
				continue;
			}

			int authoritativeIndex = indices.FindIndex(i => entries[i].AuthorityBasis == "Resources path hit");
			if (authoritativeIndex >= 0)
			{
				int authIdx = indices[authoritativeIndex];
				for (int v = 0; v < indices.Count; v++)
				{
					int idx = indices[v];
					bool isAuth = idx == authIdx;
					string basis = isAuth
						? AppendAuthority(entries[idx].AuthorityBasis, "authoritative (Resources path hit)")
						: AppendAuthority(entries[idx].AuthorityBasis, $"variant of {entries[authIdx].DataSourcePath}");
					entries[idx] = entries[idx] with { IsAuthoritative = isAuth, AuthorityBasis = basis };
				}
			}
			else
			{
				for (int v = 0; v < indices.Count; v++)
				{
					int idx = indices[v];
					entries[idx] = entries[idx] with { AuthorityBasis = AppendAuthority(entries[idx].AuthorityBasis, "variant, authority unresolved (no Resources path hit)") };
				}
			}
		}
	}

	private static string AppendAuthority(string basis, string annotation)
	{
		return string.IsNullOrEmpty(basis) ? annotation : $"{basis} — {annotation}";
	}

	private static string ClassifyAuthority(IUnityObjectBase asset)
	{
		string? originalPath = asset.OriginalPath;
		if (originalPath is not null && originalPath.Replace('\\', '/').StartsWith(ResourcesPrefix, StringComparison.OrdinalIgnoreCase))
		{
			return "Resources path hit";
		}
		return "bundle embedded";
	}

	private static string StripExtension(string name)
	{
		int dot = name.LastIndexOf('.');
		return dot > 0 ? name[..dot] : name;
	}
}