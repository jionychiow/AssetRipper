using AssetRipper.Assets;
using AssetRipper.Assets.Cloning;
using AssetRipper.Assets.Generics;
using AssetRipper.Assets.IO.Writing;
using AssetRipper.Assets.Metadata;
using AssetRipper.Assets.Traversal;
using AssetRipper.Import.Logging;
using AssetRipper.Import.Structure.Assembly.Managers;
using AssetRipper.IO.Endian;
using AssetRipper.SourceGenerated.Classes.ClassID_114;

namespace AssetRipper.Import.Structure.Assembly.Serializable;

/// <summary>
/// This is a placeholder asset that lazily reads the actual structure sometime after all the assets have been loaded.
/// This allows MonoBehaviours to be loaded before their referenced MonoScript.
/// </summary>
public sealed class UnloadedStructure : UnityAssetBase, IDeepCloneable
{
	private sealed class StatelessAsset : UnityAssetBase, IDeepCloneable
	{
		public static StatelessAsset Instance { get; } = new();
		private StatelessAsset()
		{
		}

		public override void Reset()
		{
		}

		public override bool? AddToEqualityComparer(IUnityAssetBase other, AssetEqualityComparer comparer)
		{
			return other is StatelessAsset;
		}

		IUnityAssetBase IDeepCloneable.DeepClone(PPtrConverter converter) => this;
	}

	/// <summary>
	/// The <see cref="IMonoBehaviour"/> that <see langword="this"/> is the <see cref="IMonoBehaviour.Structure"/> for.
	/// </summary>
	public IMonoBehaviour MonoBehaviour { get; }

	public IAssemblyManager AssemblyManager { get; }

	/// <summary>
	/// The segment of data for this structure.
	/// </summary>
	public ReadOnlyArraySegment<byte> StructureData { get; }

	public UnloadedStructure(IMonoBehaviour monoBehaviour, IAssemblyManager assemblyManager, ReadOnlyArraySegment<byte> structureData)
	{
		MonoBehaviour = monoBehaviour;
		AssemblyManager = assemblyManager;
		StructureData = structureData;
	}

	private void ThrowIfNotStructure()
	{
		if (!ReferenceEquals(MonoBehaviour.Structure, this))
		{
			throw new InvalidOperationException("The MonoBehaviour structure has already been loaded.");
		}
	}

	public SerializableStructure? LoadStructure()
	{
		ThrowIfNotStructure();
		string scriptFullName = MonoBehaviour.ScriptP?.GetFullName() ?? "Unknown";
		long pathID = MonoBehaviour.PathID;

		string? failureReason = null;
		SerializableStructure? structure = MonoBehaviour.ScriptP?.GetBehaviourType(AssemblyManager, out failureReason)?.CreateSerializableStructure();
		if (structure is not null)
		{
			EndianSpanReader reader = new EndianSpanReader(StructureData, MonoBehaviour.Collection.EndianType);
			if (structure.TryRead(ref reader, MonoBehaviour))
			{
				MonoBehaviour.Structure = structure;
				LayoutFixStatistics.RecordDecision(LayoutFixDecision.ReflectionSuccess, pathID, scriptFullName);
				return structure;
			}
			EndianSpanReader retryReader = new EndianSpanReader(StructureData, MonoBehaviour.Collection.EndianType);
			if (structure.TryRead(ref retryReader, MonoBehaviour, true))
			{
				Logger.Info(LogCategory.Import, $"Read MonoBehaviour structure for `{scriptFullName}` with {retryReader.Length - retryReader.Position} remaining bytes (managed references registry).");
				MonoBehaviour.Structure = structure;
				LayoutFixStatistics.RecordDecision(LayoutFixDecision.ReflectionSuccess, pathID, scriptFullName);
				return structure;
			}
			Logger.Warning(LogCategory.Import, $"Failed to read MonoBehaviour structure for `{scriptFullName}`. Falling back to degraded export.");
			LayoutFixStatistics.RecordDecision(LayoutFixDecision.DegradedExport, pathID, scriptFullName, "TryRead failed");
		}
		else
		{
			if (failureReason is not null)
			{
				Logger.Warning(LogCategory.Import, $"Could not read MonoBehaviour structure for `{scriptFullName}`. Reason: {failureReason}");
			}
			LayoutFixStatistics.RecordDecision(LayoutFixDecision.DegradedExport, pathID, scriptFullName, failureReason ?? "GetBehaviourType returned null");
		}

		if (StructureData.Count > 0)
		{
			FallbackStructureAsset fallbackAsset = new FallbackStructureAsset(StructureData.ToArray(), MonoBehaviour.Collection.EndianType, scriptFullName);
			MonoBehaviour.Structure = fallbackAsset;
			return null;
		}

		MonoBehaviour.Structure = null;
		LayoutFixStatistics.RecordDecision(LayoutFixDecision.TotalFailure, pathID, scriptFullName, "No structure data");
		return null;
	}

	private UnityAssetBase LoadStructureOrStatelessAsset()
	{
		LoadStructure();
		return (UnityAssetBase?)MonoBehaviour.Structure ?? StatelessAsset.Instance;
	}

	public IUnityAssetBase DeepClone(PPtrConverter converter)
	{
		return LoadStructure()?.DeepClone(converter) ?? (IUnityAssetBase)StatelessAsset.Instance;
	}

	#region UnityAssetBase Overrides
	public override bool FlowMappedInYaml => LoadStructure()?.FlowMappedInYaml ?? base.FlowMappedInYaml;

	public override int SerializedVersion => LoadStructure()?.SerializedVersion ?? base.SerializedVersion;

	public override void WalkEditor(AssetWalker walker)
	{
		LoadStructureOrStatelessAsset().WalkEditor(walker);
	}

	public override void WalkRelease(AssetWalker walker)
	{
		LoadStructureOrStatelessAsset().WalkRelease(walker);
	}

	public override void WalkStandard(AssetWalker walker)
	{
		LoadStructureOrStatelessAsset().WalkStandard(walker);
	}

	public override IEnumerable<(string, PPtr)> FetchDependencies()
	{
		return LoadStructure()?.FetchDependencies() ?? [];
	}

	public override void WriteEditor(AssetWriter writer) => LoadStructure()?.WriteEditor(writer);

	public override void WriteRelease(AssetWriter writer) => LoadStructure()?.WriteRelease(writer);

	public override void CopyValues(IUnityAssetBase? source, PPtrConverter converter)
	{
		SerializableStructure? loadedStructure = LoadStructure();
		if (source is not UnloadedStructure unloadedSource)
		{
			loadedStructure?.CopyValues(source, converter);
		}
		else if (ReferenceEquals(this, unloadedSource))
		{
			loadedStructure?.CopyValues(loadedStructure, converter);
		}
		else
		{
			loadedStructure?.CopyValues(unloadedSource.LoadStructure(), converter);
		}
	}

	public override void Reset() => LoadStructure()?.Reset();

	public override bool? AddToEqualityComparer(IUnityAssetBase other, AssetEqualityComparer comparer)
	{
		return LoadStructureOrStatelessAsset().AddToEqualityComparer(other, comparer);
	}
	#endregion
}
