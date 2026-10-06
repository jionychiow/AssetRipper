using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using AssetRipper.Assets;
using AssetRipper.Assets.Collections;
using AssetRipper.Export.Configuration;
using AssetRipper.Export.UnityProjects.AddressablesPathFix.Models;
using AssetRipper.Export.UnityProjects.AddressablesPathFix.Parsers;
using AssetRipper.Export.UnityProjects.AddressablesPathFix.Reporting;
using AssetRipper.Export.UnityProjects.AddressablesPathFix.Validators;
using AssetRipper.Export.UnityProjects.Project;
using AssetRipper.Import.Logging;
using AssetRipper.SourceGenerated.Extensions;

#pragma warning disable IL2075
using AssetRipper.Processing;

namespace AssetRipper.Export.UnityProjects.AddressablesPathFix;

public sealed class AddressablesPathFixPostExporter : IPostExporter
{
	private const string NaninovelConfigurationRelativePath = "Resources/naninovel/configuration";
	private const string PathIdMapFileName = "path_id_map.json";
	private const string BundleRelativePath = "StreamingAssets/StandaloneWindows";

	private static readonly Regex GuidMetaRegex = new(@"guid:\s*([0-9a-fA-F]{32})", RegexOptions.Compiled);
	private static readonly string[] ExportTypeDirectories =
	[
		"Texture2D", "Sprite", "AudioClip", "TextAsset", "VideoClip",
		"GameObject", "Material", "Font", "RenderTexture",
		"AnimationClip", "AnimatorController", "Cubemap",
	];

