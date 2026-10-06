
using AssetRipper.Assets;
using AssetRipper.Import.Logging;
using AssetRipper.SourceGenerated.Classes.ClassID_1127;
using AssetRipper.SourceGenerated.Classes.ClassID_329;
using AssetRipper.SourceGenerated.Extensions;
using AssetRipper.SourceGenerated.Subclasses.StreamedResource;

namespace AssetRipper.Export.UnityProjects.Miscellaneous;

public sealed class VideoClipExportCollection : AssetExportCollection<IVideoClip>
{
	public VideoClipExportCollection(VideoClipExporter assetExporter, IVideoClip asset) : base(assetExporter, asset)
	{
	}

	protected override bool ExportInner(IExportContainer container, string filePath, string dirPath, FileSystem fileSystem)
	{
		IStreamedResource? resource = Asset.ExternalResources;
		if (resource is not null)
		{
		Utf8String originalSource = resource.Source;
		ulong originalOffset = resource.Offset;
		ulong originalSize = resource.Size;

		bool result;
		if (resource.TryGetContent(Asset.Collection, out byte[]? data))
		{
			string resPath = filePath + ".resS";
			fileSystem.File.WriteAllBytes(resPath, data);
			// 重要：此处直接写入 .webm 文件，不调用 base.ExportInner。
			// 原因：base.ExportInner → VideoClipExporter.Export → TryGetContent 会使用
			// 已修改的 resource.Source（.resS 相对路径），经 Bundle.ResolveResource 的
			// .ress 回退逻辑错误匹配到原始游戏的 resources.assets.resS 文件，
			// 读取 0xFF 填充数据写入 .webm，导致 Unity 导入报 "Unable to read movie header"。
			// 修复方案：单次 TryGetContent 读取的 data 同时写入 .resS 和 .webm，
			// 避免二次 TryGetContent 触发回退匹配。
			fileSystem.File.WriteAllBytes(filePath, data);
			resource.Source = fileSystem.Path.GetRelativePath(dirPath, resPath);
			resource.Offset = 0;
			resource.Size = (ulong)data.Length;
			result = true;
		}
		else
		{
			resource.Source = Utf8String.Empty;
			resource.Offset = 0;
			resource.Size = 0;
			result = false;
		}
		resource.Source = originalSource;
		resource.Offset = originalOffset;
		resource.Size = originalSize;
		return result;
		}
		else
		{
			return base.ExportInner(container, filePath, dirPath, fileSystem);
		}
	}

	protected override string GetExportExtension(IUnityObjectBase asset) => Asset.GetExtensionFromPath();

	protected override IVideoClipImporter CreateImporter(IExportContainer container)
	{
		//There may be more fields to set here. I did not throughly check.

		IVideoClipImporter importer = VideoClipImporter.Create(container.File, container.ExportVersion);
		importer.EndFrame = (int)Asset.FrameCount;
		if (importer.Has_SourceFileSize())
		{
			importer.OriginalHeight = (int)Asset.Height;
			importer.OriginalWidth = (int)Asset.Width;
			importer.SourceFileSize = Asset.ExternalResources.Size;
		}
		importer.FrameRate = Asset.FrameRate;
		importer.FrameCount = importer.EndFrame;
		importer.ImportAudio = true;
		if (importer.Has_AssetBundleName_R() && Asset.AssetBundleName is not null)
		{
			importer.AssetBundleName_R = Asset.AssetBundleName;
		}
		return importer;
	}
}
