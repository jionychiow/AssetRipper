using AssetRipper.Assets;
using AssetRipper.SourceGenerated.Classes.ClassID_83;
using AssetRipper.SourceGenerated.Extensions;
using AssetRipper.SourceGenerated.Subclasses.StreamedResource;

namespace AssetRipper.Export.UnityProjects.Audio;

public sealed class YamlAudioExportCollection : AssetExportCollection<IAudioClip>
{
	public YamlAudioExportCollection(IAssetExporter assetExporter, IAudioClip asset) : base(assetExporter, asset)
	{
	}

	protected override bool ExportInner(IExportContainer container, string filePath, string dirPath, FileSystem fileSystem)
	{
		IStreamedResource? resource = Asset.Resource;
		if (resource is not null)
		{
			Utf8String originalSource = resource.Source;
			ulong originalOffset = resource.Offset;
			ulong originalSize = resource.Size;
			bool originalPreload = Asset.PreloadAudioData;
			if (resource.TryGetContent(Asset.Collection, out byte[]? data))
			{
				string resPath = filePath + ".resS";
				fileSystem.File.WriteAllBytes(resPath, data);
				resource.Source = fileSystem.Path.GetRelativePath(dirPath, resPath);
				resource.Offset = 0;
				resource.Size = (ulong)data.Length;
			}
			else
			{
				resource.Source = Utf8String.Empty;
				resource.Offset = 0;
				resource.Size = 0;
			}
			Asset.PreloadAudioData = false;
			bool result = base.ExportInner(container, filePath, dirPath, fileSystem);
			resource.Source = originalSource;
			resource.Offset = originalOffset;
			resource.Size = originalSize;
			Asset.PreloadAudioData = originalPreload;
			return result;
		}
		else
		{
			return base.ExportInner(container, filePath, dirPath, fileSystem);
		}
	}

	protected override string GetExportExtension(IUnityObjectBase asset) => "asset";
}
