using AssetRipper.Assets;
using AssetRipper.Assets.Collections;
using AssetRipper.Export.UnityProjects.AddressablesPathFix.Models;
using AssetRipper.Import.Logging;
using AssetRipper.Processing;

namespace AssetRipper.Export.UnityProjects.AddressablesPathFix.Parsers;

public sealed class AssetBundleParser
{
	private static readonly HashSet<string> NaninovelPrefixes = new(StringComparer.OrdinalIgnoreCase)
	{
		"Backgrounds", "Characters", "Character", "Audio", "Audios", "NScripts",
		"Scripts", "Anims", "Animations", "Scenes", "Fonts", "Videos",
		"Materials", "Sprites", "Textures", "Music", "VoiceLines", "Stops",
		"Movies", "CGs", "Movies", "Backgrounds/MainBackground", "Localization",
		"Unlockables", "Tips", "Items", "Custom", "Generic",
	};

	public IReadOnlyList<BundleAssetEntry> Parse(string bundleDirectory, GameData gameData, FileSystem fileSystem)
	{
		List<BundleAssetEntry> entries = new(2048);

		try
		{
			foreach (SerializedAssetCollection collection in gameData.GameBundle.FetchAssetCollections().OfType<SerializedAssetCollection>())
			{
				foreach (IUnityObjectBase asset in collection)
				{
					string name = (asset as INamed)?.Name ?? string.Empty;
					string typeName = asset.ClassName;
					string bundleName = collection.Name;

					entries.Add(new BundleAssetEntry
					{
						PathID = asset.PathID,
						Name = name,
						TypeName = typeName,
						BundleName = bundleName,
					});
				}
			}

			Logger.Info(LogCategory.Export, $"AssetBundleParser: 从 GameBundle 解析 {entries.Count} 个资源条目");

		}
		catch (Exception ex)
		{
			Logger.Warning(LogCategory.Export, $"AssetBundleParser: GameBundle 遍历失败: {ex.Message}");
		}

		if (entries.Count == 0)
		{
			Logger.Warning(LogCategory.Export, "AssetBundleParser: GameBundle 未返回资源，尝试扫描 bundle 目录");
			IReadOnlyList<string> bundleFiles = LocateBundles(bundleDirectory, fileSystem);
			if (bundleFiles.Count > 0)
			{
				Logger.Info(LogCategory.Export, $"AssetBundleParser: 找到 {bundleFiles.Count} 个 bundle 文件，但直接解析未实现，返回空列表");
			}
		}

		return entries;
	}

	public static IReadOnlyList<string> LocateBundles(string bundleDirectory, FileSystem fileSystem)
	{
		List<string> bundles = new();

		if (!fileSystem.Directory.Exists(bundleDirectory))
		{
			Logger.Warning(LogCategory.Export, $"AssetBundleParser: bundle 目录不存在: {bundleDirectory}");
			return bundles;
		}

		foreach (string file in fileSystem.Directory.EnumerateFiles(bundleDirectory, "*.bundle"))
		{
			bundles.Add(file);
		}

		Logger.Info(LogCategory.Export, $"AssetBundleParser: 定位到 {bundles.Count} 个 bundle 文件");
		return bundles;
	}
}