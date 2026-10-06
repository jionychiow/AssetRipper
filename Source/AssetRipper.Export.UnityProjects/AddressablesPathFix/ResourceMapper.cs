using System.Globalization;
using System.Text.RegularExpressions;
using AssetRipper.Export.UnityProjects.AddressablesPathFix.Models;
using AssetRipper.Export.UnityProjects.AddressablesPathFix.Parsers;
using AssetRipper.Import.Logging;

namespace AssetRipper.Export.UnityProjects.AddressablesPathFix;

public sealed class ResourceMapper
{
	private static readonly string[] TypeDirectories =
	[
		"Texture2D",
		"Sprite",
		"AudioClip",
		"TextAsset",
		"VideoClip",
		"GameObject",
		"Material",
		"Font",
		"RenderTexture",
		"AnimationClip",
		"AnimatorController",
	];

	private static readonly IReadOnlyDictionary<string, string> TypeToExtension = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
	{
		["Texture2D"] = ".png",
		["Sprite"] = ".asset",
		["TextAsset"] = ".txt",
		["AudioClip"] = ".wav",
		["VideoClip"] = ".mp4",
		["GameObject"] = ".prefab",
		["Material"] = ".mat",
		["AnimationClip"] = ".anim",
		["AnimatorController"] = ".controller",
		["Font"] = ".asset",
		["RenderTexture"] = ".asset",
	};

	private static readonly Regex GuidRegex = new(@"guid:\s*([0-9a-fA-F]{32})", RegexOptions.Compiled);

	public MovePlan Build(CatalogData catalog, NaninovelConfig config, PathIdNameMap pathIdMap, PathIdAddressMap pathIdAddressMap, string assetsPath, FileSystem fileSystem)
	{
		List<MoveItem> moveItems = new(512);
		List<UnmappedResource> unmappedResources = new(128);

		int pathIdAddressMatches = 0;
		int guidMatches = 0;
		int pathIdMatches = 0;
		int nameMatches = 0;

		IReadOnlyDictionary<string, IReadOnlyList<string>> addressByLastSegment = BuildAddressByLastSegment(catalog);

		bool strategyDAvailable = pathIdAddressMap is not null && pathIdAddressMap.Count > 0;
		if (!strategyDAvailable)
		{
			Logger.Warning(LogCategory.Export, "策略 D (PathID→地址直接映射) 不可用，降级为策略 A/B/C");
		}

		foreach (ExportedResourceFile resource in EnumerateExportedResources(assetsPath, fileSystem))
		{
			string? address = null;
			MatchMethod method = MatchMethod.Name;

			if (strategyDAvailable)
			{
				address = MatchByPathIdAddress(resource.FileName, pathIdAddressMap!);
				method = MatchMethod.PathIdAddress;
			}

			if (address is null)
			{
				address = MatchByGuid(resource.Guid, catalog);
				method = MatchMethod.OriginalGuid;
			}

			if (address is null)
			{
				address = MatchByPathId(resource.FileName, pathIdMap, addressByLastSegment);
				method = MatchMethod.PathId;
			}

			if (address is null)
			{
				address = MatchByName(resource.FileName, resource.Extension, pathIdMap, addressByLastSegment);
				method = MatchMethod.Name;
			}

			if (address is null)
			{
				unmappedResources.Add(new UnmappedResource(resource.SourcePath, resource.TypeDirectory, UnmappedReason.NoPathIdAddressMatch));
				continue;
			}

			switch (method)
			{
				case MatchMethod.PathIdAddress:
					pathIdAddressMatches++;
					break;
				case MatchMethod.OriginalGuid:
				case MatchMethod.Guid:
					guidMatches++;
					break;
				case MatchMethod.PathId:
					pathIdMatches++;
					break;
				case MatchMethod.Name:
					nameMatches++;
					break;
			}

			if (!IsInScope(address, config))
			{
				unmappedResources.Add(new UnmappedResource(resource.SourcePath, resource.TypeDirectory, UnmappedReason.PathPrefixOutOfScope));
				continue;
			}

			string? targetPath = ComputeTargetPath(address, resource.TypeDirectory, assetsPath, fileSystem);
			if (targetPath is null)
			{
				unmappedResources.Add(new UnmappedResource(resource.SourcePath, resource.TypeDirectory, UnmappedReason.NotInTypeDirectory));
				continue;
			}

			if (!ValidateTypeMatch(resource.TypeDirectory, address, catalog))
			{
				unmappedResources.Add(new UnmappedResource(resource.SourcePath, resource.TypeDirectory, UnmappedReason.TypeMismatch));
				continue;
			}

			string targetMetaPath = targetPath + ".meta";

			moveItems.Add(new MoveItem
			{
				SourcePath = resource.SourcePath,
				SourceMetaPath = resource.MetaPath,
				TargetPath = targetPath,
				TargetMetaPath = targetMetaPath,
				Address = address,
				MatchMethod = method,
				ResourceType = resource.TypeDirectory,
			});
		}

		Logger.Info(LogCategory.Export, $"映射建立完成: {moveItems.Count} 个已映射, {unmappedResources.Count} 个未映射, 策略 D={pathIdAddressMatches} A={guidMatches} B={pathIdMatches} C={nameMatches}");

		return new MovePlan
		{
			MoveItems = moveItems,
			UnmappedResources = unmappedResources,
		};
	}

