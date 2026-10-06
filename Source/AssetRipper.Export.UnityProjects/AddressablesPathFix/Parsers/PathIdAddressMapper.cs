using AssetRipper.Export.UnityProjects.AddressablesPathFix.Models;
using AssetRipper.Import.Logging;

namespace AssetRipper.Export.UnityProjects.AddressablesPathFix.Parsers;

public sealed class PathIdAddressMapper
{
	public PathIdAddressMap Build(CatalogData catalog, IReadOnlyList<BundleAssetEntry> bundleAssets)
	{
		if (bundleAssets.Count == 0)
		{
			Logger.Warning(LogCategory.Export, "PathIdAddressMapper: bundle 资源列表为空，返回空映射");
			return new PathIdAddressMap();
		}

		Dictionary<string, string> addressByLastSegment = new(catalog.Keys.Count, StringComparer.OrdinalIgnoreCase);
		Dictionary<string, List<string>> addressByAnySegment = new(catalog.Keys.Count, StringComparer.OrdinalIgnoreCase);

		foreach (CatalogKeyEntry key in catalog.Keys)
		{
			string addr = key.Address;
			int lastSlash = addr.LastIndexOf('/');
			string lastSeg = lastSlash >= 0 ? addr[(lastSlash + 1)..] : addr;
			if (!string.IsNullOrEmpty(lastSeg))
			{
				addressByLastSegment.TryAdd(lastSeg, addr);
			}

			string[] segments = addr.Split('/');
			foreach (string seg in segments)
			{
				if (!string.IsNullOrEmpty(seg) && !addressByAnySegment.TryGetValue(seg, out List<string>? list))
				{
					list = [];
					addressByAnySegment[seg] = list;
				}
				if (!string.IsNullOrEmpty(seg))
				{
					addressByAnySegment[seg].Add(addr);
				}
			}
		}

		Dictionary<long, PathIdAddressEntry> byPathId = new(bundleAssets.Count);
		Dictionary<string, PathIdAddressEntry> byName = new(bundleAssets.Count, StringComparer.OrdinalIgnoreCase);
		int matched = 0;
		int unmatched = 0;

		foreach (BundleAssetEntry bundleAsset in bundleAssets)
		{
			string? address = TryMatchAddress(bundleAsset, catalog, addressByLastSegment, addressByAnySegment);

			if (address is not null)
			{
				string resourceType = DetermineResourceType(address, catalog, bundleAsset);
				PathIdAddressEntry entry = new PathIdAddressEntry
				{
					PathID = bundleAsset.PathID,
					Address = address,
					ResourceType = resourceType,
					BundleName = bundleAsset.BundleName,
				};
				byPathId[bundleAsset.PathID] = entry;
				if (!string.IsNullOrEmpty(bundleAsset.Name))
				{
					byName.TryAdd(bundleAsset.Name, entry);
				}
				matched++;
			}
			else
			{
				unmatched++;
				if (unmatched <= 50)
				{
					Logger.Warning(LogCategory.Export, $"PathIdAddressMapper: PathID={bundleAsset.PathID} Name='{bundleAsset.Name}' Type={bundleAsset.TypeName} Bundle={bundleAsset.BundleName} 无匹配地址");
				}
			}
		}

		int catalogAddressCount = catalog.Keys.Count;
		double coverage = catalogAddressCount > 0 ? (double)byPathId.Count / catalogAddressCount * 100.0 : 0.0;
		Logger.Info(LogCategory.Export, $"PathIdAddressMapper: 映射建立完成 {matched} 个匹配, {unmatched} 个未匹配, 覆盖率 {coverage:F1}% ({byPathId.Count}/{catalogAddressCount}), ByName={byName.Count}");

		if (coverage < 100.0 && unmatched > 0)
		{
			Logger.Warning(LogCategory.Export, $"PathIdAddressMapper: 覆盖率未达 100%, {unmatched} 个 PathID 无对应地址");
		}

		return new PathIdAddressMap
		{
			ByPathId = byPathId,
			ByName = byName,
		};
	}

	private static string? TryMatchAddress(
		BundleAssetEntry bundleAsset,
		CatalogData catalog,
		Dictionary<string, string> addressByLastSegment,
		Dictionary<string, List<string>> addressByAnySegment)
	{
		if (string.IsNullOrEmpty(bundleAsset.Name))
		{
			return null;
		}

		if (addressByLastSegment.TryGetValue(bundleAsset.Name, out string? addr))
		{
			return addr;
		}

		if (addressByAnySegment.TryGetValue(bundleAsset.Name, out List<string>? addrs) && addrs.Count == 1)
		{
			return addrs[0];
		}

		foreach (CatalogKeyEntry key in catalog.Keys)
		{
			if (key.Address.Equals(bundleAsset.Name, StringComparison.OrdinalIgnoreCase))
			{
				return key.Address;
			}
		}

		int slashIndex = bundleAsset.Name.IndexOf('/');
		if (slashIndex >= 0)
		{
			string lastPart = bundleAsset.Name[(slashIndex + 1)..];
			if (addressByLastSegment.TryGetValue(lastPart, out addr))
			{
				return addr;
			}
		}

		return null;
	}

	private static string DetermineResourceType(string address, CatalogData catalog, BundleAssetEntry bundleAsset)
	{
		ResourceTypeEntry? typeEntry = catalog.GetAddressType(address);
		if (typeEntry is not null)
		{
			return typeEntry.ClassName;
		}
		return bundleAsset.TypeName;
	}
}