using AssetRipper.Assets;
using AssetRipper.Export.UnityProjects.Project;
using AssetRipper.SourceGenerated.Classes.ClassID_91;
using AssetRipper.SourceGenerated.Extensions;

namespace AssetRipper.Export.UnityProjects.AnimatorControllers;

public sealed class AnimatorControllerExportCollection : AssetsExportCollection<IAnimatorController>
{
	public AnimatorControllerExportCollection(IAssetExporter assetExporter, IAnimatorController controller) : base(assetExporter, controller)
	{
		AddAssets(controller.FetchEditorHierarchy());
	}

	protected override long GenerateExportID(IUnityObjectBase asset)
	{
		int pathIDHash = unchecked((int)(asset.PathID ^ (asset.PathID >> 32)));
		int seed = unchecked(pathIDHash + ExportIDCount);
		return ExportIdHandler.GetPseudoRandomExportId(asset, seed);
	}
}