	public MovePlan BuildFromCatalog(
		CatalogData catalog,
		NaninovelConfig config,
		OriginalPathTable originalPathTable,
		PathIdNameMap pathIdMap,
		PathIdAddressMap pathIdAddressMap,
		string assetsPath,
		FileSystem fileSystem)
	{
		List<MoveItem> moveItems = new(catalog.Keys.Count);
		List<UnmappedResource> unmappedResources = new(128);

		int originalPathMatches = 0;
		int guidMatches = 0;
		int nameMatches = 0;
		int alreadyInPlace = 0;

		HashSet<string> processedSourcePaths = new(StringComparer.OrdinalIgnoreCase);
		int unmatchedLogged = 0;
		int matchedLogged = 0;

		foreach (CatalogKeyEntry key in catalog.Keys)
		{
			string address = key.Address;

			if (!IsInScope(address, config))
			{
				continue;
			}

			OriginalPathEntry? entry = null;
			originalPathTable.TryGetByResourcePath(address, out entry);

			string extension = entry is not null
				? GetExtensionForClassName(entry.ClassName)
				: GetExtensionForAddress(address, catalog);

			string targetPath = fileSystem.Path.Join(assetsPath, "Resources", address + extension);
			string targetMetaPath = targetPath + ".meta";

			if (CheckFileExistsByAddress(assetsPath, address, fileSystem))
			{
				alreadyInPlace++;
				continue;
			}

			string? sourcePath = null;
			MatchMethod method = MatchMethod.Name;

			if (entry is not null)
			{
				sourcePath = FindSourceFile(entry, assetsPath, extension, fileSystem);
				if (sourcePath is not null)
				{
					method = MatchMethod.OriginalPath;
					originalPathMatches++;
					if (matchedLogged < 5)
					{
						Logger.Info(LogCategory.Export, $"  反向映射 E 匹配: addr='{address}' -> source='{sourcePath}'");
						matchedLogged++;
					}
				}
				else if (unmatchedLogged < 30)
				{
					Logger.Info(LogCategory.Export, $"  反向映射 E 未找到文件: addr='{address}' Name='{entry.Name}' OrigDir='{entry.OriginalDirectory}' ext='{extension}' Class='{entry.ClassName}'");
					unmatchedLogged++;
				}
			}
			else if (unmatchedLogged < 30 && address.StartsWith("Backgrounds/", StringComparison.OrdinalIgnoreCase))
			{
				bool hasEntry = originalPathTable.TryGetByResourcePath(address, out _);
				Logger.Info(LogCategory.Export, $"  反向映射 E 无OriginalPath条目: addr='{address}' hasEntry={hasEntry}");
				unmatchedLogged++;
			}

			if (sourcePath is null)
			{
				sourcePath = FindSourceFileByGuid(key.Guid, assetsPath, extension, fileSystem);
				if (sourcePath is not null)
				{
					method = MatchMethod.OriginalGuid;
					guidMatches++;
				}
			}

			if (sourcePath is null)
			{
				sourcePath = FindSourceFileByName(address, assetsPath, extension, fileSystem);
				if (sourcePath is not null)
				{
					method = MatchMethod.Name;
					nameMatches++;
				}
			}

			if (sourcePath is null)
			{
				unmappedResources.Add(new UnmappedResource(address, "Unknown", UnmappedReason.NoPathIdAddressMatch));
				continue;
			}

			if (processedSourcePaths.Contains(sourcePath))
			{
				continue;
			}
			processedSourcePaths.Add(sourcePath);

			string sourceMetaPath = sourcePath + ".meta";
			string typeDirectory = GuessTypeDirectory(sourcePath, assetsPath, fileSystem);

			moveItems.Add(new MoveItem
			{
				SourcePath = sourcePath,
				SourceMetaPath = fileSystem.File.Exists(sourceMetaPath) ? sourceMetaPath : string.Empty,
				TargetPath = targetPath,
				TargetMetaPath = targetMetaPath,
				Address = address,
				MatchMethod = method,
				ResourceType = typeDirectory,
			});
		}

		Logger.Info(LogCategory.Export,
			$"反向映射完成: 已映射={moveItems.Count}, 未映射={unmappedResources.Count}, " +
			$"已在目标位置={alreadyInPlace}, " +
			$"策略 E={originalPathMatches} A={guidMatches} C={nameMatches}");

		Dictionary<string, int> unmappedByPrefix = new();
		foreach (UnmappedResource u in unmappedResources)
		{
			int slashIndex = u.SourcePath.IndexOf('/');
			string prefix = slashIndex >= 0 ? u.SourcePath[..slashIndex] : u.SourcePath;
			unmappedByPrefix[prefix] = unmappedByPrefix.GetValueOrDefault(prefix) + 1;
		}
		foreach (KeyValuePair<string, int> kvp in unmappedByPrefix.OrderByDescending(x => x.Value))
		{
			Logger.Info(LogCategory.Export, $"  未映射前缀: {kvp.Key} = {kvp.Value}");
		}

		return new MovePlan
		{
			MoveItems = moveItems,
			UnmappedResources = unmappedResources,
		};
	}

