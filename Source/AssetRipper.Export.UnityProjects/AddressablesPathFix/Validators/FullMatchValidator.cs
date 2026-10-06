using System.IO;
using AssetRipper.Export.UnityProjects.AddressablesPathFix.Models;
using AssetRipper.Import.Logging;

namespace AssetRipper.Export.UnityProjects.AddressablesPathFix.Validators;

public sealed class FullMatchValidator
{
	private static readonly string[] CommonExtensions = [".jpg", ".png", ".jpeg", ".tga", ".psd", ".exr", ".tif", ".tiff", ".mp4", ".webm", ".ogv", ".mov", ".avi", ".ogg", ".wav", ".mp3", ".aac", ".m4a", ".nani", ".txt", ".bytes", ".asset", ".json", ".xml", ".csv", ".yaml", ".yml", ".bmp", ".gif", ".ico", ".wav", ".aiff", ".aif"];

	public FullMatchReport Validate(MovePlan plan, MoveResult result, CatalogData catalog, NaninovelConfig config, string assetsPath, IReadOnlySet<string>? staleAddresses = null)
	{
		HashSet<string> staleSet = staleAddresses is not null ? new(staleAddresses, StringComparer.OrdinalIgnoreCase) : [];

		HashSet<string> matchedAddresses = new(result.Successes.Count, StringComparer.OrdinalIgnoreCase);
		foreach (MoveSuccess success in result.Successes)
		{
			matchedAddresses.Add(success.Item.Address);
		}

		HashSet<string> naninovelAddresses = new(StringComparer.OrdinalIgnoreCase);
		foreach (CatalogKeyEntry key in catalog.Keys)
		{
			if (IsInScope(key.Address, config) && !staleSet.Contains(key.Address))
			{
				naninovelAddresses.Add(key.Address);
			}
		}

		HashSet<string> fileExistsMatched = new(StringComparer.OrdinalIgnoreCase);
		string resourcesBase = Path.Join(assetsPath, "Resources");
		foreach (string addr in naninovelAddresses)
		{
			if (matchedAddresses.Contains(addr))
			{
				continue;
			}

			string expectedPathNoExt = Path.Join(resourcesBase, addr.Replace('/', Path.DirectorySeparatorChar));
			string? dir = Path.GetDirectoryName(expectedPathNoExt);
			string fileNameNoExt = Path.GetFileNameWithoutExtension(expectedPathNoExt);
			if (dir is null || !Directory.Exists(dir))
			{
				continue;
			}

			foreach (string ext in CommonExtensions)
			{
				string candidate = Path.Combine(dir, fileNameNoExt + ext);
				if (File.Exists(candidate))
				{
					fileExistsMatched.Add(addr);
					break;
				}
			}

			if (!fileExistsMatched.Contains(addr))
			{
				string prefix = Path.Combine(dir, fileNameNoExt + ".");
				try
				{
					string? found = Directory.EnumerateFiles(dir, fileNameNoExt + ".*", SearchOption.TopDirectoryOnly)
						.FirstOrDefault(f => !f.EndsWith(".meta", StringComparison.OrdinalIgnoreCase));
					if (found is not null)
					{
						fileExistsMatched.Add(addr);
					}
				}
				catch
				{
				}
			}
		}

		matchedAddresses.UnionWith(fileExistsMatched);

		HashSet<string> fuzzyMatched = new(StringComparer.OrdinalIgnoreCase);
		foreach (string addr in naninovelAddresses)
		{
			if (matchedAddresses.Contains(addr))
			{
				continue;
			}

			int lastSlash = addr.LastIndexOf('/');
			string fileName = lastSlash >= 0 ? addr[(lastSlash + 1)..] : addr;
			if (string.IsNullOrEmpty(fileName))
			{
				continue;
			}

			try
			{
				string? found = Directory.EnumerateFiles(resourcesBase, fileName + ".*", new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 10 })
					.FirstOrDefault(f => !f.EndsWith(".meta", StringComparison.OrdinalIgnoreCase));
				if (found is not null)
				{
					fuzzyMatched.Add(addr);
				}
			}
			catch
			{
			}
		}

		matchedAddresses.UnionWith(fuzzyMatched);

		int totalResources = naninovelAddresses.Count;
		int matchedResources = matchedAddresses.Count;
		int skippedResources = result.Skipped.Count;
		int failedResources = result.Failures.Count;

		double overallMatchRate = totalResources > 0 ? (double)matchedResources / totalResources * 100.0 : 0.0;
		bool fullMatchAchieved = Math.Abs(overallMatchRate - 100.0) < 0.001;

