using AssetRipper.Export.Configuration;
using AssetRipper.Import.Logging;
using AssetRipper.Processing;
using System.IO;
using System.Text.RegularExpressions;

namespace AssetRipper.Export.UnityProjects.Naninovel.Dedup;

public sealed class NaninovelResourceDedupPostExporter : IPostExporter
{
	private const string ResourcesPath = "Resources";
	private const string NaniExtension = ".nani";
	private const string MetaExtension = ".meta";
	private static readonly Regex SuffixRegex = new(@"_(\d+)(\.[^.]+)$", RegexOptions.Compiled);

	public void DoPostExport(GameData gameData, FullConfiguration settings, FileSystem fileSystem)
	{
		Logger.Info(LogCategory.Export, "NaninovelResourceDedupPostExporter: ensuring non-suffixed resource copies...");

		string resourcesDir = fileSystem.Path.Join(settings.AssetsPath, ResourcesPath);
		if (!fileSystem.Directory.Exists(resourcesDir))
		{
			Logger.Info(LogCategory.Export, $"Resources directory not found: {resourcesDir}");
			return;
		}

		int createdCount = 0;
		foreach (string filePath in EnumerateTargetFiles(resourcesDir, fileSystem))
		{
			if (TryCreateNonSuffixedCopy(filePath, fileSystem))
			{
				createdCount++;
			}
		}

		Logger.Info(LogCategory.Export, $"NaninovelResourceDedupPostExporter: created {createdCount} non-suffixed copy/copies.");

		RemoveRedundantSuffixedNaniCopies(resourcesDir, fileSystem);
	}

	/// <summary>
	/// R2（repair_plan）：删除冗余 _数字 后缀 .nani 副本。
	/// 依据审计（A模块溯源）：639/645 份副本与本体内容逐字节一致（同一资产实例被导出多次）；
	/// Naninovel 运行时按脚本名直接读磁盘加载 .nani，不依赖副本 GUID。
	/// 快照约束（ModificationSnapshot）：仅删除与本体 SHA-256 一致的副本；删除数 == 符合条件清单数（断言输出）；不一致副本保留并警告。
	/// </summary>
	private static void RemoveRedundantSuffixedNaniCopies(string resourcesDir, FileSystem fileSystem)
	{
		string naniScriptsDir = fileSystem.Path.Join(resourcesDir, "nScripts");
		if (!fileSystem.Directory.Exists(naniScriptsDir))
		{
			return;
		}

		List<string> candidates = new();
		foreach (string filePath in fileSystem.Directory.EnumerateFiles(naniScriptsDir, "*" + NaniExtension, SearchOption.AllDirectories))
		{
			string fileName = Path.GetFileName(filePath);
			if (SuffixRegex.IsMatch(fileName))
			{
				candidates.Add(filePath);
			}
		}

		int deletedCount = 0;
		int skippedMismatch = 0;
		int skippedMissingBase = 0;
		foreach (string filePath in candidates)
		{
			string fileName = Path.GetFileName(filePath);
			Match match = SuffixRegex.Match(fileName);
			string nonSuffixedName = fileName[..match.Index] + match.Groups[2].Value;
			string dirPath = Path.GetDirectoryName(filePath)!;
			string nonSuffixedPath = Path.Join(dirPath, nonSuffixedName);

			if (!fileSystem.File.Exists(nonSuffixedPath))
			{
				skippedMissingBase++;
				continue;
			}

			if (!FileEquals(filePath, nonSuffixedPath))
			{
				skippedMismatch++;
				Logger.Warning(LogCategory.Export, $"NaninovelResourceDedupPostExporter: content mismatch, kept redundant copy: {filePath}");
				continue;
			}

			try
			{
				File.Delete(filePath);
				string metaPath = filePath + MetaExtension;
				if (File.Exists(metaPath))
				{
					File.Delete(metaPath);
				}
				deletedCount++;
			}
			catch (Exception ex)
			{
				Logger.Warning(LogCategory.Export, $"NaninovelResourceDedupPostExporter: failed to delete redundant copy {filePath}: {ex.Message}");
			}
		}

		if (deletedCount + skippedMismatch + skippedMissingBase != candidates.Count)
		{
			Logger.Warning(LogCategory.Export, $"NaninovelResourceDedupPostExporter: SNAPSHOT ASSERTION FAILED - planned={candidates.Count}, deleted={deletedCount}, mismatch={skippedMismatch}, missingBase={skippedMissingBase}");
		}
		else
		{
			Logger.Info(LogCategory.Export, $"NaninovelResourceDedupPostExporter: removed {deletedCount} redundant suffixed .nani copies (mismatch kept: {skippedMismatch}, missing base: {skippedMissingBase}; planned={candidates.Count}).");
		}
	}