	private static string GetExtensionForClassName(string className)
	{
		return className switch
		{
			"Texture2D" => ".png",
			"Sprite" => ".asset",
			"TextAsset" => ".txt",
			"AudioClip" => ".wav",
			"VideoClip" => ".asset",
			"GameObject" => ".prefab",
			"Material" => ".mat",
			"AnimationClip" => ".anim",
			"AnimatorController" => ".controller",
			"Font" => ".ttf",
			"RenderTexture" => ".renderTexture",
			_ => ".asset",
		};
	}

	private static bool CheckFileExistsByAddress(string assetsPath, string address, FileSystem fileSystem)
	{
		int lastSlash = address.LastIndexOf('/');
		string dirPart = lastSlash >= 0 ? address[..lastSlash] : string.Empty;
		string namePart = lastSlash >= 0 ? address[(lastSlash + 1)..] : address;

		string dirPath = fileSystem.Path.Join(assetsPath, "Resources", dirPart);
		if (!fileSystem.Directory.Exists(dirPath))
		{
			return false;
		}

		foreach (string file in fileSystem.Directory.EnumerateFiles(dirPath, namePart + "*"))
		{
			string fileName = fileSystem.Path.GetFileName(file);
			if (fileName.StartsWith(namePart, StringComparison.OrdinalIgnoreCase) && !fileName.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}
		return false;
	}

	private static string GetExtensionForAddress(string address, CatalogData catalog)
	{
		ResourceTypeEntry? typeEntry = catalog.GetAddressType(address);
		if (typeEntry is null)
		{
			return ".png";
		}

		return typeEntry.ClassName switch
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
			"Font" => ".asset",
			"RenderTexture" => ".asset",
			_ => ".png",
		};
	}

	private static string? FindSourceFile(OriginalPathEntry entry, string assetsPath, string? extension, FileSystem fileSystem)
	{
		string name = entry.Name;
		if (string.IsNullOrEmpty(name))
		{
			name = entry.OriginalName ?? entry.ClassName;
		}
		name = SanitizeFileName(name);

		if (entry.OriginalDirectory is not null)
		{
			string dir = entry.OriginalDirectory.Replace('\\', '/');
			string relativeDir = dir.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)
				? dir["Assets/".Length..]
				: dir;
			string searchDir = fileSystem.Path.Join(assetsPath, relativeDir);

			string? found = FindFileByNameInDir(fileSystem, searchDir, name);
			if (found is not null)
			{
				return found;
			}

			if (entry.OriginalName is not null)
			{
				string origName = SanitizeFileName(entry.OriginalName);
				found = FindFileByNameInDir(fileSystem, searchDir, origName);
				if (found is not null)
				{
					return found;
				}
			}
		}