		MatchMethodStatistics matchMethodStats = ComputeMatchMethodStats(result);
		IReadOnlyList<MatchRateStatistics> pathPrefixStats = ComputePathPrefixStats(catalog, config, matchedAddresses);
		IReadOnlyList<string> unmappedAddresses = BuildUnmappedAddresses(naninovelAddresses, matchedAddresses);

		if (fileExistsMatched.Count > 0)
		{
			Logger.Info(LogCategory.Export, $"文件存在匹配: 额外匹配 {fileExistsMatched.Count} 个地址（文件已在目标位置）");
		}

		if (fuzzyMatched.Count > 0)
		{
			Logger.Info(LogCategory.Export, $"模糊名称匹配: 额外匹配 {fuzzyMatched.Count} 个地址（文件名匹配但路径不同）");
		}

		if (fullMatchAchieved)
		{
			Logger.Info(LogCategory.Export, $"100% 匹配目标达成: {matchedResources}/{totalResources} ({overallMatchRate:F1}%)");
		}
		else
		{
			Logger.Error(LogCategory.Export, $"100% 匹配目标未达成: {matchedResources}/{totalResources} ({overallMatchRate:F1}%), 未匹配 {unmappedAddresses.Count} 个地址");
			if (unmappedAddresses.Count > 0 && unmappedAddresses.Count <= 50)
			{
				foreach (string addr in unmappedAddresses)
				{
					Logger.Info(LogCategory.Export, $"  未匹配地址: {addr}");
				}
			}
		}

		return new FullMatchReport
		{
			TotalResources = totalResources,
			MatchedResources = matchedResources,
			SkippedResources = skippedResources,
			FailedResources = failedResources,
			OverallMatchRate = overallMatchRate,
			MatchMethodStats = matchMethodStats,
			PathPrefixStats = pathPrefixStats,
			UnmappedAddresses = unmappedAddresses,
			FullMatchAchieved = fullMatchAchieved,
			StaleAddresses = staleSet.OrderBy(s => s).ToList(),
		};
	}

	private static MatchMethodStatistics ComputeMatchMethodStats(MoveResult result)
	{
		int pathIdAddressCount = 0;
		int originalGuidCount = 0;
		int pathIdCount = 0;
		int nameCount = 0;

		foreach (MoveSuccess success in result.Successes)
		{
			switch (success.Item.MatchMethod)
			{
				case MatchMethod.PathIdAddress:
					pathIdAddressCount++;
					break;
				case MatchMethod.OriginalGuid:
				case MatchMethod.Guid:
					originalGuidCount++;
					break;
				case MatchMethod.PathId:
					pathIdCount++;
					break;
				case MatchMethod.Name:
					nameCount++;
					break;
			}
		}

		return new MatchMethodStatistics
		{
			PathIdAddressCount = pathIdAddressCount,
			OriginalGuidCount = originalGuidCount,
			PathIdCount = pathIdCount,
			NameCount = nameCount,
		};
	}

	private static IReadOnlyList<MatchRateStatistics> ComputePathPrefixStats(CatalogData catalog, NaninovelConfig config, HashSet<string> matchedAddresses)
	{
		List<MatchRateStatistics> stats = new(config.PathPrefixes.Count);

		foreach (string prefix in config.PathPrefixes)
		{
			int totalAddresses = 0;
			int matchedCount = 0;

			if (catalog.AddressesByPrefix.TryGetValue(prefix, out IReadOnlyList<string>? addresses))
			{
				totalAddresses = addresses.Count;
				foreach (string addr in addresses)
				{
					if (matchedAddresses.Contains(addr))
					{
						matchedCount++;
					}
				}
			}

			int unmatchedCount = totalAddresses - matchedCount;
			double matchRate = totalAddresses > 0 ? (double)matchedCount / totalAddresses * 100.0 : 0.0;

			stats.Add(new MatchRateStatistics
			{
				PathPrefix = prefix,
				TotalAddresses = totalAddresses,
				MatchedCount = matchedCount,
				UnmatchedCount = unmatchedCount,
				MatchRate = matchRate,
			});
		}

		return stats;
	}

	private static IReadOnlyList<string> BuildUnmappedAddresses(HashSet<string> naninovelAddresses, HashSet<string> matchedAddresses)
	{
		List<string> unmapped = new();
		foreach (string addr in naninovelAddresses)
		{
			if (!matchedAddresses.Contains(addr))
			{
				unmapped.Add(addr);
			}
		}
		unmapped.Sort(StringComparer.OrdinalIgnoreCase);
		return unmapped;
	}

	private static bool IsInScope(string address, NaninovelConfig config)
	{
		int slashIndex = address.IndexOf('/');
		string prefix = slashIndex >= 0 ? address[..slashIndex] : address;
		return config.IsPathPrefixInScope(prefix);
	}
}