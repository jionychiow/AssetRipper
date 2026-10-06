using AssetRipper.Assets;
using AssetRipper.Assets.Cloning;
using AssetRipper.Assets.Traversal;
using AssetRipper.Import.Structure.Assembly.Serializable;
using AssetRipper.IO.Endian;

namespace AssetRipper.Tests;

public class FallbackExportTests
{
	[Test]
	public void FallbackStructureAsset_Properties_AreCorrect()
	{
		byte[] data = [1, 2, 3, 4, 5];
		FallbackStructureAsset asset = new(data, EndianType.LittleEndian, "TestNS.TestClass");

		Assert.That(asset.OriginalData, Is.EqualTo(data));
		Assert.That(asset.EndianType, Is.EqualTo(EndianType.LittleEndian));
		Assert.That(asset.ScriptFullName, Is.EqualTo("TestNS.TestClass"));
		Assert.That(asset.SerializedVersion, Is.EqualTo(1));
		Assert.That(asset.FlowMappedInYaml, Is.False);
	}

	[Test]
	public void FallbackStructureAsset_OriginalData_IsDefensiveCopy()
	{
		byte[] data = [1, 2, 3, 4, 5];
		FallbackStructureAsset asset = new(data, EndianType.LittleEndian, "TestClass");

		data[0] = 99;
		Assert.That(asset.OriginalData[0], Is.EqualTo(1), "FallbackStructureAsset should make a defensive copy of the input data.");
	}

	[Test]
	public void FallbackStructureAsset_OriginalData_IsBase64RoundTrip()
	{
		byte[] data = new byte[256];
		for (int i = 0; i < 256; i++)
		{
			data[i] = (byte)i;
		}

		FallbackStructureAsset asset = new(data, EndianType.BigEndian, "TestClass");
		string base64 = Convert.ToBase64String(asset.OriginalData);
		byte[] decoded = Convert.FromBase64String(base64);
		Assert.That(decoded, Is.EqualTo(data));
	}

	[Test]
	public void FallbackStructureAsset_ImplementsIFallbackStructure()
	{
		byte[] data = [10, 20, 30];
		FallbackStructureAsset asset = new(data, EndianType.LittleEndian, "NS.Class");

		IFallbackStructure? fallback = asset as IFallbackStructure;
		Assert.That(fallback, Is.Not.Null);
		Assert.That(fallback!.OriginalData, Is.EqualTo(data));
		Assert.That(fallback.EndianType, Is.EqualTo(EndianType.LittleEndian));
		Assert.That(fallback.ScriptFullName, Is.EqualTo("NS.Class"));
	}

	[Test]
	public void FallbackStructureAsset_ImplementsIDeepCloneable()
	{
		byte[] data = [1, 2, 3];
		FallbackStructureAsset asset = new(data, EndianType.LittleEndian, "TestClass");

		IDeepCloneable? cloneable = asset as IDeepCloneable;
		Assert.That(cloneable, Is.Not.Null);
		IUnityAssetBase cloned = cloneable!.DeepClone(default(PPtrConverter));
		Assert.That(ReferenceEquals(cloned, asset), Is.True, "DeepClone should return the same instance for immutable fallback assets.");	}

	[Test]
	public void FallbackStructureAsset_Reset_IsNoOp()
	{
		byte[] data = [1, 2, 3];
		FallbackStructureAsset asset = new(data, EndianType.LittleEndian, "TestClass");

		asset.Reset();
		Assert.That(asset.OriginalData, Is.EqualTo(data));
	}

	[Test]
	public void FallbackStructureAsset_EmptyData_HandledCorrectly()
	{
		byte[] data = [];
		FallbackStructureAsset asset = new(data, EndianType.LittleEndian, "TestClass");

		Assert.That(asset.OriginalData, Is.Empty);
		Assert.That(asset.OriginalData.Length, Is.EqualTo(0));
	}

	[Test]
	public void FallbackStructureAsset_WalkEditor_VisitsThreeFallbackFields()
	{
		byte[] data = [1, 2, 3, 4];
		FallbackStructureAsset asset = new(data, EndianType.LittleEndian, "TestNS.TestClass");

		FieldRecordingWalker walker = new();
		asset.WalkEditor(walker);

		Assert.That(walker.VisitedFieldNames.Contains("_AssetRipperFallbackData"), Is.True);
		Assert.That(walker.VisitedFieldNames.Contains("_AssetRipperFallbackSize"), Is.True);
		Assert.That(walker.VisitedFieldNames.Contains("_AssetRipperFallbackEndian"), Is.True);
		Assert.That(walker.VisitedFieldNames.Count, Is.EqualTo(3));

		int sizeIndex = walker.VisitedFieldNames.IndexOf("_AssetRipperFallbackSize");
		Assert.That(walker.VisitedPrimitives[sizeIndex], Is.EqualTo(4));
	}

	private sealed class FieldRecordingWalker : AssetWalker
	{
		public List<string> VisitedFieldNames { get; } = new();
		public List<object?> VisitedPrimitives { get; } = new();

		public override bool EnterAsset(IUnityAssetBase asset) => true;
		public override void ExitAsset(IUnityAssetBase asset) { }

		public override bool EnterField(IUnityAssetBase asset, string name)
		{
			VisitedFieldNames.Add(name);
			return true;
		}

		public override void ExitField(IUnityAssetBase asset, string name) { }

		public override void VisitPrimitive<T>(T value)
		{
			VisitedPrimitives.Add(value);
		}
	}
}