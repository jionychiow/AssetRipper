using System.IO;
using System.Text;

namespace AssetRipper.Export.UnityProjects.AddressablesPathFix;

public static class TextureMetaWriter
{
	public static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
	{
		".jpg", ".jpeg", ".png", ".tga", ".bmp", ".exr", ".tif", ".tiff",
	};

	public static bool IsImageFile(string filePath)
	{
		string ext = Path.GetExtension(filePath);
		return ImageExtensions.Contains(ext);
	}

	public static void WriteDefaultTextureMeta(string filePath, string guid)
	{
		string metaPath = filePath + ".meta";
		StringBuilder sb = new(2100);
		sb.Append("fileFormatVersion: 2\n");
		sb.Append($"guid: {guid}\n");
		sb.Append("TextureImporter:\n");
		sb.Append("  internalIDToNameTable: []\n");
		sb.Append("  externalObjects: {}\n");
		sb.Append("  serializedVersion: 11\n");
		sb.Append("  mipmaps:\n");
		sb.Append("    mipMapMode: 0\n");
		sb.Append("    enableMipMap: 0\n");
		sb.Append("    sRGBTexture: 1\n");
		sb.Append("    linearTexture: 0\n");
		sb.Append("    fadeOut: 0\n");
		sb.Append("    borderMipMap: 0\n");
		sb.Append("    mipMapsPreserveCoverage: 0\n");
		sb.Append("    alphaTestReferenceValue: 0.5\n");
		sb.Append("    mipMapFadeDistanceStart: 1\n");
		sb.Append("    mipMapFadeDistanceEnd: 3\n");
		sb.Append("  bumpmap:\n");
		sb.Append("    convertToNormalMap: 0\n");
		sb.Append("    externalNormalMap: 0\n");
		sb.Append("    heightScale: 0.25\n");
		sb.Append("    normalMapFilter: 0\n");
		sb.Append("  isReadable: 0\n");
		sb.Append("  streamingMipmaps: 0\n");
		sb.Append("  streamingMipmapsPriority: 0\n");
		sb.Append("  grayScaleToAlpha: 0\n");
		sb.Append("  generateCubemap: 6\n");
		sb.Append("  cubemapConvolution: 0\n");
		sb.Append("  seamlessCubemap: 0\n");
		sb.Append("  textureFormat: 12\n");
		sb.Append("  maxTextureSize: 2048\n");
		sb.Append("  textureSettings:\n");
		sb.Append("    serializedVersion: 2\n");
		sb.Append("    filterMode: 1\n");
		sb.Append("    aniso: 1\n");
		sb.Append("    mipBias: 0\n");
		sb.Append("    wrapU: 0\n");
		sb.Append("    wrapV: 0\n");
		sb.Append("    wrapW: 0\n");
		sb.Append("  nPOTScale: 0\n");
		sb.Append("  lightmap: 0\n");
		sb.Append("  compressionQuality: 50\n");
		sb.Append("  spriteMode: 0\n");
		sb.Append("  spriteExtrude: 1\n");
		sb.Append("  spriteMeshType: 1\n");
		sb.Append("  alignment: 0\n");
		sb.Append("  spritePivot: {x: 0.5, y: 0.5}\n");
		sb.Append("  spritePixelsToUnits: 100\n");
		sb.Append("  spriteBorder: {x: 0, y: 0, z: 0, w: 0}\n");
		sb.Append("  spriteGenerateFallbackPhysicsShape: 1\n");
		sb.Append("  alphaUsage: 1\n");
		sb.Append("  alphaIsTransparency: 1\n");
		sb.Append("  spriteTessellationDetail: -1\n");
		sb.Append("  textureType: 0\n");
		sb.Append("  textureShape: 1\n");
		sb.Append("  singleChannelComponent: 0\n");
		sb.Append("  maxTextureSizeSet: 0\n");
		sb.Append("  compressionQualitySet: 0\n");
		sb.Append("  textureFormatSet: 0\n");
		sb.Append("  applyGammaDecoding: 0\n");
		sb.Append("  platformSettings:\n");
		sb.Append("  - serializedVersion: 3\n");
		sb.Append("    buildTarget: DefaultTexturePlatform\n");
		sb.Append("    maxTextureSize: 2048\n");
		sb.Append("    resizeAlgorithm: 0\n");
		sb.Append("    textureFormat: -1\n");
		sb.Append("    textureCompression: 1\n");
		sb.Append("    compressionQuality: 50\n");
		sb.Append("    crunchedCompression: 0\n");
		sb.Append("    allowsAlphaSplitting: 0\n");
		sb.Append("    overridden: 0\n");
		sb.Append("    androidETC2FallbackOverride: 0\n");
		sb.Append("    forceMaximumCompressionQuality_BC6H_BC7: 0\n");
		sb.Append("  spriteSheet:\n");
		sb.Append("    serializedVersion: 2\n");
		sb.Append("    sprites: []\n");
		sb.Append("    outline: []\n");
		sb.Append("    physicsShape: []\n");
		sb.Append("    bones: []\n");
		sb.Append("    spriteID: \n");
		sb.Append("    internalID: 0\n");
		sb.Append("    vertices: []\n");
		sb.Append("    indices: \n");
		sb.Append("    edges: []\n");
		sb.Append("    weights: []\n");
		sb.Append("    secondaryTextures: []\n");
		sb.Append("  spritePackingTag: \n");
		sb.Append("  pSDRemoveMatte: 0\n");
		sb.Append("  pSDShowRemoveMatteOption: 0\n");
		sb.Append("  userData: \n");
		sb.Append("  assetBundleName: \n");
		sb.Append("  assetBundleVariant: \n");

		File.WriteAllText(metaPath, sb.ToString(), new UTF8Encoding(false));
	}
}