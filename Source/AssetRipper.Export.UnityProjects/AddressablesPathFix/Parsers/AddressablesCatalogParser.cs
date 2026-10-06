using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AssetRipper.Export.Configuration;
using AssetRipper.Export.UnityProjects.AddressablesPathFix.Models;
using AssetRipper.Import.Logging;

namespace AssetRipper.Export.UnityProjects.AddressablesPathFix.Parsers;

public sealed class AddressablesCatalogParser
{
	private const string CatalogRelativePath = "StreamingAssets/aa/catalog.json";
	private static readonly Regex GuidRegex = new(@"[0-9a-fA-F]{32}", RegexOptions.Compiled);

	public static string? LocateCatalog(FullConfiguration settings, FileSystem fileSystem)
	{
		Logger.Info(LogCategory.Export, $"LocateCatalog: AssetsPath='{settings.AssetsPath}', SourceDataPath='{settings.SourceDataPath}'");

		string exportPath = fileSystem.Path.Join(settings.AssetsPath, CatalogRelativePath);
		Logger.Info(LogCategory.Export, $"LocateCatalog: checking export path '{exportPath}', exists={fileSystem.File.Exists(exportPath)}");
		if (fileSystem.File.Exists(exportPath))
		{
			Logger.Info(LogCategory.Export, $"Found Addressables catalog at export project: {exportPath}");
			return exportPath;
		}

		if (!string.IsNullOrEmpty(settings.SourceDataPath))
		{
			string sourcePath = fileSystem.Path.Join(settings.SourceDataPath, CatalogRelativePath);
			Logger.Info(LogCategory.Export, $"LocateCatalog: checking source fallback path '{sourcePath}', exists={fileSystem.File.Exists(sourcePath)}");
			if (fileSystem.File.Exists(sourcePath))
			{
				Logger.Info(LogCategory.Export, $"Found Addressables catalog at source data (fallback): {sourcePath}");
				return sourcePath;
			}

			string? parentDir = Path.GetDirectoryName(settings.SourceDataPath);
			if (parentDir is not null)
			{
				string folderName = Path.GetFileName(settings.SourceDataPath);
				if (folderName.EndsWith("_Data", StringComparison.OrdinalIgnoreCase))
				{
					string dataCatalogPath = Path.Join(parentDir, folderName, CatalogRelativePath);
					Logger.Info(LogCategory.Export, $"LocateCatalog: checking _Data fallback path '{dataCatalogPath}', exists={fileSystem.File.Exists(dataCatalogPath)}");
					if (fileSystem.File.Exists(dataCatalogPath))
					{
						Logger.Info(LogCategory.Export, $"Found Addressables catalog at _Data fallback: {dataCatalogPath}");
						return dataCatalogPath;
					}
				}
			}

			string? searchRoot = settings.SourceDataPath;
			if (Directory.Exists(searchRoot))
			{
				try
				{
					string? found = Directory.EnumerateFiles(searchRoot, "catalog.json", new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 5 })
						.FirstOrDefault(p => p.Contains("StreamingAssets", StringComparison.OrdinalIgnoreCase) && p.Contains("aa", StringComparison.OrdinalIgnoreCase));
					if (found is not null)
					{
						Logger.Info(LogCategory.Export, $"Found Addressables catalog via recursive search: {found}");
						return found;
					}
				}
				catch (Exception ex)
				{
					Logger.Warning(LogCategory.Export, $"LocateCatalog recursive search failed: {ex.Message}");
				}
			}
		}
		else
		{
			Logger.Warning(LogCategory.Export, "LocateCatalog: SourceDataPath is empty or null");
		}

