using AssetRipper.Assets;
using AssetRipper.Export.Configuration;
using AssetRipper.Export.Modules.Textures;
using AssetRipper.Import.Logging;
using AssetRipper.Processing.Textures;
using AssetRipper.SourceGenerated.Classes.ClassID_213;
using AssetRipper.SourceGenerated.Classes.ClassID_28;
using AssetRipper.SourceGenerated.Extensions;

namespace AssetRipper.Export.UnityProjects.Textures;

public class TextureAssetExporter : BinaryAssetExporter
{
	public ImageExportFormat ImageExportFormat { get; }
	private SpriteExportMode SpriteExportMode { get; }
	public bool PreferOriginalTextureExtension { get; }
	private bool ExportSprites => SpriteExportMode is not SpriteExportMode.Yaml;

	public TextureAssetExporter(FullConfiguration configuration)
	{
		ImageExportFormat = configuration.ExportSettings.ImageExportFormat;
		SpriteExportMode = configuration.ExportSettings.SpriteExportMode;
		PreferOriginalTextureExtension = configuration.ExportSettings.PreferOriginalTextureExtension;
	}

	public override bool TryCreateCollection(IUnityObjectBase asset, [NotNullWhen(true)] out IExportCollection? exportCollection)
	{
		if (asset.MainAsset is SpriteInformationObject spriteInformationObject && (ExportSprites || asset is not ISprite))
		{
			exportCollection = new TextureExportCollection(this, spriteInformationObject, ExportSprites);
			return true;
		}
		else
		{
			exportCollection = null;
			return false;
		}
	}

	public override bool Export(IExportContainer container, IUnityObjectBase asset, string path, FileSystem fileSystem)
	{
		ITexture2D texture = (ITexture2D)asset;
		if (!texture.CheckAssetIntegrity())
		{
			Logger.Log(LogType.Warning, LogCategory.Export, $"Can't export '{texture.Name}' because resources file '{texture.StreamData_C28?.Path}' hasn't been found");
			return false;
		}

		if (TextureConverter.TryConvertToBitmap(texture, out DirectBitmap bitmap))
		{
			using Stream stream = fileSystem.File.Create(path);
			bitmap.Save(stream, texture.GetTextureExportFormat(PreferOriginalTextureExtension, ImageExportFormat));
			return true;
		}
		else
		{
			Logger.Log(LogType.Warning, LogCategory.Export, $"Unable to convert '{texture.Name}' to bitmap, creating 1x1 placeholder PNG. Format={texture.Format_C28E} Width={texture.Width_C28} Height={texture.Height_C28}");
			byte[] minimalPng = [
				0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
				0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
				0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
				0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
				0x89, 0x00, 0x00, 0x00, 0x0A, 0x49, 0x44, 0x41,
				0x54, 0x78, 0x9C, 0x62, 0x00, 0x02, 0x00, 0x00,
				0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00,
				0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE,
				0x42, 0x60, 0x82,
			];
			using Stream placeholderStream = fileSystem.File.Create(path);
			placeholderStream.Write(minimalPng, 0, minimalPng.Length);
			return true;
		}
	}
}
