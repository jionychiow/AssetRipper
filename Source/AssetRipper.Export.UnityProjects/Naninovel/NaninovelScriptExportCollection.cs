using AssetRipper.Assets;
using AssetRipper.Assets.Generics;
using AssetRipper.Export.Modules.Naninovel;
using AssetRipper.Import.AssetCreation;
using AssetRipper.Import.Logging;
using AssetRipper.Import.Structure.Assembly.Serializable;
using AssetRipper.IO.Endian;
using AssetRipper.IO.Files;
using AssetRipper.SourceGenerated.Classes.ClassID_114;

namespace AssetRipper.Export.UnityProjects.Naninovel;

public class NaninovelScriptExportCollection : AssetExportCollection<IMonoBehaviour>
{
	private readonly byte[]? cachedStructureData;
	private readonly EndianType cachedEndianType;

	public NaninovelScriptExportCollection(NaninovelScriptExporter exporter, IMonoBehaviour asset) : base(exporter, asset)
	{
		if (SerializeReferenceDataCache.TryGetData(asset, out byte[]? cachedData, out EndianType cachedEndian))
		{
			cachedStructureData = cachedData;
			cachedEndianType = cachedEndian;
			Logger.Info(LogCategory.Export, $"[NaninovelExport] Retrieved {cachedData?.Length ?? 0} bytes from cache for '{asset.GetBestName()}' (PathID={asset.PathID}).");
		}
		else if (asset.Structure is UnloadedStructure unloaded)
		{
			cachedStructureData = unloaded.StructureData.ToArray();
			cachedEndianType = asset.Collection.EndianType;
			Logger.Info(LogCategory.Export, $"[NaninovelExport] Retrieved {cachedStructureData.Length} bytes from UnloadedStructure for '{asset.GetBestName()}' (PathID={asset.PathID}).");
		}
		else
		{
			cachedEndianType = asset.Collection.EndianType;
			Logger.Warning(LogCategory.Export, $"[NaninovelExport] No cached data for '{asset.GetBestName()}' (PathID={asset.PathID}). Structure type: {asset.Structure?.GetType().Name ?? "null"}.");
		}
	}

	protected override string GetExportExtension(IUnityObjectBase asset)
	{
		return "nani";
	}

	protected override bool ExportInner(IExportContainer container, string filePath, string dirPath, FileSystem fileSystem)
	{
		byte[] naniBytes;
		try
		{
			naniBytes = GenerateNaniBytes();
		}
		catch (Exception ex)
		{
			Logger.Error(LogCategory.Export, $"[NaninovelExport] Failed to export '{Asset.GetBestName()}': {ex}");
			naniBytes = System.Text.Encoding.UTF8.GetBytes($";[NaninovelExport] Failed to export: {ex.Message}\n");
		}

		try
		{
			fileSystem.File.WriteAllBytes(filePath, naniBytes);
			Logger.Info(LogCategory.Export, $"[NaninovelExport] Wrote '{filePath}' ({naniBytes.Length} bytes).");
			return true;
		}
		catch (Exception ex)
		{
			Logger.Error(LogCategory.Export, $"[NaninovelExport] Failed to write file '{filePath}': {ex.Message}");
			return false;
		}
	}

	private byte[] GenerateNaniBytes()
	{
		if (cachedStructureData is null || cachedStructureData.Length == 0)
		{
			Logger.Warning(LogCategory.Export, $"[NaninovelExport] No cached structure data for '{Asset.GetBestName()}'.");
			return System.Text.Encoding.UTF8.GetBytes($";[NaninovelExport] Failed to read ManagedReferencesRegistry\n");
		}

		EndianSpanReader reader = new EndianSpanReader(cachedStructureData, cachedEndianType);

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

		return textGenerator.Generate(lineRids, referenceMap);
	}
}
