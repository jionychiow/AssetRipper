using AssetRipper.Assets;
using AssetRipper.Assets.Cloning;
using AssetRipper.Assets.Metadata;
using AssetRipper.Assets.Traversal;
using AssetRipper.IO.Endian;

namespace AssetRipper.Import.Structure.Assembly.Serializable;

public sealed class FallbackStructureAsset : UnityAssetBase, IDeepCloneable, IFallbackStructure
{
	private readonly byte[] originalData;
	private readonly EndianType endianType;
	private readonly string scriptFullName;

	public FallbackStructureAsset(byte[] originalData, EndianType endianType, string scriptFullName)
	{
		this.originalData = originalData.ToArray();
		this.endianType = endianType;
		this.scriptFullName = scriptFullName;
	}

	public byte[] OriginalData => originalData;
	public EndianType EndianType => endianType;
	public string ScriptFullName => scriptFullName;
	public override int SerializedVersion => 1;
	public override bool FlowMappedInYaml => false;

	public override void WalkEditor(AssetWalker walker) => WalkFallback(walker);
	public override void WalkRelease(AssetWalker walker) => WalkFallback(walker);
	public override void WalkStandard(AssetWalker walker) => WalkFallback(walker);

	private void WalkFallback(AssetWalker walker)
	{
		if (walker.EnterAsset(this))
		{
			if (walker.EnterField(this, "_AssetRipperFallbackData"))
			{
				walker.VisitPrimitive(Convert.ToBase64String(originalData));
				walker.ExitField(this, "_AssetRipperFallbackData");
			}
			if (walker.EnterField(this, "_AssetRipperFallbackSize"))
			{
				walker.VisitPrimitive(originalData.Length);
				walker.ExitField(this, "_AssetRipperFallbackSize");
			}
			if (walker.EnterField(this, "_AssetRipperFallbackEndian"))
			{
				walker.VisitPrimitive(endianType.ToString());
				walker.ExitField(this, "_AssetRipperFallbackEndian");
			}
			walker.ExitAsset(this);
		}
	}

	IUnityAssetBase IDeepCloneable.DeepClone(PPtrConverter converter) => this;

	public override void CopyValues(IUnityAssetBase? source, PPtrConverter converter) { }

	public override void Reset() { }

	public override bool? AddToEqualityComparer(IUnityAssetBase other, AssetEqualityComparer comparer)
	{
		return other is FallbackStructureAsset;
	}
}
