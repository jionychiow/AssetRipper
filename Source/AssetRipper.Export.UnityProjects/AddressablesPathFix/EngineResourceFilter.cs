using System.IO;

namespace AssetRipper.Export.UnityProjects.AddressablesPathFix;

public static class EngineResourceFilter
{
	private static readonly HashSet<string> ExcludedFileNames = new(StringComparer.OrdinalIgnoreCase)
	{
		"globalgamemanagers",
		"globalgamemanagers.assets",
		"unity default resources",
		"unity default resources.ress",
		"unity_builtin_extra",
		"resources.assets",
		"sharedassets0.assets",
		"sharedassets1.assets",
		"sharedassets2.assets",
		"sharedassets3.assets",
	};

	private static readonly HashSet<string> ExcludedExtensions = new(StringComparer.OrdinalIgnoreCase)
	{
		".resS",
		".resource",
		".bundle",
	};

	public static (bool ShouldExclude, string? Reason) ShouldExclude(string relativePath)
	{
		string fileName = Path.GetFileName(relativePath);

		if (ExcludedFileNames.Contains(fileName))
		{
			return (true, "引擎资源文件");
		}

		string extension = Path.GetExtension(relativePath);
		if (ExcludedExtensions.Contains(extension))
		{
			return (true, "引擎二进制资源扩展名");
		}

		string normalizedPath = relativePath.Replace('\\', '/');
		if (normalizedPath.Contains("StreamingAssets/", StringComparison.OrdinalIgnoreCase))
		{
			return (true, "StreamingAssets 目录");
		}

		if (normalizedPath.Contains("unity_builtin_extra", StringComparison.OrdinalIgnoreCase))
		{
			return (true, "Unity 内置额外资源");
		}

		if (fileName.StartsWith("built-in ", StringComparison.OrdinalIgnoreCase))
		{
			return (true, "Unity 内置资源");
		}

		return (false, null);
	}
}