		foreach (string typeDir in TypeDirectories)
		{
			if (!TypeToExtension.TryGetValue(typeDir, out string? expectedExt) || !expectedExt.Equals(extension, StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			string typeDirPath = fileSystem.Path.Join(assetsPath, typeDir);
			if (!fileSystem.Directory.Exists(typeDirPath))
			{
				continue;
			}

			string? found = FindFileByNameInDir(fileSystem, typeDirPath, name);
			if (found is not null)
			{
				return found;
			}
		}

		foreach (string typeDir in TypeDirectories)
		{
			string typeDirPath = fileSystem.Path.Join(assetsPath, typeDir);
			if (!fileSystem.Directory.Exists(typeDirPath))
			{
				continue;
			}

			string? found = FindFileByNameInDir(fileSystem, typeDirPath, name);
			if (found is not null)
			{
				return found;
			}
		}

		return null;
	}

	private static string? FindFileByNameInDir(FileSystem fileSystem, string dirPath, string name)
	{
		if (!fileSystem.Directory.Exists(dirPath))
		{
			return null;
		}

		string exactPath = fileSystem.Path.Join(dirPath, name);
		foreach (string file in fileSystem.Directory.EnumerateFiles(dirPath, name + "*"))
		{
			string fileName = fileSystem.Path.GetFileName(file);
			if (fileName.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}
			if (fileName.Equals(name, StringComparison.OrdinalIgnoreCase) || fileName.StartsWith(name + ".", StringComparison.OrdinalIgnoreCase) || fileName.StartsWith(name + "_", StringComparison.OrdinalIgnoreCase))
			{
				return file;
			}
		}
		return null;
	}

	private static string? FindWithCollisionSuffix(FileSystem fileSystem, string basePath, string name, string extension)
	{
		string? dir = fileSystem.Path.GetDirectoryName(basePath);
		if (dir is null || !fileSystem.Directory.Exists(dir))
		{
			return null;
		}

		foreach (string file in fileSystem.Directory.EnumerateFiles(dir, name + "*" + extension))
		{
			string fileName = fileSystem.Path.GetFileName(file);
			if (fileName.StartsWith(name + "_", StringComparison.OrdinalIgnoreCase) && fileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
			{
				return file;
			}
		}

		return null;
	}

	private static string? FindSourceFileByGuid(string guid, string assetsPath, string? extension, FileSystem fileSystem)
	{
		if (string.IsNullOrEmpty(guid))
		{
			return null;
		}

		foreach (string typeDir in TypeDirectories)
		{
			string typeDirPath = fileSystem.Path.Join(assetsPath, typeDir);
			if (!fileSystem.Directory.Exists(typeDirPath))
			{
				continue;
			}

			foreach (string filePath in fileSystem.Directory.EnumerateFiles(typeDirPath, "*" + extension))
			{
				if (filePath.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}

				string metaPath = filePath + ".meta";
				string extractedGuid = ExtractGuid(metaPath, fileSystem);
				if (extractedGuid.Equals(guid, StringComparison.OrdinalIgnoreCase))
				{
					return filePath;
				}
			}
		}

		return null;
	}

	private static string? FindSourceFileByName(string address, string assetsPath, string? extension, FileSystem fileSystem)
	{
		int lastSlash = address.LastIndexOf('/');
		string lastSegment = lastSlash >= 0 ? address[(lastSlash + 1)..] : address;
		lastSegment = SanitizeFileName(lastSegment);

		foreach (string typeDir in TypeDirectories)
		{
			string typeDirPath = fileSystem.Path.Join(assetsPath, typeDir);
			if (!fileSystem.Directory.Exists(typeDirPath))
			{
				continue;
			}

			string filePath = fileSystem.Path.Join(typeDirPath, lastSegment + extension);
			if (fileSystem.File.Exists(filePath))
			{
				return filePath;
			}
		}

		return null;
	}

	private static string SanitizeFileName(string name)
	{
		return name.Replace('/', '_').Replace('\\', '_').Trim();
	}

	private static string GuessTypeDirectory(string sourcePath, string assetsPath, FileSystem fileSystem)
	{
		string? dir = fileSystem.Path.GetDirectoryName(sourcePath);
		if (dir is not null)
		{
			string dirName = fileSystem.Path.GetFileName(dir);
			foreach (string typeDir in TypeDirectories)
			{
				if (dirName.Equals(typeDir, StringComparison.OrdinalIgnoreCase))
				{
					return typeDir;
				}
			}
		}
		return "Texture2D";
	}

	private static IEnumerable<ExportedResourceFile> EnumerateExportedResources(string assetsPath, FileSystem fileSystem)
	{
		foreach (string typeDir in TypeDirectories)
		{
			string typeDirPath = fileSystem.Path.Join(assetsPath, typeDir);
			if (!fileSystem.Directory.Exists(typeDirPath))
			{
				continue;
			}

			foreach (string filePath in fileSystem.Directory.EnumerateFiles(typeDirPath))
			{
				string fileName = fileSystem.Path.GetFileName(filePath);
				if (fileName.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}

				string metaPath = filePath + ".meta";
				string guid = ExtractGuid(metaPath, fileSystem);

				yield return new ExportedResourceFile
				{
					SourcePath = filePath,
					FileName = fileName,
					Extension = fileSystem.Path.GetExtension(filePath),
					TypeDirectory = typeDir,
					MetaPath = fileSystem.File.Exists(metaPath) ? metaPath : string.Empty,
					Guid = guid,
				};
			}
		}
	}

	private static string ExtractGuid(string metaPath, FileSystem fileSystem)
	{
		if (!fileSystem.File.Exists(metaPath))
		{
			return string.Empty;
		}

		try
		{
			string content = fileSystem.File.ReadAllText(metaPath);
			Match match = GuidRegex.Match(content);
			return match.Success ? match.Groups[1].Value.ToLowerInvariant() : string.Empty;
		}
		catch
		{
			return string.Empty;
		}
	}

	private static string? MatchByGuid(string resourceGuid, CatalogData catalog)
	{
		if (string.IsNullOrEmpty(resourceGuid))
		{
			return null;
		}

		return catalog.AddressByGuid.TryGetValue(resourceGuid, out string? address) ? address : null;
	}

	private static string? MatchByPathIdAddress(string fileName, PathIdAddressMap pathIdAddressMap)
	{
		string nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);

		if (pathIdAddressMap.TryGetAddressByName(nameWithoutExt, out string? address))
		{
			return address;
		}

		int underscoreIndex = nameWithoutExt.LastIndexOf('_');
		if (underscoreIndex > 0)
		{
			string baseName = nameWithoutExt[..underscoreIndex];
			if (pathIdAddressMap.TryGetAddressByName(baseName, out address))
			{
				return address;
			}
		}

		return null;
	}

	private static string? MatchByPathId(string fileName, PathIdNameMap pathIdMap, IReadOnlyDictionary<string, IReadOnlyList<string>> addressByLastSegment)
	{
		string nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
		if (!long.TryParse(nameWithoutExt, NumberStyles.Integer, CultureInfo.InvariantCulture, out long pathId))
		{
			return null;
		}

		if (!pathIdMap.ByPathId.TryGetValue(pathId, out PathIdNameEntry? entry) || entry is null)
		{
			return null;
		}

		if (string.IsNullOrEmpty(entry.Name))
		{
			return null;
		}

		return FindAddressByLastSegment(entry.Name, addressByLastSegment);
	}

	private static string? MatchByName(string fileName, string extension, PathIdNameMap pathIdMap, IReadOnlyDictionary<string, IReadOnlyList<string>> addressByLastSegment)
	{
		string nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);

		if (long.TryParse(nameWithoutExt, NumberStyles.Integer, CultureInfo.InvariantCulture, out long pathId)
			&& pathIdMap.ByPathId.TryGetValue(pathId, out PathIdNameEntry? entry) && entry is not null
			&& !string.IsNullOrEmpty(entry.Name))
		{
			return FindAddressByLastSegment(entry.Name, addressByLastSegment);
		}

		return FindAddressByLastSegment(nameWithoutExt, addressByLastSegment);
	}

