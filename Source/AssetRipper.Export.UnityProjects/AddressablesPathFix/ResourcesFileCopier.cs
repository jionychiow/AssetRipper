using System.IO;
using AssetRipper.Export.UnityProjects.AddressablesPathFix.Models;
using AssetRipper.Import.Logging;

namespace AssetRipper.Export.UnityProjects.AddressablesPathFix;

public sealed class ResourcesFileCopier
{
	public ResourcesCopyResult Copy(string sourceRoot, string targetRoot, FileSystem fileSystem)
	{
		List<ResourcesCopySuccess> copiedFiles = [];
		List<ResourcesCopySkip> skippedFiles = [];
		List<ResourcesCopyFailure> failedFiles = [];
		List<string> excludedFiles = [];
		int totalScanned = 0;

		if (!fileSystem.Directory.Exists(sourceRoot))
		{
			Logger.Warning(LogCategory.Export, $"Resources 源目录不存在: {sourceRoot}");
			return new ResourcesCopyResult
			{
				CopiedFiles = copiedFiles,
				SkippedFiles = skippedFiles,
				FailedFiles = failedFiles,
				ExcludedFiles = excludedFiles,
				TotalScanned = 0,
			};
		}

		Directory.CreateDirectory(targetRoot);

		IEnumerable<string> allFiles;
		try
		{
			allFiles = Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories);
		}
		catch (Exception ex)
		{
			Logger.Error(LogCategory.Export, $"枚举源文件失败: {ex.Message}");
			return new ResourcesCopyResult
			{
				CopiedFiles = copiedFiles,
				SkippedFiles = skippedFiles,
				FailedFiles = failedFiles,
				ExcludedFiles = excludedFiles,
				TotalScanned = totalScanned,
			};
		}

		foreach (string sourceFile in allFiles)
		{
			totalScanned++;

			string relativePath = Path.GetRelativePath(sourceRoot, sourceFile).Replace('\\', '/');

			(bool shouldExclude, string? reason) = EngineResourceFilter.ShouldExclude(relativePath);
			if (shouldExclude)
			{
				excludedFiles.Add(relativePath);
				continue;
			}

			string targetFile = Path.Join(targetRoot, relativePath);

			if (File.Exists(targetFile))
			{
				skippedFiles.Add(new ResourcesCopySkip
				{
					SourcePath = sourceFile,
					TargetPath = targetFile,
					Reason = "目标已存在",
				});
				continue;
			}

			string? targetDir = Path.GetDirectoryName(targetFile);
			if (targetDir is not null && !Directory.Exists(targetDir))
			{
				try
				{
					Directory.CreateDirectory(targetDir);
				}
				catch (Exception ex)
				{
					failedFiles.Add(new ResourcesCopyFailure
					{
						SourcePath = sourceFile,
						TargetPath = targetFile,
						Exception = ex.Message,
						Stage = "目录创建",
					});
					continue;
				}
			}

			try
			{
				File.SetAttributes(sourceFile, FileAttributes.Normal);
				File.Copy(sourceFile, targetFile, false);

				long sourceSize = new FileInfo(sourceFile).Length;
				long targetSize = new FileInfo(targetFile).Length;
				if (sourceSize != targetSize)
				{
					failedFiles.Add(new ResourcesCopyFailure
					{
						SourcePath = sourceFile,
						TargetPath = targetFile,
						Exception = $"文件大小不一致: 源={sourceSize}, 目标={targetSize}",
						Stage = "验证",
					});
					continue;
				}

				copiedFiles.Add(new ResourcesCopySuccess
				{
					SourcePath = sourceFile,
					TargetPath = targetFile,
					FileSizeBytes = sourceSize,
				});
			}
			catch (Exception ex)
			{
				failedFiles.Add(new ResourcesCopyFailure
				{
					SourcePath = sourceFile,
					TargetPath = targetFile,
					Exception = ex.Message,
					Stage = "文件复制",
				});
			}

			if (totalScanned % 100 == 0)
			{
				Logger.Info(LogCategory.Export, $"Resources 复制进度: 已扫描 {totalScanned}, 已复制 {copiedFiles.Count}, 跳过 {skippedFiles.Count}, 失败 {failedFiles.Count}");
			}
		}

		Logger.Info(LogCategory.Export, $"Resources 复制完成: 总扫描={totalScanned}, 已复制={copiedFiles.Count}, 跳过={skippedFiles.Count}, 失败={failedFiles.Count}, 排除={excludedFiles.Count}");

		return new ResourcesCopyResult
		{
			CopiedFiles = copiedFiles,
			SkippedFiles = skippedFiles,
			FailedFiles = failedFiles,
			ExcludedFiles = excludedFiles,
			TotalScanned = totalScanned,
		};
	}
}