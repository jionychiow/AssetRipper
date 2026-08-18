using AssetRipper.Assets;
using AssetRipper.Assets.Cloning;
using AssetRipper.Assets.IO.Writing;
using AssetRipper.Assets.Metadata;
using AssetRipper.Assets.Traversal;
using AssetRipper.IO.Endian;
using AssetRipper.IO.Files.SerializedFiles;
using AssetRipper.Import.Structure.Assembly.Managers;
using AssetRipper.Import.Logging;
using AssetRipper.SerializationLogic;
using System.Collections.Generic;

namespace AssetRipper.Import.Structure.Assembly.Serializable;

public sealed class SerializableManagedReferencesRegistry : IUnityAssetBase
{
	public ManagedReferencesRegistry Registry { get; } = new();

	public int SerializedVersion => 1;
	public bool FlowMappedInYaml => false;

	public bool IgnoreFieldInMetaFiles(string fieldName) => false;

	public void CopyValues(IUnityAssetBase? source, PPtrConverter converter)
	{
		if (source is SerializableManagedReferencesRegistry other)
		{
			Registry.Version = other.Registry.Version;
			Registry.References.Clear();
			Registry.References.AddRange(other.Registry.References);
		}
	}

	public void Reset()
	{
		Registry.Version = 0;
		Registry.References.Clear();
	}

	public void ReadEditor(ref EndianSpanReader reader) => Read(ref reader);
	public void ReadRelease(ref EndianSpanReader reader) => Read(ref reader);

	private void Read(ref EndianSpanReader reader)
	{
		throw new NotSupportedException("Use Read(ref EndianSpanReader, UnityVersion, TransferInstructionFlags, IAssemblyManager) instead.");
	}

	public void Read(ref EndianSpanReader reader, UnityVersion version, TransferInstructionFlags flags, IAssemblyManager assemblyManager)
	{
		Registry.Read(ref reader, version, flags, assemblyManager);
	}

	public void WriteEditor(AssetWriter writer) => Write(writer);
	public void WriteRelease(AssetWriter writer) => Write(writer);

	private void Write(AssetWriter writer)
	{
		writer.Write(Registry.Version);
		
		foreach (ManagedReference reference in Registry.References)
		{
			WriteManagedReference(writer, reference);
		}
		
		WriteTerminator(writer);
	}

	private void WriteManagedReference(AssetWriter writer, ManagedReference reference)
	{
		writer.Write(reference.Class);
		writer.Write(reference.Namespace);
		writer.Write(reference.Assembly);
		
		if (reference.Data is SerializableStructure structure)
		{
			structure.Write(writer);
		}
	}

	private void WriteTerminator(AssetWriter writer)
	{
		writer.Write(string.Empty);
		writer.Write(string.Empty);
		writer.Write(string.Empty);
	}

	public void WalkEditor(AssetWalker walker) => Walk(walker);
	public void WalkRelease(AssetWalker walker) => Walk(walker);
	public void WalkStandard(AssetWalker walker) => Walk(walker);

	private void Walk(AssetWalker walker)
	{
		if (walker.EnterAsset(this))
		{
			walker.ExitAsset(this);
		}
	}

	public IEnumerable<(string, PPtr)> FetchDependencies()
	{
		yield break;
	}

	public bool? AddToEqualityComparer(IUnityAssetBase other, AssetEqualityComparer comparer)
	{
		if (other is SerializableManagedReferencesRegistry otherRegistry)
		{
			if (Registry.Version != otherRegistry.Registry.Version)
				return false;
			
			if (Registry.References.Count != otherRegistry.Registry.References.Count)
				return false;
			
			for (int i = 0; i < Registry.References.Count; i++)
			{
				if (!ReferenceEquals(Registry.References[i], otherRegistry.Registry.References[i]))
					return false;
			}
			
			return true;
		}
		return false;
	}
}