	public void DoPostExport(GameData gameData, FullConfiguration settings, FileSystem fileSystem)
	{
		try
		{
			string? catalogPath = AddressablesCatalogParser.LocateCatalog(settings, fileSystem);
			if (catalogPath is null)
			{
				Logger.Error(LogCategory.Export, "Addressables catalog 未找到，跳过路径修复");
				return;
			}

			CatalogData catalog;
			try
			{
				AddressablesCatalogParser parser = new();
				catalog = parser.Parse(catalogPath, fileSystem);
			}
			catch (CatalogParseException ex)
			{
				Logger.Error(LogCategory.Export, $"catalog 解析失败: {ex.Message} (字段: {ex.FieldName})");
				return;
			}

			string configDir = fileSystem.Path.Join(settings.AssetsPath, NaninovelConfigurationRelativePath);
			if (!fileSystem.Directory.Exists(configDir))
			{
				Logger.Error(LogCategory.Export, $"Naninovel 配置目录不存在: {configDir}，跳过路径修复");
				return;
			}

			NaninovelConfig config;
			try
			{
				NaninovelConfigReader configReader = new();
				config = configReader.Read(configDir, fileSystem);
			}
			catch (Exception ex)
			{
				Logger.Error(LogCategory.Export, $"Naninovel 配置解析失败: {ex.Message}");
				return;
			}

			if (config.UseAddressables)
			{
				Logger.Warning(LogCategory.Export, "UseAddressables=1，路径修复可能无效，Naninovel 将使用 Addressables 加载资源");
			}

		OriginalPathTable originalPathTable = OriginalPathTable.Build(gameData);

		ResourcesCopyPostProcessResult resourcesCopyResult = new()
		{
			CopyResult = null,
			MetaResult = null,
			ValidationResult = null,
			Skipped = true,
			SkipReason = "未执行",
		};
		try
		{
			ResourcesCopyPostProcessor copyProcessor = new();
			resourcesCopyResult = copyProcessor.Process(settings, fileSystem);
			if (resourcesCopyResult.Skipped)
			{
				Logger.Warning(LogCategory.Export, $"Resources 文件复制跳过: {resourcesCopyResult.SkipReason}");
			}
		}
		catch (Exception ex)
		{
			Logger.Error(LogCategory.Export, $"Resources 文件复制异常: {ex.Message}");
		}

		try
		{
			DiagnoseUnmappedGuids(gameData, catalog, originalPathTable, config, settings.AssetsPath, fileSystem);
		}
		catch (Exception ex)
		{
			Logger.Warning(LogCategory.Export, $"GUID 诊断失败: {ex.Message}");
		}

		CleanupPlaceholderFiles(settings.AssetsPath);
		FixEmptyYamlFiles(settings.AssetsPath);

		PathIdAddressMap pathIdAddressMap = new();
			try
			{
				string bundleDirectory = fileSystem.Path.Join(settings.AssetsPath, BundleRelativePath);
				AssetBundleParser bundleParser = new();
				IReadOnlyList<BundleAssetEntry> bundleAssets = bundleParser.Parse(bundleDirectory, gameData, fileSystem);

				if (bundleAssets.Count > 0)
				{
					PathIdAddressMapper pathIdAddressMapper = new();
					pathIdAddressMap = pathIdAddressMapper.Build(catalog, bundleAssets);
					Logger.Info(LogCategory.Export, $"PathID→地址映射建立完成: {pathIdAddressMap.Count} 个条目");
				}
				else
				{
					Logger.Warning(LogCategory.Export, "Asset bundle 解析未返回资源，策略 D 将不可用");
				}
			}
			catch (Exception ex)
			{
				Logger.Warning(LogCategory.Export, $"Asset bundle 解析或 PathID 映射建立失败: {ex.Message}，降级为策略 A/B/C");
			}

			string pathIdMapPath = fileSystem.Path.Join(settings.AuxiliaryFilesPath, PathIdMapFileName);
			PathIdNameMap pathIdMap;
			try
			{
				PathIdMapLoader loader = new();
				pathIdMap = loader.Load(pathIdMapPath, gameData, fileSystem);
			}
			catch (Exception ex)
			{
				Logger.Warning(LogCategory.Export, $"PathID 映射加载失败: {ex.Message}，使用空映射");
				pathIdMap = new PathIdNameMap();
			}

			MovePlan plan;
			try
			{
				ResourceMapper mapper = new();
				plan = mapper.BuildFromCatalog(catalog, config, originalPathTable, pathIdMap, pathIdAddressMap, settings.AssetsPath, fileSystem);
			}
			catch (Exception ex)
			{
				Logger.Error(LogCategory.Export, $"映射建立失败: {ex.Message}");
				return;
			}

		MoveResult result;
			try
			{
				ResourceMover mover = new();
				result = mover.Execute(plan, config, settings.AssetsPath, fileSystem, ConflictStrategy.Skip);
			}
			catch (Exception ex)
			{
				Logger.Error(LogCategory.Export, $"文件移动失败: {ex.Message}");
				return;
			}

	Logger.Info(LogCategory.Export, "CopyModFiles 和 CopyExternalStreamingAssets 已禁用（进程崩溃问题），请使用外部脚本 copy_mod.ps1 复制");

	// Identify stale catalog addresses: in scope, no matching bundle asset, no exported file
		HashSet<string> staleAddresses = IdentifyStaleAddresses(catalog, config, pathIdAddressMap, settings.AssetsPath, fileSystem);
		if (staleAddresses.Count > 0)
		{
			Logger.Info(LogCategory.Export, $"检测到 {staleAddresses.Count} 个过期 catalog 地址（bundle 中无对应资源，已从匹配率计算中排除）");

		}

		FullMatchReport fullMatchReport;
		try
		{
			FullMatchValidator validator = new();
			fullMatchReport = validator.Validate(plan, result, catalog, config, settings.AssetsPath, staleAddresses);
		}
			catch (Exception ex)
			{
				Logger.Warning(LogCategory.Export, $"100% 匹配验证失败: {ex.Message}");
				fullMatchReport = new FullMatchReport
				{
					TotalResources = 0,
					MatchedResources = result.Successes.Count,
					SkippedResources = result.Skipped.Count,
					FailedResources = result.Failures.Count,
					OverallMatchRate = 0.0,
					MatchMethodStats = new MatchMethodStatistics
					{
						PathIdAddressCount = 0,
						OriginalGuidCount = 0,
						PathIdCount = 0,
						NameCount = 0,
					},
					PathPrefixStats = [],
					UnmappedAddresses = [],
					FullMatchAchieved = false,
					StaleAddresses = [],
				};
			}

			try
			{
				FixReportGenerator reportGenerator = new();
				reportGenerator.Generate(result, plan, catalog, config, fullMatchReport, settings.AssetsPath, fileSystem);
			}
			catch (Exception ex)
			{
				Logger.Warning(LogCategory.Export, $"报告生成失败: {ex.Message}");
			}

			Logger.Info(LogCategory.Export, $"Addressables 路径修复完成: 总资源={plan.TotalCount}, 已映射={plan.MoveItems.Count}, 已移动={result.Successes.Count}, 跳过={result.Skipped.Count}, 失败={result.Failures.Count}, 匹配率={fullMatchReport.OverallMatchRate:F1}%");
		}
		catch (Exception ex)
		{
			Logger.Error(LogCategory.Export, $"Addressables 路径修复发生未预期异常: {ex.Message}");
		}

		try
		{
			NaninovelResourcePatcherPostExporter.RegisterBackgroundsResources(settings.AssetsPath, fileSystem);
		}
		catch (Exception ex)
		{
			Logger.Warning(LogCategory.Export, $"RegisterBackgroundsResources 失败: {ex.Message}");
		}
	}

