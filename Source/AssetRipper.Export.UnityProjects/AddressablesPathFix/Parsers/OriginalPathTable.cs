using AssetRipper.Assets;
using AssetRipper.Assets.Collections;
using AssetRipper.Import.Logging;
using AssetRipper.Processing;
using AssetRipper.Export.UnityProjects.AddressablesPathFix.Models;

#pragma warning disable IL2075

namespace AssetRipper.Export.UnityProjects.AddressablesPathFix.Parsers;

public sealed class OriginalPathTable
{
	private const string ResourcesPrefix = "Assets/Resources/";
	private const string AssetBundlesPrefix = "Assets/AssetBundles/";

	private readonly Dictionary<string, OriginalPathEntry> _byResourcePath = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<long, OriginalPathEntry> _byPathId = new();
	private readonly Dictionary<string, List<OriginalPathEntry>> _byNameAndType = new(StringComparer.OrdinalIgnoreCase);

	public int Count => _byResourcePath.Count;
	public int TotalAssets { get; private set; }
	public int WithOriginalPath { get; private set; }
	public int WithResourcePath { get; private set; }
	public int WithoutOriginalPath { get; private set; }

	public IReadOnlyDictionary<string, OriginalPathEntry> ByResourcePath => _byResourcePath;
	public IReadOnlyDictionary<long, OriginalPathEntry> ByPathId => _byPathId;

