using AssetRipper.Import.Logging;

namespace AssetRipper.Export.UnityProjects.Scripts;

public static class DecompiledCodePostProcessor
{
	private static readonly IPostProcessRule[] PostProcessRules =
	[
		new AccessModifierFixRule(),
		new UsingDirectiveFixRule(),
		new DuplicateAttributeFixRule(),
	];

	public static PostProcessStatistics PostProcessDirectory(string outputFolder, FileSystem fileSystem)
	{
		PostProcessStatistics totalStats = new();
		if (!fileSystem.Directory.Exists(outputFolder))
		{
			return totalStats;
		}

		foreach (string filePath in EnumerateCsFiles(outputFolder, fileSystem))
		{
			try
			{
				PostProcessStatistics fileStats = PostProcessFile(filePath, fileSystem);
				totalStats.Merge(fileStats);
			}
			catch (Exception ex)
			{
				Logger.Error(LogCategory.Export, $"Failed to post-process file '{filePath}': {ex.Message}");
			}
		}

		return totalStats;
	}

	public static PostProcessStatistics PostProcessFile(string filePath, FileSystem fileSystem)
	{
		PostProcessStatistics stats = new();
		stats.AddProcessedFile();

		string content = fileSystem.File.ReadAllText(filePath);
		if (string.IsNullOrEmpty(content))
		{
			return stats;
		}

		string originalContent = content;
		List<string> appliedRules = [];

		foreach (IPostProcessRule rule in PostProcessRules)
		{
			if (rule.TryFix(content, out string fixedContent, out string fixRecord))
			{
				if (ValidateBraceBalance(fixedContent))
				{
					content = fixedContent;
					stats.AddFix(rule.Name);
					appliedRules.Add($"{rule.Name}: {fixRecord}");
					Logger.Info(LogCategory.Export, $"Post-processed '{filePath}' with rule '{rule.Name}': {fixRecord}");
				}
				else
				{
					Logger.Error(LogCategory.Export, $"Rule '{rule.Name}' produced invalid syntax for '{filePath}'. Reverting.");
					content = originalContent;
				}
			}
		}

		if (content != originalContent)
		{
			fileSystem.File.WriteAllText(filePath, content);
		}

		return stats;
	}

	private static IEnumerable<string> EnumerateCsFiles(string directory, FileSystem fileSystem)
	{
		foreach (string file in fileSystem.Directory.EnumerateFiles(directory, "*.cs"))
		{
			yield return file;
		}
		foreach (string subDir in fileSystem.Directory.EnumerateDirectories(directory))
		{
			foreach (string file in EnumerateCsFiles(subDir, fileSystem))
			{
				yield return file;
			}
		}
	}

	private static bool ValidateBraceBalance(string content)
	{
		int openBraces = 0;
		int closeBraces = 0;
		foreach (char c in content)
		{
			if (c == '{')
			{
				openBraces++;
			}
			else if (c == '}')
			{
				closeBraces++;
			}
		}
		return openBraces == closeBraces;
	}
}