		Logger.Error(LogCategory.Export, "LocateCatalog: catalog.json not found in any location");
		return null;
	}

	public CatalogData Parse(string catalogPath, FileSystem fileSystem)
	{
		string jsonText = fileSystem.File.ReadAllText(catalogPath);
		using JsonDocument doc = JsonDocument.Parse(jsonText);
		JsonElement root = doc.RootElement;

		if (!root.TryGetProperty("m_InternalIds", out JsonElement internalIdsElement))
		{
			throw new CatalogParseException("Missing m_InternalIds field", "m_InternalIds");
		}

		IReadOnlyList<string> internalIds = ParseInternalIds(internalIdsElement);
		IReadOnlyList<ResourceTypeEntry> resourceTypes = ParseResourceTypes(root);

		Dictionary<string, string> addressByGuid = new();
		HashSet<string> allAddresses = new(StringComparer.OrdinalIgnoreCase);
		List<CatalogKeyEntry> keys = new();

		if (root.TryGetProperty("m_KeyDataString", out JsonElement keyDataElement))
		{
			try
			{
				string base64 = keyDataElement.GetString()!;
				byte[] data = Convert.FromBase64String(base64);
				ExtractGuidAddressPairsPrecise(data, addressByGuid, allAddresses, keys);
			}
			catch (Exception ex)
			{
				Logger.Warning(LogCategory.Export, $"精确解析 m_KeyDataString 失败，回退正则扫描: {ex.Message}");
				try
				{
					string base64 = keyDataElement.GetString()!;
					byte[] data = Convert.FromBase64String(base64);
					ExtractGuidAddressPairsRegex(data, addressByGuid, allAddresses, keys);
				}
				catch (Exception ex2)
				{
					Logger.Warning(LogCategory.Export, $"GUID 提取完全失败: {ex2.Message}");
				}
			}
		}

		foreach (string id in internalIds)
		{
			if (!string.IsNullOrEmpty(id) && !id.StartsWith('{'))
			{
				allAddresses.Add(id);
			}
		}

		if (keys.Count == 0)
		{
			foreach (string addr in allAddresses)
			{
				keys.Add(new CatalogKeyEntry(addr, string.Empty));
			}
		}

		Dictionary<string, List<string>> addressesByPrefix = new(StringComparer.OrdinalIgnoreCase);
		foreach (string addr in allAddresses)
		{
			string prefix = GetFirstSegment(addr);
			if (!addressesByPrefix.TryGetValue(prefix, out List<string>? list))
			{
				list = [];
				addressesByPrefix[prefix] = list;
			}
			list.Add(addr);
		}

		IReadOnlyDictionary<string, IReadOnlyList<string>> readOnlyPrefixes = addressesByPrefix
			.ToDictionary(kvp => kvp.Key, kvp => (IReadOnlyList<string>)kvp.Value, StringComparer.OrdinalIgnoreCase);

		Dictionary<int, int>? entryData = null;
		Dictionary<int, int>? entryTypeData = null;
		if (root.TryGetProperty("m_EntryDataString", out JsonElement entryDataElement))
		{
			try
			{
				(entryData, entryTypeData) = ParseEntryDataString(entryDataElement.GetString()!);
				Logger.Info(LogCategory.Export, $"m_EntryDataString 解析完成: {entryData.Count} 个条目");
			}
			catch (Exception ex)
			{
				Logger.Warning(LogCategory.Export, $"m_EntryDataString 解析失败: {ex.Message}");
			}
		}

		Dictionary<string, int>? keyIndexByAddress = BuildKeyIndexByAddress(keys);

		Logger.Info(LogCategory.Export, $"catalog 解析完成: {keys.Count} 个地址键, {internalIds.Count} 个内部标识, {resourceTypes.Count} 个资源类型, {addressByGuid.Count} 个 GUID 映射");

		return new CatalogData
		{
			Keys = keys,
			InternalIds = internalIds,
			ResourceTypes = resourceTypes,
			AddressByGuid = addressByGuid,
			AddressesByPrefix = readOnlyPrefixes,
			EntryData = entryData,
			EntryTypeData = entryTypeData,
			KeyIndexByAddress = keyIndexByAddress,
		};
	}

	private static void ExtractGuidAddressPairsPrecise(byte[] data, Dictionary<string, string> addressByGuid, HashSet<string> allAddresses, List<CatalogKeyEntry> keys)
	{
		int pos = 0;
		int count = 0;
		string? lastAddress = null;

		while (pos < data.Length)
		{
			int keyType = data[pos];
			pos++;

			if (keyType == 0)
			{
				if (pos + 4 > data.Length)
				{
					break;
				}
				int strLen = BitConverter.ToInt32(data, pos);
				pos += 4;
				if (strLen <= 0 || strLen > 1000 || pos + strLen > data.Length)
				{
					break;
				}
				string address = Encoding.UTF8.GetString(data, pos, strLen);
				pos += strLen;
				if (IsValidAddress(address))
				{
					lastAddress = address;
					allAddresses.Add(address);
					count++;
				}
			}
			else if (keyType == 1)
			{
				if (pos + 16 > data.Length)
				{
					break;
				}
				byte[] guidBytes = new byte[16];
				Array.Copy(data, pos, guidBytes, 0, 16);
				pos += 16;
				string guid = new Guid(guidBytes).ToString("N").ToLowerInvariant();
				if (lastAddress is not null && IsValidGuid(guid))
				{
					addressByGuid.TryAdd(guid, lastAddress);
					keys.Add(new CatalogKeyEntry(lastAddress, guid));
				}
			}
			else if (keyType == 2)
			{
				if (pos + 32 > data.Length)
				{
					break;
				}
				string guid = Encoding.ASCII.GetString(data, pos, 32).ToLowerInvariant();
				pos += 32;
				if (lastAddress is not null && IsValidGuid(guid))
				{
					addressByGuid.TryAdd(guid, lastAddress);
					keys.Add(new CatalogKeyEntry(lastAddress, guid));
				}
			}
			else
			{
				break;
			}
		}

		if (count < 100)
		{
			string hexPreview = pos > 0 ? BitConverter.ToString(data, 0, Math.Min(64, data.Length)) : "empty";
			Logger.Warning(LogCategory.Export, $"精确解析仅提取 {count} 个键，前64字节: {hexPreview}");
			throw new FormatException($"精确解析仅提取 {count} 个键，格式可能不匹配");
		}

		Logger.Info(LogCategory.Export, $"m_KeyDataString 精确解析提取 {count} 个 GUID-地址对");
	}

	private static void ExtractGuidAddressPairsRegex(byte[] data, Dictionary<string, string> addressByGuid, HashSet<string> allAddresses, List<CatalogKeyEntry> keys)
	{
		string text = Encoding.ASCII.GetString(data);
		MatchCollection matches = GuidRegex.Matches(text);

		int extracted = 0;
		foreach (Match match in matches)
		{
			string guid = match.Value.ToLowerInvariant();
			int guidStart = match.Index;

			int searchStart = guidStart - 5;
			if (searchStart < 0)
			{
				continue;
			}

			int addrEnd = -1;
			for (int back = searchStart; back >= Math.Max(0, guidStart - 200); back--)
			{
				if (data[back] == 0)
				{
					addrEnd = back;
					break;
				}
			}

			if (addrEnd < 0)
			{
				continue;
			}

			int addrStart = addrEnd - 1;
			while (addrStart >= 0 && data[addrStart] >= 32 && data[addrStart] < 127)
			{
				addrStart--;
			}
			addrStart++;

			int addrLen = addrEnd - addrStart;
			if (addrLen < 2 || addrLen > 100)
			{
				continue;
			}

			string address = Encoding.ASCII.GetString(data, addrStart, addrLen).Trim();
			if (string.IsNullOrEmpty(address) || !IsValidAddress(address))
			{
				continue;
			}

			if (addressByGuid.TryAdd(guid, address))
			{
				allAddresses.Add(address);
				keys.Add(new CatalogKeyEntry(address, guid));
				extracted++;
			}
		}

		Logger.Info(LogCategory.Export, $"从 m_KeyDataString 正则回溯提取到 {extracted} 个 GUID-地址对");
	}

	private static (Dictionary<int, int> entryData, Dictionary<int, int> entryTypeData) ParseEntryDataString(string base64)
	{
		byte[] data = Convert.FromBase64String(base64);
		Dictionary<int, int> entryData = new();
		Dictionary<int, int> entryTypeData = new();

		const int entrySize = 16;
		int entryCount = data.Length / entrySize;

		for (int i = 0; i < entryCount; i++)
		{
			int offset = i * entrySize;
			int typeIndex = BitConverter.ToInt32(data, offset);
			int internalIdIndex = BitConverter.ToInt32(data, offset + 4);
			int dependencyIndex = BitConverter.ToInt32(data, offset + 8);
			int dataIndex = BitConverter.ToInt32(data, offset + 12);

			if (internalIdIndex >= 0)
			{
				entryData[i] = internalIdIndex;
			}
			if (typeIndex >= 0)
			{
				entryTypeData[i] = typeIndex;
			}
		}

		return (entryData, entryTypeData);
	}

	private static Dictionary<string, int>? BuildKeyIndexByAddress(IReadOnlyList<CatalogKeyEntry> keys)
	{
		if (keys.Count == 0)
		{
			return null;
		}

		Dictionary<string, int> result = new(keys.Count, StringComparer.OrdinalIgnoreCase);
		for (int i = 0; i < keys.Count; i++)
		{
			result.TryAdd(keys[i].Address, i);
		}
		return result;
	}

	private static bool IsValidGuid(string guid)
	{
		if (guid.Length != 32)
		{
			return false;
		}
		foreach (char c in guid)
		{
			if (!char.IsAsciiHexDigit(c))
			{
				return false;
			}
		}
		return true;
	}

	private static bool IsValidAddress(string address)
	{
		if (address.Length == 32 && GuidRegex.IsMatch(address))
		{
			return false;
		}

		foreach (char c in address)
		{
			if (!char.IsLetterOrDigit(c) && c != '/' && c != '_' && c != '-' && c != '.' && c != ' ')
			{
				return false;
			}
		}

		return true;
	}

	private static IReadOnlyList<string> ParseInternalIds(JsonElement internalIdsElement)
	{
		List<string> result = new(internalIdsElement.GetArrayLength());
		foreach (JsonElement item in internalIdsElement.EnumerateArray())
		{
			result.Add(item.GetString()!);
		}
		return result;
	}

	private static IReadOnlyList<ResourceTypeEntry> ParseResourceTypes(JsonElement root)
	{
		if (!root.TryGetProperty("m_resourceTypes", out JsonElement resourceTypesElement))
		{
			return [];
		}

		List<ResourceTypeEntry> result = new(resourceTypesElement.GetArrayLength());
		foreach (JsonElement item in resourceTypesElement.EnumerateArray())
		{
			string assemblyName = item.TryGetProperty("m_AssemblyName", out JsonElement asmElem) ? asmElem.GetString()! : string.Empty;
			string className = item.TryGetProperty("m_ClassName", out JsonElement classElem) ? classElem.GetString()! : string.Empty;
			result.Add(new ResourceTypeEntry(assemblyName, className));
		}
		return result;
	}

	private static string GetFirstSegment(string address)
	{
		int slashIndex = address.IndexOf('/');
		return slashIndex >= 0 ? address[..slashIndex] : address;
	}
}