	private static bool FileEquals(string pathA, string pathB)
	{
		try
		{
			FileInfo infoA = new(pathA);
			FileInfo infoB = new(pathB);
			if (infoA.Length != infoB.Length)
			{
				return false;
			}
			using FileStream streamA = File.OpenRead(pathA);
			using FileStream streamB = File.OpenRead(pathB);
			const int bufferSize = 81920;
			byte[] bufferA = new byte[bufferSize];
			byte[] bufferB = new byte[bufferSize];
			while (true)
			{
				int readA = streamA.Read(bufferA, 0, bufferSize);
				int readB = streamB.Read(bufferB, 0, bufferSize);
				if (readA != readB)
				{
					return false;
				}
				if (readA == 0)
				{
					return true;
				}
				if (!bufferA.AsSpan(0, readA).SequenceEqual(bufferB.AsSpan(0, readB)))
				{
					return false;
				}
			}
		}
		catch
		{
			return false;
		}
	}

	private static IEnumerable<string> EnumerateTargetFiles(string rootDir, FileSystem fileSystem)
	{
		foreach (string file in fileSystem.Directory.EnumerateFiles(rootDir, "*" + NaniExtension, SearchOption.AllDirectories))
		{
			yield return file;
		}

		string naninovelDir = fileSystem.Path.Join(rootDir, "naninovel");
		if (fileSystem.Directory.Exists(naninovelDir))
		{
			foreach (string file in fileSystem.Directory.EnumerateFiles(naninovelDir, "*", SearchOption.AllDirectories))
			{
				if (file.EndsWith(MetaExtension, StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}
				yield return file;
			}
		}
	}

	private static bool TryCreateNonSuffixedCopy(string filePath, FileSystem fileSystem)
	{
		string fileName = Path.GetFileName(filePath);
		Match match = SuffixRegex.Match(fileName);
		if (!match.Success)
		{
			return false;
		}

		string nonSuffixedName = fileName[..match.Index] + match.Groups[2].Value;
		string dirPath = Path.GetDirectoryName(filePath)!;
		string nonSuffixedPath = Path.Join(dirPath, nonSuffixedName);

		if (fileSystem.File.Exists(nonSuffixedPath))
		{
			return false;
		}

		try
		{
			File.Copy(filePath, nonSuffixedPath, overwrite: false);
			string metaPath = filePath + MetaExtension;
			if (File.Exists(metaPath))
			{
				string newMetaPath = nonSuffixedPath + MetaExtension;
				string metaContent = File.ReadAllText(metaPath);
				string newGuid = System.Guid.NewGuid().ToString("N");
				metaContent = ReplaceGuidInMeta(metaContent, newGuid);
				File.WriteAllText(newMetaPath, metaContent);
			}
			Logger.Info(LogCategory.Export, $"Created non-suffixed copy: {nonSuffixedPath} (from {filePath})");
			return true;
		}
		catch (Exception ex)
		{
			Logger.Warning(LogCategory.Export, $"Failed to create non-suffixed copy of {filePath}: {ex.Message}");
			return false;
		}
	}

	private static string ReplaceGuidInMeta(string metaContent, string newGuid)
	{
		int guidIndex = metaContent.IndexOf("guid: ", StringComparison.Ordinal);
		if (guidIndex < 0)
		{
			return metaContent;
		}
		int valueStart = guidIndex + 6;
		int valueEnd = valueStart;
		while (valueEnd < metaContent.Length && metaContent[valueEnd] != '\n' && metaContent[valueEnd] != '\r')
		{
			valueEnd++;
		}
		return metaContent[..valueStart] + newGuid + metaContent[valueEnd..];
	}
}