	private static HashSet<string> IdentifyStaleAddresses(
		CatalogData catalog,
		NaninovelConfig config,
		PathIdAddressMap pathIdAddressMap,
		string assetsPath,
		FileSystem fileSystem)
	{
		HashSet<string> stale = new(StringComparer.OrdinalIgnoreCase);
		string resourcesBase = System.IO.Path.Join(assetsPath, "Resources");

		// Build set of addresses that have matching bundle assets
		HashSet<string> addressesWithBundleAsset = new(StringComparer.OrdinalIgnoreCase);
		foreach (PathIdAddressEntry entry in pathIdAddressMap.ByPathId.Values)
		{
			addressesWithBundleAsset.Add(entry.Address);
		}

		foreach (CatalogKeyEntry key in catalog.Keys)
		{
			string addr = key.Address;
			int slashIndex = addr.IndexOf('/');
			string prefix = slashIndex >= 0 ? addr[..slashIndex] : addr;
			if (!config.IsPathPrefixInScope(prefix))
			{
				continue;
			}

			if (addressesWithBundleAsset.Contains(addr))
			{
				continue;
			}

			// Check if a file exists at the expected path using System.IO directly for reliability
			string expectedPathNoExt = System.IO.Path.Join(resourcesBase, addr.Replace('/', System.IO.Path.DirectorySeparatorChar));
			string? dir = System.IO.Path.GetDirectoryName(expectedPathNoExt);
			string fileNameNoExt = System.IO.Path.GetFileNameWithoutExtension(expectedPathNoExt);
			if (dir is not null && System.IO.Directory.Exists(dir))
			{
				bool found = false;
				try
				{
					foreach (string filePath in System.IO.Directory.EnumerateFiles(dir, fileNameNoExt + ".*"))
					{
						if (!filePath.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
						{
							found = true;
							break;
						}
					}
				}
				catch { }
				if (found)
				{
					continue;
				}
			}

			stale.Add(addr);
		}

		return stale;
	}


	private static void DiagnoseUnmappedGuids(
		GameData gameData,
		CatalogData catalog,
		OriginalPathTable originalPathTable,
		NaninovelConfig config,
		string assetsPath,
		FileSystem fileSystem)
	{
		Dictionary<string, long> bundleContainerMap = BuildAssetBundleContainerMap(gameData);
		Dictionary<string, string> guidToFileIndex = BuildGuidToFileIndex(assetsPath);
		Dictionary<string, List<long>> nameToPathIds = BuildNameToPathIdMap(gameData);

		List<string> unmappedAddresses = new();
		Dictionary<string, string> addressToGuid = new();
		foreach (CatalogKeyEntry key in catalog.Keys)
		{
			if (!IsInScope(key.Address, config))
			{
				continue;
			}

			if (originalPathTable.TryGetByResourcePath(key.Address, out _))
			{
				continue;
			}

			string resourcesBase = Path.Join(assetsPath, "Resources");
			if (FileExistsAtAddress(resourcesBase, key.Address))
			{
				continue;
			}

			unmappedAddresses.Add(key.Address);
			if (!string.IsNullOrEmpty(key.Guid))
			{
				addressToGuid[key.Address] = key.Guid;
			}
		}


		int resolvedByGuid = 0;
		int resolvedByPathId = 0;
		int resolvedByContainer = 0;
		int resolvedByFuzzyName = 0;
		int resolvedByName = 0;
		int placeholderCreated = 0;
		int placeholderSkipped = 0;
		int failed = 0;

		foreach (string address in unmappedAddresses)
		{
			ResourceTypeEntry? typeEntry = catalog.GetAddressType(address);
			string className = typeEntry?.ClassName ?? "Unknown";
			string extension = GetExtensionForType(className);
			string targetPath = Path.Join(assetsPath, "Resources", address.Replace('/', Path.DirectorySeparatorChar) + extension);
			string? targetDir = Path.GetDirectoryName(targetPath);

			if (targetDir is null)
			{
				failed++;
				continue;
			}

			// Strategy 1: GUID matching
			if (addressToGuid.TryGetValue(address, out string? guid) && !string.IsNullOrEmpty(guid))
			{
				string? guidSourceFile = FindExportedFileByGuid(guid, guidToFileIndex);
				if (guidSourceFile is not null && IsValidExportedFile(guidSourceFile))
				{
					Directory.CreateDirectory(targetDir);
					File.Copy(guidSourceFile, targetPath, true);
					GenerateMetaFile(targetPath);
					resolvedByGuid++;

					continue;
				}
			}

			// Strategy 2: PathID matching
			long pathId = 0;
			bool foundInContainer = TryFindPathIdInContainer(bundleContainerMap, address, out pathId);

			if (foundInContainer)
			{
				string? pathIdSourceFile = FindExportedFileByPathId(pathId, assetsPath, extension);
				if (pathIdSourceFile is not null && IsValidExportedFile(pathIdSourceFile))
				{
					Directory.CreateDirectory(targetDir);
					File.Copy(pathIdSourceFile, targetPath, true);
					GenerateMetaFile(targetPath);
					resolvedByPathId++;

					continue;
				}
			}

			// Strategy 3: Container matching (existing logic)
			if (foundInContainer)
			{
				if (originalPathTable.TryGetByPathId(pathId, out OriginalPathEntry? entry) && entry is not null)
				{
					string? sourceFile = FindExportedFile(entry, assetsPath, extension);
					if (sourceFile is not null && IsValidExportedFile(sourceFile))
					{
						Directory.CreateDirectory(targetDir);
						File.Copy(sourceFile, targetPath, true);
						GenerateMetaFile(targetPath);
						resolvedByContainer++;
						continue;
					}
				}
			}

			// Strategy 4: Fuzzy name matching (existing logic)
			string? fuzzySourceFile = FindExportedFileByFuzzyName(address, className, extension, assetsPath);
			if (fuzzySourceFile is not null && IsValidExportedFile(fuzzySourceFile))
			{
				Directory.CreateDirectory(targetDir);
				File.Copy(fuzzySourceFile, targetPath, true);
				GenerateMetaFile(targetPath);
				resolvedByFuzzyName++;

				continue;
			}

			// Strategy 5: Name→PathID matching
			int lastSlashIdx = address.LastIndexOf('/');
			string assetName = lastSlashIdx >= 0 ? address[(lastSlashIdx + 1)..] : address;
			if (!string.IsNullOrEmpty(assetName) && nameToPathIds.TryGetValue(assetName, out List<long>? pathIds) && pathIds is not null)
			{
				bool nameMatched = false;
				foreach (long pid in pathIds)
				{
					string? nameSourceFile = FindExportedFileByPathId(pid, assetsPath, extension);
					if (nameSourceFile is not null && IsValidExportedFile(nameSourceFile))
					{
						Directory.CreateDirectory(targetDir);
						File.Copy(nameSourceFile, targetPath, true);
						GenerateMetaFile(targetPath);
						resolvedByName++;

						nameMatched = true;
						break;
					}
				}

				if (nameMatched)
				{
					continue;
				}
			}


			// Fallback: skip placeholder for types with invalid/missing source data
			if (className is "VideoClip" or "AudioClip")
			{
				placeholderSkipped++;
				continue;
			}

			// Fallback: placeholder
			Directory.CreateDirectory(targetDir);
			bool placeholderOk = CreatePlaceholderFile(targetPath, className);
			if (placeholderOk)
			{
				GenerateMetaFile(targetPath);
				placeholderCreated++;
			}
			else
			{
				failed++;
			}
		}

		Logger.Info(LogCategory.Export,
			$"诊断完成: 总未映射={unmappedAddresses.Count}, GUID匹配={resolvedByGuid}, PathID匹配={resolvedByPathId}, " +
			$"Container匹配={resolvedByContainer}, 模糊名称匹配={resolvedByFuzzyName}, Name→PathID匹配={resolvedByName}, " +
			$"占位文件={placeholderCreated}, 跳过占位={placeholderSkipped}, 失败={failed}");
	}

	private static Dictionary<string, long> BuildAssetBundleContainerMap(GameData gameData)
	{
		Dictionary<string, long> map = new(StringComparer.OrdinalIgnoreCase);
		int bundleCount = 0;

		foreach (SerializedAssetCollection collection in gameData.GameBundle.FetchAssetCollections().OfType<SerializedAssetCollection>())
		{
			foreach (IUnityObjectBase asset in collection)
			{
				if (asset.ClassName != "AssetBundle")
				{
					continue;
				}

				bundleCount++;
				System.Reflection.PropertyInfo? containerProp = asset.GetType().GetProperty("Container");
				if (containerProp is null)
				{
					continue;
				}

				object? container = containerProp.GetValue(asset);
				if (container is null)
				{
					continue;
				}

				foreach (object? item in (System.Collections.IEnumerable)container)
				{
					object? key = item?.GetType().GetProperty("Key")?.GetValue(item);
					object? value = item?.GetType().GetProperty("Value")?.GetValue(item);
					string keyStr = key?.ToString() ?? "";
					if (string.IsNullOrEmpty(keyStr))
					{
						continue;
					}

					long pathId = ExtractPathIdFromAssetInfo(value);
					if (pathId != 0)
					{
						map.TryAdd(keyStr, pathId);
					}
				}
			}
		}

		Logger.Info(LogCategory.Export, $"AssetBundle Container 映射: {map.Count} 个条目 (bundles={bundleCount})");
		return map;
	}

	private static long ExtractPathIdFromAssetInfo(object? assetInfo)
	{
		if (assetInfo is null)
		{
			return 0;
		}

		System.Reflection.PropertyInfo? assetProp = assetInfo.GetType().GetProperty("Asset");
		if (assetProp is null)
		{
			return 0;
		}

		object? assetPtr = assetProp.GetValue(assetInfo);
		if (assetPtr is null)
		{
			return 0;
		}

		System.Reflection.PropertyInfo? pathIdProp = assetPtr.GetType().GetProperty("PathID");
		if (pathIdProp is null)
		{
			return 0;
		}

		try
		{
			return (long)pathIdProp.GetValue(assetPtr)!;
		}
		catch
		{
			return 0;
		}
	}

	private static bool TryFindPathIdInContainer(Dictionary<string, long> containerMap, string address, out long pathId)
	{
		pathId = 0;

		if (containerMap.TryGetValue(address, out pathId))
		{
			return true;
		}

		string normalized = address.Replace('\\', '/').TrimStart('/');
		if (containerMap.TryGetValue(normalized, out pathId))
		{
			return true;
		}

		string lower = address.ToLowerInvariant();
		if (containerMap.TryGetValue(lower, out pathId))
		{
			return true;
		}

		foreach (KeyValuePair<string, long> kvp in containerMap)
		{
			string containerKey = kvp.Key.Replace('\\', '/').TrimStart('/');
			string containerKeyLower = containerKey.ToLowerInvariant();

			if (containerKeyLower.Equals(lower, StringComparison.Ordinal))
			{
				pathId = kvp.Value;
				return true;
			}

			if (containerKeyLower.EndsWith("/" + lower, StringComparison.Ordinal))
			{
				pathId = kvp.Value;
				return true;
			}

			if (containerKeyLower.StartsWith("assets/resources/", StringComparison.Ordinal))
			{
				string stripped = containerKeyLower["assets/resources/".Length..];
				if (stripped.Equals(lower, StringComparison.Ordinal))
				{
					pathId = kvp.Value;
					return true;
				}
			}

			int dotIndex = containerKeyLower.LastIndexOf('.');
			if (dotIndex > 0)
			{
				string noExt = containerKeyLower[..dotIndex];
				if (noExt.EndsWith("/" + lower, StringComparison.Ordinal) || noExt.Equals(lower, StringComparison.Ordinal))
				{
					pathId = kvp.Value;
					return true;
				}
			}
		}

		return false;
	}

	private static string? FindExportedFile(OriginalPathEntry entry, string assetsPath, string extension)
	{
		string name = entry.Name;
		if (string.IsNullOrEmpty(name))
		{
			name = entry.OriginalName ?? entry.ClassName;
		}
		name = SanitizeFileName(name);

		string[] typeDirs = GetTypeDirectoriesForClass(entry.ClassName);
		foreach (string typeDir in typeDirs)
		{
			string typeDirPath = Path.Join(assetsPath, typeDir);
			if (!Directory.Exists(typeDirPath))
			{
				continue;
			}

			foreach (string file in Directory.EnumerateFiles(typeDirPath, name + "*"))
			{
				string fileName = Path.GetFileName(file);
				if (fileName.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}
				if (fileName.Equals(name, StringComparison.OrdinalIgnoreCase) || fileName.StartsWith(name + ".", StringComparison.OrdinalIgnoreCase))
				{
					return file;
				}
			}
		}

		if (entry.OriginalDirectory is not null)
		{
			string dir = entry.OriginalDirectory.Replace('\\', '/');
			string relativeDir = dir.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) ? dir["Assets/".Length..] : dir;
			string searchDir = Path.Join(assetsPath, relativeDir);
			if (Directory.Exists(searchDir))
			{
				foreach (string file in Directory.EnumerateFiles(searchDir, name + "*"))
				{
					string fileName = Path.GetFileName(file);
					if (fileName.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
					{
						continue;
					}
					if (fileName.Equals(name, StringComparison.OrdinalIgnoreCase) || fileName.StartsWith(name + ".", StringComparison.OrdinalIgnoreCase))
					{
						return file;
					}
				}
			}
		}

		return null;
	}

	private static string? FindExportedFileByFuzzyName(string address, string className, string extension, string assetsPath)
	{
		int lastSlash = address.LastIndexOf('/');
		string fileName = lastSlash >= 0 ? address[(lastSlash + 1)..] : address;
		fileName = SanitizeFileName(fileName);

		if (string.IsNullOrEmpty(fileName))
		{
			return null;
		}

		string[] typeDirs = GetTypeDirectoriesForClass(className);
		foreach (string typeDir in typeDirs)
		{
			string typeDirPath = Path.Join(assetsPath, typeDir);
			if (!Directory.Exists(typeDirPath))
			{
				continue;
			}

			string exactPath = Path.Join(typeDirPath, fileName + extension);
			if (File.Exists(exactPath))
			{
				return exactPath;
			}

			foreach (string file in Directory.EnumerateFiles(typeDirPath, fileName + ".*"))
			{
				string fn = Path.GetFileName(file);
				if (!fn.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
				{
					return file;
				}
			}
		}

		return null;
	}

	private static bool FileExistsAtAddress(string resourcesBase, string address)
	{
		string[] extensions = [".png", ".jpg", ".jpeg", ".wav", ".mp3", ".txt", ".bytes", ".mp4", ".asset", ".prefab", ".mat", ".anim", ".controller", ".ttf"];
		string basePath = Path.Join(resourcesBase, address.Replace('/', Path.DirectorySeparatorChar));
		string? dir = Path.GetDirectoryName(basePath);
		string fileNameNoExt = Path.GetFileNameWithoutExtension(basePath);
		if (dir is null || !Directory.Exists(dir))
		{
			return false;
		}

		foreach (string ext in extensions)
		{
			if (File.Exists(Path.Combine(dir, fileNameNoExt + ext)))
			{
				return true;
			}
		}
		return false;
	}

	private static void CleanupPlaceholderFiles(string assetsPath)
	{
		string resourcesDir = Path.Join(assetsPath, "Resources");
		if (!Directory.Exists(resourcesDir))
		{
			return;
		}

		string[] realExtensions = [".jpg", ".jpeg", ".wav", ".mp3", ".txt", ".bytes", ".mp4", ".webm", ".asset", ".prefab", ".mat", ".anim", ".controller", ".ttf", ".otf", ".nani", ".bin", ".audioclip", ".renderTexture"];
		int deletedCount = 0;
		int metaDeletedCount = 0;

		try
		{
			IEnumerable<string> allFiles = Directory.EnumerateFiles(resourcesDir, "*.*", SearchOption.AllDirectories);
			List<string> placeholderFiles = new();

			foreach (string filePath in allFiles)
			{
				try
				{
				FileInfo fi = new(filePath);
					if (fi.Length > 100 || fi.Extension is ".meta")
					{
						continue;
					}

					string? dir = Path.GetDirectoryName(filePath);
					string fileNameNoExt = Path.GetFileNameWithoutExtension(filePath);
					if (dir is null || string.IsNullOrEmpty(fileNameNoExt))
					{
						continue;
					}

					foreach (string ext in realExtensions)
					{
						string realPath = Path.Combine(dir, fileNameNoExt + ext);
						if (File.Exists(realPath))
						{
							FileInfo realFi = new(realPath);
							if (realFi.Length > 100)
							{
								placeholderFiles.Add(filePath);
								break;
							}
						}
					}
				}
				catch { }
			}

			foreach (string placeholderPath in placeholderFiles)
			{
				try
				{
					File.Delete(placeholderPath);
					deletedCount++;
					string metaPath = placeholderPath + ".meta";
					if (File.Exists(metaPath))
					{
						File.Delete(metaPath);
						metaDeletedCount++;
					}
				}
				catch (Exception ex)
				{
					Logger.Warning(LogCategory.Export, $"清理占位文件失败: {placeholderPath}: {ex.Message}");
				}
			}
		}
		catch (Exception ex)
		{
			Logger.Warning(LogCategory.Export, $"CleanupPlaceholderFiles 异常: {ex.Message}");
		}

		if (deletedCount > 0)
		{
			Logger.Info(LogCategory.Export, $"占位文件清理: 删除 {deletedCount} 个占位文件及 {metaDeletedCount} 个 .meta 文件（已有真实文件替代）");
		}
	}

	private static void FixEmptyYamlFiles(string assetsPath)
	{
		Logger.Info(LogCategory.Export, $"FixEmptyYamlFiles 开始扫描: {assetsPath}");
		string[] extensions = [".asset", ".controller", ".anim", ".prefab", ".mat", ".shader", ".computeShader"];
		int fixedCount = 0;
		try
		{
			foreach (string ext in extensions)
			{
				foreach (string file in Directory.EnumerateFiles(assetsPath, "*" + ext, SearchOption.AllDirectories))
				{
					try
					{
						if (new FileInfo(file).Length == 0)
						{
							File.WriteAllText(file, "%YAML 1.1\n--- {}\n", new UTF8Encoding(false));
							fixedCount++;
							Logger.Warning(LogCategory.Export, $"修复空 YAML 文件: {file}");
						}
					}
					catch { }
				}
			}
		}
		catch (Exception ex)
		{
			Logger.Warning(LogCategory.Export, $"FixEmptyYamlFiles 异常: {ex.Message}");
		}

		if (fixedCount > 0)
		{
			Logger.Info(LogCategory.Export, $"空 YAML 文件修复: 共修复 {fixedCount} 个空文件");
		}
	}

	private static Dictionary<string, List<long>> BuildNameToPathIdMap(GameData gameData)
	{
		Dictionary<string, List<long>> map = new(StringComparer.OrdinalIgnoreCase);
		foreach (SerializedAssetCollection collection in gameData.GameBundle.FetchAssetCollections().OfType<SerializedAssetCollection>())
		{
			foreach (IUnityObjectBase asset in collection)
			{
				string? name = asset.TryGetName();
				if (string.IsNullOrEmpty(name))
				{
					continue;
				}

				if (!map.TryGetValue(name, out List<long>? pathIds))
				{
					pathIds = new List<long>();
					map[name] = pathIds;
				}

				pathIds.Add(asset.PathID);
			}
		}

		return map;
	}

	private static Dictionary<string, string> BuildGuidToFileIndex(string assetsPath)
	{
		Dictionary<string, string> index = new(StringComparer.OrdinalIgnoreCase);
		foreach (string typeDir in ExportTypeDirectories)
		{
			string typeDirPath = Path.Join(assetsPath, typeDir);
			if (!Directory.Exists(typeDirPath))
			{
				continue;
			}

			foreach (string metaFile in Directory.EnumerateFiles(typeDirPath, "*.meta", SearchOption.TopDirectoryOnly))
			{
				try
				{
					string metaContent = File.ReadAllText(metaFile);
					Match match = GuidMetaRegex.Match(metaContent);
					if (match.Success)
					{
						string guid = match.Groups[1].Value.ToLowerInvariant();
						string assetFile = metaFile[..^5]; // remove ".meta"
						if (!IsValidExportedFile(assetFile))
						{
							continue;
						}

						if (index.TryGetValue(guid, out string? existing))
						{
							Logger.Warning(LogCategory.Export, $"  GUID 冲突: {guid} -> '{existing}' 和 '{assetFile}'");
						}

						index[guid] = assetFile;
					}
				}
				catch (Exception ex)
				{
					Logger.Warning(LogCategory.Export, $"  读取 .meta 失败: {metaFile} - {ex.Message}");
				}
			}
		}

		return index;
	}

	private static string? FindExportedFileByGuid(string guid, IReadOnlyDictionary<string, string> guidToFileIndex)
	{
		if (string.IsNullOrEmpty(guid))
		{
			return null;
		}

		string normalizedGuid = guid.ToLowerInvariant();
		return guidToFileIndex.TryGetValue(normalizedGuid, out string? filePath) ? filePath : null;
	}

	private static string? FindExportedFileByPathId(long pathId, string assetsPath, string extension)
	{
		if (pathId <= 0)
		{
			return null;
		}

		string pathIdStr = pathId.ToString();
		List<string> candidates = new();

		foreach (string typeDir in ExportTypeDirectories)
		{
			string typeDirPath = Path.Join(assetsPath, typeDir);
			if (!Directory.Exists(typeDirPath))
			{
				continue;
			}

			string exactPath = Path.Join(typeDirPath, pathIdStr + extension);
			if (File.Exists(exactPath))
			{
				candidates.Add(exactPath);
			}

			foreach (string file in Directory.EnumerateFiles(typeDirPath, pathIdStr + "*"))
			{
				string fileName = Path.GetFileName(file);
				if (fileName.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}

				if (fileName.StartsWith(pathIdStr + ".", StringComparison.OrdinalIgnoreCase) ||
					fileName.Equals(pathIdStr + extension, StringComparison.OrdinalIgnoreCase))
				{
					if (!candidates.Contains(file))
					{
						candidates.Add(file);
					}
				}
			}
		}

		if (candidates.Count == 0)
		{
			return null;
		}

		if (candidates.Count == 1)
		{
			return candidates[0];
		}

		candidates.Sort((a, b) => new FileInfo(b).Length.CompareTo(new FileInfo(a).Length));
		Logger.Warning(LogCategory.Export, $"  PathID={pathId} 多候选文件, 选择最大: {candidates[0]}");
		return candidates[0];
	}

	private static bool IsValidExportedFile(string filePath)
	{
		string ext = Path.GetExtension(filePath).ToLowerInvariant();
		return ext != ".bin";
	}

	private static bool CreatePlaceholderFile(string targetPath, string className)
	{
		try
		{
			switch (className)
			{
				case "TextAsset":
					File.WriteAllText(targetPath, "", new UTF8Encoding(false));
					return true;
				case "VideoClip":
					File.WriteAllBytes(targetPath, []);
					return true;
				case "AudioClip":
					byte[] wavHeader = new byte[44];
					wavHeader[0] = (byte)'R'; wavHeader[1] = (byte)'I'; wavHeader[2] = (byte)'F'; wavHeader[3] = (byte)'F';
					wavHeader[8] = (byte)'W'; wavHeader[9] = (byte)'A'; wavHeader[10] = (byte)'V'; wavHeader[11] = (byte)'E';
					wavHeader[12] = (byte)'f'; wavHeader[13] = (byte)'m'; wavHeader[14] = (byte)'t'; wavHeader[15] = (byte)' ';
					wavHeader[16] = 16; wavHeader[20] = 1; wavHeader[22] = 1; wavHeader[24] = 44;
					wavHeader[32] = 1; wavHeader[34] = 8;
					wavHeader[36] = (byte)'d'; wavHeader[37] = (byte)'a'; wavHeader[38] = (byte)'t'; wavHeader[39] = (byte)'a';
					File.WriteAllBytes(targetPath, wavHeader);
					return true;
				case "Texture2D":
					byte[] pngHeader = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
					File.WriteAllBytes(targetPath, pngHeader);
					return true;
				default:
					File.WriteAllBytes(targetPath, []);
					return true;
			}
		}
		catch (Exception ex)
		{
			Logger.Warning(LogCategory.Export, $"创建占位文件异常: {ex.Message}");
			return false;
		}
	}

	private static void GenerateMetaFile(string filePath)
	{
		string metaPath = filePath + ".meta";
		if (File.Exists(metaPath))
		{
			return;
		}

		string guid = System.Guid.NewGuid().ToString("N");
		if (TextureMetaWriter.IsImageFile(filePath))
		{
			try
			{
				TextureMetaWriter.WriteDefaultTextureMeta(filePath, guid);
				return;
			}
			catch (Exception ex)
			{
				Logger.Warning(LogCategory.Export, $"Failed to write TextureImporter .meta for {filePath}: {ex.Message}, falling back to simplified .meta");
			}
		}
		string content = $"fileFormatVersion: 2\nguid: {guid}\n";
		File.WriteAllText(metaPath, content, new UTF8Encoding(false));
	}

	private static string GetExtensionForType(string className)
	{
		return className switch
		{
			"Texture2D" => ".png",
			"Sprite" => ".asset",
			"TextAsset" => ".txt",
			"AudioClip" => ".wav",
			"VideoClip" => ".mp4",
			"GameObject" => ".prefab",
			"Material" => ".mat",
			"AnimationClip" => ".anim",
			"AnimatorController" => ".controller",
			"Font" => ".ttf",
			"RenderTexture" => ".asset",
			_ => ".asset",
		};
	}

	private static string[] GetTypeDirectoriesForClass(string className)
	{
		return className switch
		{
			"Texture2D" => ["Texture2D", "Resources"],
			"Sprite" => ["Sprite", "Resources"],
			"TextAsset" => ["TextAsset", "Resources"],
			"AudioClip" => ["AudioClip", "Resources"],
			"VideoClip" => ["VideoClip", "Resources"],
			"GameObject" => ["GameObject", "Resources"],
			"Material" => ["Material", "Resources"],
			"AnimationClip" => ["AnimationClip", "Resources"],
			"AnimatorController" => ["AnimatorController", "Resources"],
			"Font" => ["Font", "Resources"],
			"RenderTexture" => ["RenderTexture", "Resources"],
			_ => ["Resources"],
		};
	}

	private static string SanitizeFileName(string name)
	{
		return name.Replace('/', '_').Replace('\\', '_').Trim();
	}

	private static bool IsInScope(string address, NaninovelConfig config)
	{
		int slashIndex = address.IndexOf('/');
		string prefix = slashIndex >= 0 ? address[..slashIndex] : address;
		return config.IsPathPrefixInScope(prefix);
	}

	private static void CopyModFiles(FullConfiguration settings, FileSystem fileSystem)
	{
		if (string.IsNullOrEmpty(settings.SourceDataPath))
		{
			return;
		}

		string sourceBgDir = Path.Join(settings.SourceDataPath, "Resources", "Backgrounds");
		Logger.Info(LogCategory.Export, $"CopyModFiles: 源目录={sourceBgDir}, 存在={Directory.Exists(sourceBgDir)}");
		if (!Directory.Exists(sourceBgDir))
		{
			Logger.Warning(LogCategory.Export, $"CopyModFiles: 源目录不存在: {sourceBgDir}");
			return;
		}

		string targetBgDir = Path.Join(settings.AssetsPath, "Resources", "Backgrounds");
		Directory.CreateDirectory(targetBgDir);

		int copied = 0;
		int overwritten = 0;
		int skipped = 0;
		int scanned = 0;

		Logger.Info(LogCategory.Export, "CopyModFiles: 开始安全递归枚举");
		Stack<string> dirStack = new();
		dirStack.Push(sourceBgDir);
		while (dirStack.Count > 0)
		{
			string currentDir = dirStack.Pop();
			string[] files;
			string[] subDirs;
			try
			{
				files = Directory.GetFiles(currentDir);
			}
			catch (Exception ex)
			{
				Logger.Warning(LogCategory.Export, $"CopyModFiles: 跳过目录 {currentDir}: {ex.Message}");
				continue;
			}
			try
			{
				subDirs = Directory.GetDirectories(currentDir);
			}
			catch (Exception ex)
			{
				Logger.Warning(LogCategory.Export, $"CopyModFiles: 跳过子目录 {currentDir}: {ex.Message}");
				subDirs = Array.Empty<string>();
			}

			foreach (string subDir in subDirs)
			{
				dirStack.Push(subDir);
			}

			foreach (string sourceFile in files)
			{
				scanned++;
				if (scanned % 100 == 0)
				{
					Logger.Info(LogCategory.Export, $"CopyModFiles: 已扫描 {scanned}");
					Console.Out.Flush();
				}
				string fileName = Path.GetFileName(sourceFile);
				if (!fileName.Contains("mod", StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}

				try
				{
					string relativePath = Path.GetRelativePath(sourceBgDir, sourceFile);
					string targetFile = Path.Join(targetBgDir, relativePath);
					string? targetDir = Path.GetDirectoryName(targetFile);
					if (targetDir is not null)
					{
						Directory.CreateDirectory(targetDir);
					}

					if (File.Exists(targetFile))
					{
						File.Copy(sourceFile, targetFile, true);
						overwritten++;
					}
					else
					{
						File.Copy(sourceFile, targetFile, false);
						copied++;
						GenerateMetaFile(targetFile);
					}
				}
				catch (Exception ex)
				{
					Logger.Warning(LogCategory.Export, $"CopyModFiles: 复制失败 {sourceFile}: {ex.Message}");
				}
			}
		}


		Logger.Info(LogCategory.Export, $"Mod 文件复制完成: 新增={copied}, 覆盖={overwritten}, 跳过={skipped}");
	}

	private static void CopyExternalStreamingAssets(FullConfiguration settings, FileSystem fileSystem)
	{
		if (string.IsNullOrEmpty(settings.SourceDataPath))
		{
			return;
		}

		string gameFolderName = Path.GetFileName(settings.SourceDataPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
		if (string.IsNullOrEmpty(gameFolderName))
		{
			return;
		}

		string? externalSAPath = FindExternalStreamingAssets(gameFolderName);
		if (externalSAPath is null)
		{
			Logger.Info(LogCategory.Export, "CopyExternalStreamingAssets: 未找到外部 AutoTranslator StreamingAssets 目录");
			return;
		}

		Logger.Info(LogCategory.Export, $"CopyExternalStreamingAssets: 找到外部目录: {externalSAPath}");

		string targetSADir = Path.Join(settings.AssetsPath, "StreamingAssets");
		Directory.CreateDirectory(targetSADir);

		int copied = 0;
		int overwritten = 0;

		foreach (string sourceFile in Directory.EnumerateFiles(externalSAPath, "*", SearchOption.AllDirectories))
		{
			string relativePath = Path.GetRelativePath(externalSAPath, sourceFile);
			string targetFile = Path.Join(targetSADir, relativePath);
			string? targetDir = Path.GetDirectoryName(targetFile);
			if (targetDir is not null)
			{
				Directory.CreateDirectory(targetDir);
			}

			if (File.Exists(targetFile))
			{
				long sourceLen = new FileInfo(sourceFile).Length;
				long targetLen = new FileInfo(targetFile).Length;
				if (sourceLen != targetLen)
				{
					File.Copy(sourceFile, targetFile, true);
					overwritten++;
				}
			}
			else
			{
				File.Copy(sourceFile, targetFile, false);
				copied++;
				GenerateMetaFile(targetFile);
			}
		}

		Logger.Info(LogCategory.Export, $"外部 StreamingAssets 复制完成: 新增={copied}, 覆盖={overwritten}");
	}

	private static string? FindExternalStreamingAssets(string gameFolderName)
	{
		foreach (DriveInfo drive in DriveInfo.GetDrives().Where(d => d.IsReady))
		{
			try
			{
				foreach (string dir in Directory.EnumerateDirectories(drive.RootDirectory.FullName, "*AutoTranslator*", SearchOption.TopDirectoryOnly))
				{
					try
					{
						foreach (string subDir in Directory.EnumerateDirectories(dir, "*", SearchOption.AllDirectories))
						{
							if (Path.GetFileName(subDir) == gameFolderName)
							{
								string saPath = Path.Join(subDir, "StreamingAssets");
								if (Directory.Exists(saPath))
								{
									return saPath;
								}
							}
						}
					}
					catch { }
				}
			}
			catch { }
		}
		return null;
	}
}