	private static string? FindAddressByLastSegment(string name, IReadOnlyDictionary<string, IReadOnlyList<string>> addressByLastSegment)
	{
		if (string.IsNullOrEmpty(name))
		{
			return null;
		}

		if (!addressByLastSegment.TryGetValue(name, out IReadOnlyList<string>? addresses) || addresses.Count == 0)
		{
			return null;
		}

		if (addresses.Count > 1)
		{
			Logger.Warning(LogCategory.Export, $"名称匹配到多个地址: {name}, 使用第一个: {addresses[0]}");
		}

		return addresses[0];
	}

	private static IReadOnlyDictionary<string, IReadOnlyList<string>> BuildAddressByLastSegment(CatalogData catalog)
	{
		Dictionary<string, List<string>> result = new(catalog.Keys.Count, StringComparer.OrdinalIgnoreCase);
		foreach (CatalogKeyEntry key in catalog.Keys)
		{
			int slashIndex = key.Address.LastIndexOf('/');
			string lastSegment = slashIndex >= 0 ? key.Address[(slashIndex + 1)..] : key.Address;
			if (string.IsNullOrEmpty(lastSegment))
			{
				continue;
			}

			if (!result.TryGetValue(lastSegment, out List<string>? list))
			{
				list = [];
				result[lastSegment] = list;
			}
			list.Add(key.Address);
		}

		return result.ToDictionary(kvp => kvp.Key, kvp => (IReadOnlyList<string>)kvp.Value, StringComparer.OrdinalIgnoreCase);
	}