	public static OriginalPathTable Build(GameData gameData)
	{
		OriginalPathTable table = new OriginalPathTable();

		try
		{
			foreach (SerializedAssetCollection collection in gameData.GameBundle.FetchAssetCollections().OfType<SerializedAssetCollection>())
			{
				foreach (IUnityObjectBase asset in collection)
				{
					table.TotalAssets++;
					string name = (asset as INamed)?.Name ?? string.Empty;
					string className = asset.ClassName;
					long pathId = asset.PathID;

					string? originalPath = asset.OriginalPath;
					string? originalDir = asset.OriginalDirectory;
					string? originalName = asset.OriginalName;

					string? resourcePath = null;
					if (originalPath is not null)
					{
						table.WithOriginalPath++;
						string normalizedPath = originalPath.Replace('\\', '/');
						if (normalizedPath.StartsWith(ResourcesPrefix, StringComparison.OrdinalIgnoreCase))
						{
							resourcePath = normalizedPath[ResourcesPrefix.Length..];
							table.WithResourcePath++;
						}
					}
					else
					{
						table.WithoutOriginalPath++;
					}

					OriginalPathEntry entry = new()
					{
						PathID = pathId,
						Name = name,
						ClassName = className,
						OriginalPath = originalPath,
						OriginalDirectory = originalDir,
						OriginalName = originalName,
						ResourcePath = resourcePath,
					};

					if (resourcePath is not null)
					{
						table._byResourcePath.TryAdd(resourcePath, entry);
					}

					table._byPathId[pathId] = entry;

					string nameTypeKey = $"{name}|{className}";
					if (!table._byNameAndType.TryGetValue(nameTypeKey, out List<OriginalPathEntry>? list))
					{
						list = [];
						table._byNameAndType[nameTypeKey] = list;
					}
					list.Add(entry);
				}
			}
		}
		catch (Exception ex)
		{
			Logger.Warning(LogCategory.Export, $"OriginalPathTable 构建失败: {ex.Message}");
		}

		Logger.Info(LogCategory.Export,
			$"OriginalPathTable: 总资产={table.TotalAssets}, 有OriginalPath={table.WithOriginalPath}, 有ResourcePath={table.WithResourcePath}, 无OriginalPath={table.WithoutOriginalPath}, 资源路径映射={table._byResourcePath.Count}");

		int bgCount = 0;
		foreach (KeyValuePair<string, OriginalPathEntry> kvp in table._byResourcePath)
		{
			if (kvp.Key.StartsWith("Backgrounds", StringComparison.OrdinalIgnoreCase))
			{
				bgCount++;
				if (bgCount <= 5)
				{
					Logger.Info(LogCategory.Export, $"  OriginalPathTable Backgrounds: path='{kvp.Key}' Name='{kvp.Value.Name}' Class='{kvp.Value.ClassName}'");
				}
			}
		}
		Logger.Info(LogCategory.Export, $"  OriginalPathTable Backgrounds count: {bgCount}");

		int rmCount = 0;
		foreach (KeyValuePair<long, OriginalPathEntry> kvp in table._byPathId)
		{
			if (kvp.Value.ClassName.Contains("ResourceManager", StringComparison.OrdinalIgnoreCase) || kvp.Value.ClassName.Contains("PreloadData", StringComparison.OrdinalIgnoreCase))
			{
				rmCount++;
				if (rmCount <= 5)
				{
					Logger.Info(LogCategory.Export, $"  ResourceManager-like asset: PathID={kvp.Key} Name='{kvp.Value.Name}' Class='{kvp.Value.ClassName}' OriginalPath='{kvp.Value.OriginalPath}'");
				}
			}
		}
		Logger.Info(LogCategory.Export, $"  ResourceManager-like assets count: {rmCount}");

		try
		{
			int rmFound = 0;
			foreach (SerializedAssetCollection collection in gameData.GameBundle.FetchAssetCollections().OfType<SerializedAssetCollection>())
			{
				foreach (IUnityObjectBase asset in collection)
				{
					if (asset.ClassName == "ResourceManager")
					{
						rmFound++;
						Logger.Info(LogCategory.Export, $"  Exploring ResourceManager: PathID={asset.PathID} Collection={collection.Name}");

						System.Reflection.PropertyInfo? containerProp = asset.GetType().GetProperty("Container");
						if (containerProp is not null)
						{
							object? container = containerProp.GetValue(asset);
							if (container is not null)
							{
						Logger.Info(LogCategory.Export, $"    Container type: {container.GetType().Name}");
							int entryCount = 0;
							int bgKeyCount = 0;
							int sampleCount = 0;
							System.Collections.Generic.HashSet<long> containerPathIds = new();
							System.Collections.Generic.Dictionary<string, long> containerKeyToPathId = new();
							foreach (object? item in (System.Collections.IEnumerable)container)
							{
								entryCount++;
								object? key = item?.GetType().GetProperty("Key")?.GetValue(item);
								object? value = item?.GetType().GetProperty("Value")?.GetValue(item);
								string keyStr = key?.ToString() ?? "";
								long pid = 0;
								if (value is not null)
								{
									System.Reflection.PropertyInfo? pathIdProp = value.GetType().GetProperty("PathID");
									if (pathIdProp is not null)
									{
										pid = (long)pathIdProp.GetValue(value)!;
										containerPathIds.Add(pid);
									}
								}
								containerKeyToPathId[keyStr] = pid;
								if (keyStr.Contains("backgrounds", StringComparison.OrdinalIgnoreCase) || keyStr.Contains("mainbackground", StringComparison.OrdinalIgnoreCase))
								{
									bgKeyCount++;
									if (bgKeyCount <= 20)
									{
										Logger.Info(LogCategory.Export, $"    Container BG key[{bgKeyCount}]: '{keyStr}' PathID={pid}");
									}
								}
								if (sampleCount < 5)
								{
									Logger.Info(LogCategory.Export, $"    Container sample[{entryCount}]: key='{keyStr}' PathID={pid}");
									sampleCount++;
								}
							}
							Logger.Info(LogCategory.Export, $"    Container total entries: {entryCount}, unique PathIDs: {containerPathIds.Count}, BG keys: {bgKeyCount}");

							// Check specific Backgrounds addresses from catalog
							string[] testAddresses = [
								"backgrounds/mainbackground/abandonhouse",
								"backgrounds/mainbackground/abortion",
								"backgrounds/mainbackground/airplane",
								"backgrounds/mainbackground/city",
								"backgrounds/mainbackground/home",
							];
							foreach (string testAddr in testAddresses)
							{
								bool found = containerKeyToPathId.ContainsKey(testAddr);
								Logger.Info(LogCategory.Export, $"    Container has '{testAddr}': {found}");
							}

							// Count keys by prefix
							var prefixCounts = new System.Collections.Generic.Dictionary<string, int>();
							foreach (string k in containerKeyToPathId.Keys)
							{
								string prefix = k.Contains('/') ? k[..k.IndexOf('/')] : k;
								if (!prefixCounts.TryGetValue(prefix, out int c))
									c = 0;
								prefixCounts[prefix] = c + 1;
							}
							foreach (var pc in prefixCounts.OrderByDescending(x => x.Value).Take(20))
							{
								Logger.Info(LogCategory.Export, $"    Container prefix '{pc.Key}': {pc.Value} entries");
							}

							int inContainer = 0;
							int notInContainer = 0;
							int notInContainerTexture = 0;
							foreach (KeyValuePair<long, OriginalPathEntry> kvp in table._byPathId)
							{
								if (containerPathIds.Contains(kvp.Key))
								{
									inContainer++;
								}
								else
								{
									notInContainer++;
									if (kvp.Value.ClassName == "Texture2D" && !string.IsNullOrEmpty(kvp.Value.Name))
									{
										notInContainerTexture++;
									}
								}
							}
							Logger.Info(LogCategory.Export, $"    Assets in Container: {inContainer}, not in Container: {notInContainer}, Texture2D not in Container: {notInContainerTexture}");
							}
						}
					}
				}
			}
			Logger.Info(LogCategory.Export, $"  ResourceManager found in exploration: {rmFound}");

		int abCount = 0;
		int abBgCount = 0;
		try
		{
			foreach (SerializedAssetCollection collection in gameData.GameBundle.FetchAssetCollections().OfType<SerializedAssetCollection>())
			{
				foreach (IUnityObjectBase asset in collection)
				{
					if (asset.ClassName == "AssetBundle")
					{
						abCount++;
						System.Reflection.PropertyInfo? containerProp = asset.GetType().GetProperty("Container");
						if (containerProp is not null)
						{
							object? container = containerProp.GetValue(asset);
							if (container is not null)
							{
								foreach (object? item in (System.Collections.IEnumerable)container)
								{
									object? key = item?.GetType().GetProperty("Key")?.GetValue(item);
									string keyStr = key?.ToString() ?? "";
									if (keyStr.Contains("backgrounds", StringComparison.OrdinalIgnoreCase) || keyStr.Contains("abandonhouse", StringComparison.OrdinalIgnoreCase))
									{
										abBgCount++;
										if (abBgCount <= 10)
										{
											object? value = item?.GetType().GetProperty("Value")?.GetValue(item);
											Logger.Info(LogCategory.Export, $"  AssetBundle Container BG: bundle='{asset.GetBestName()}' key='{keyStr}' value={value}");
										}
									}
								}
							}
						}
					}
				}
			}
			Logger.Info(LogCategory.Export, $"  AssetBundle count: {abCount}, BG entries: {abBgCount}");
		}
		catch (Exception ex)
		{
			Logger.Warning(LogCategory.Export, $"AssetBundle Container exploration failed: {ex.Message}");
		}
		}
		catch (Exception ex)
		{
			Logger.Warning(LogCategory.Export, $"ResourceManager Container exploration failed: {ex.Message}");
		}

		return table;
	}

	public bool TryGetByResourcePath(string resourcePath, out OriginalPathEntry? entry)
	{
		return _byResourcePath.TryGetValue(resourcePath, out entry);
	}

	public bool TryGetByPathId(long pathId, out OriginalPathEntry? entry)
	{
		return _byPathId.TryGetValue(pathId, out entry);
	}

	public IReadOnlyList<OriginalPathEntry> GetByNameAndType(string name, string className)
	{
		string key = $"{name}|{className}";
		return _byNameAndType.TryGetValue(key, out List<OriginalPathEntry>? list) ? list : [];
	}
}