	private static bool IsInScope(string address, NaninovelConfig config)
	{
		int slashIndex = address.IndexOf('/');
		string prefix = slashIndex >= 0 ? address[..slashIndex] : address;
		return config.IsPathPrefixInScope(prefix);
	}

	private static string? ComputeTargetPath(string address, string resourceType, string assetsPath, FileSystem fileSystem)
	{
		if (!TypeToExtension.TryGetValue(resourceType, out string? extension))
		{
			Logger.Warning(LogCategory.Export, $"未知资源类型目录: {resourceType}, 地址: {address}");
			return null;
		}

		string targetPath = fileSystem.Path.Join(assetsPath, "Resources", address + extension);

		string fullTargetPath = fileSystem.Path.GetFullPath(targetPath);
		string fullResourcesRoot = fileSystem.Path.GetFullPath(fileSystem.Path.Join(assetsPath, "Resources")) + System.IO.Path.DirectorySeparatorChar;
		if (!fullTargetPath.StartsWith(fullResourcesRoot, StringComparison.OrdinalIgnoreCase))
		{
			Logger.Error(LogCategory.Export, $"目标路径越界: {targetPath}");
			return null;
		}

		if (targetPath.Contains(".."))
		{
			Logger.Error(LogCategory.Export, $"目标路径包含 '..': {targetPath}");
			return null;
		}

		return targetPath;
	}

	private static bool ValidateTypeMatch(string typeDirectory, string address, CatalogData catalog)
	{
		ResourceTypeEntry? typeEntry = catalog.GetAddressType(address);
		if (typeEntry is null)
		{
			return true;
		}

		return typeEntry.ClassName switch
		{
			"Texture2D" => typeDirectory is "Texture2D",
			"Sprite" => typeDirectory is "Sprite",
			"TextAsset" => typeDirectory is "TextAsset",
			"AudioClip" => typeDirectory is "AudioClip",
			"VideoClip" => typeDirectory is "VideoClip",
			"GameObject" => typeDirectory is "GameObject",
			"Material" => typeDirectory is "Material",
			"AnimationClip" => typeDirectory is "AnimationClip",
			"AnimatorController" => typeDirectory is "AnimatorController",
			"Font" => typeDirectory is "Font",
			"RenderTexture" => typeDirectory is "RenderTexture",
			_ => true,
		};
	}

	private sealed class ExportedResourceFile
	{
		public required string SourcePath { get; init; }
		public required string FileName { get; init; }
		public required string Extension { get; init; }
		public required string TypeDirectory { get; init; }
		public required string MetaPath { get; init; }
		public required string Guid { get; init; }
	}
}