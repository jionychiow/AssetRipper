using System.Diagnostics;
using AssetRipper.Export.UnityProjects.AddressablesPathFix.Models;
using AssetRipper.Import.Logging;

namespace AssetRipper.Export.UnityProjects.AddressablesPathFix;

public sealed class ResourceMover
{
	public MoveResult Execute(MovePlan plan, NaninovelConfig config, string assetsPath, FileSystem fileSystem, ConflictStrategy conflictStrategy)
	{
		List<MoveSuccess> successes = new(plan.MoveItems.Count);
		List<MoveSkip> skipped = new();
		List<MoveFailure> failures = new();
		List<MoveConflict> conflicts = new();

		int total = plan.MoveItems.Count;
		int processed = 0;

		foreach (MoveItem item in plan.MoveItems)
		{
			processed++;
			if (processed % 100 == 0)
			{
				Logger.Info(LogCategory.Export, $"进度: {processed}/{total}, 当前: {item.Address}");
			}

			try
			{
				string targetDir = fileSystem.Path.GetDirectoryName(item.TargetPath)!;
				if (!EnsureDirectoryExists(targetDir))
				{
					failures.Add(new MoveFailure(item, "目录创建失败", MoveStage.CreateDirectory));
					continue;
				}

				if (fileSystem.File.Exists(item.TargetPath))
				{
					MoveConflict? conflict = HandleConflict(item, fileSystem, conflictStrategy);
					if (conflict is not null)
					{
						conflicts.Add(conflict);
						if (conflictStrategy == ConflictStrategy.Skip)
						{
							skipped.Add(new MoveSkip(item, "目标文件已存在"));
							continue;
						}
					}
				}

				Stopwatch sw = Stopwatch.StartNew();
				(MoveStage stage, string? error) = MoveResourceWithMeta(item, fileSystem);
				sw.Stop();

				if (error is not null)
				{
					failures.Add(new MoveFailure(item, error, stage));
				}
				else
				{
					successes.Add(new MoveSuccess(item, sw.ElapsedMilliseconds));
				}
			}
			catch (Exception ex)
			{
				failures.Add(new MoveFailure(item, ex.Message, MoveStage.MoveResourceFile));
			}
		}

		IReadOnlyList<string> placeholders = CreatePlaceholderDirectories(config, GetCatalogPrefixes(plan), assetsPath, fileSystem);

		Logger.Info(LogCategory.Export, $"文件移动完成: 成功={successes.Count}, 跳过={skipped.Count}, 失败={failures.Count}, 冲突={conflicts.Count}, 占位目录={placeholders.Count}");

		return new MoveResult
		{
			Successes = successes,
			Skipped = skipped,
			Failures = failures,
			Conflicts = conflicts,
			CreatedPlaceholders = placeholders,
		};
	}

	private static bool EnsureDirectoryExists(string dirPath)
	{
		if (Directory.Exists(dirPath))
		{
			return true;
		}

		try
		{
			Directory.CreateDirectory(dirPath);
			return true;
		}
		catch (Exception ex)
		{
			Logger.Error(LogCategory.Export, $"目录创建失败: {dirPath}, 错误: {ex.Message}");
			return false;
		}
	}

	private static bool MoveFileAtomic(string sourcePath, string targetPath, FileSystem fileSystem)
	{
		try
		{
			if (File.Exists(sourcePath))
			{
				File.SetAttributes(sourcePath, FileAttributes.Normal);
			}

			File.Move(sourcePath, targetPath, true);
			return true;
		}
		catch (Exception)
		{
			try
			{
				File.Copy(sourcePath, targetPath, true);
				FileInfo sourceInfo = new(sourcePath);
				FileInfo targetInfo = new(targetPath);
				if (sourceInfo.Length != targetInfo.Length)
				{
					Logger.Warning(LogCategory.Export, $"跨分区复制大小不匹配: {sourcePath} ({sourceInfo.Length}) -> {targetPath} ({targetInfo.Length})");
					return false;
				}

				File.Delete(sourcePath);
				return true;
			}
			catch (Exception ex)
			{
				Logger.Error(LogCategory.Export, $"文件移动失败: {sourcePath} -> {targetPath}, 错误: {ex.Message}");
				return false;
			}
		}
	}

	private static (MoveStage stage, string? error) MoveResourceWithMeta(MoveItem item, FileSystem fileSystem)
	{
		if (!string.IsNullOrEmpty(item.SourceMetaPath) && fileSystem.File.Exists(item.SourceMetaPath))
		{
			if (!MoveFileAtomic(item.SourceMetaPath, item.TargetMetaPath, fileSystem))
			{
				return (MoveStage.MoveMetaFile, ".meta 文件移动失败");
			}
		}
		else if (!string.IsNullOrEmpty(item.SourceMetaPath))
		{
			Logger.Warning(LogCategory.Export, $".meta 文件缺失: {item.SourceMetaPath}");
		}

		if (!MoveFileAtomic(item.SourcePath, item.TargetPath, fileSystem))
		{
			return (MoveStage.MoveResourceFile, "资源文件移动失败");
		}

		if (!fileSystem.File.Exists(item.TargetPath))
		{
			return (MoveStage.VerifyTarget, "目标文件验证失败");
		}

		return (MoveStage.VerifyTarget, null);
	}

	private static MoveConflict? HandleConflict(MoveItem item, FileSystem fileSystem, ConflictStrategy strategy)
	{
		switch (strategy)
		{
			case ConflictStrategy.Skip:
				Logger.Warning(LogCategory.Export, $"冲突跳过: {item.TargetPath} 已存在");
				return new MoveConflict(item, strategy, "跳过");

			case ConflictStrategy.Overwrite:
				Logger.Info(LogCategory.Export, $"冲突覆盖: {item.TargetPath}");
				if (fileSystem.File.Exists(item.TargetMetaPath))
				{
					File.Delete(item.TargetMetaPath);
				}
				return new MoveConflict(item, strategy, "覆盖");

			case ConflictStrategy.Rename:
				string dir = fileSystem.Path.GetDirectoryName(item.TargetPath)!;
				string baseName = fileSystem.Path.GetFileNameWithoutExtension(item.TargetPath);
				string ext = fileSystem.Path.GetExtension(item.TargetPath);
				int suffix = 1;
				string newPath;
				do
				{
					newPath = fileSystem.Path.Join(dir, $"{baseName}_{suffix}{ext}");
					suffix++;
				} while (fileSystem.File.Exists(newPath));

				Logger.Info(LogCategory.Export, $"冲突重命名: {item.TargetPath} -> {newPath}");
				return new MoveConflict(item, strategy, $"重命名为 {fileSystem.Path.GetFileName(newPath)}");

			default:
				return null;
		}
	}

	private static IReadOnlyList<string> CreatePlaceholderDirectories(NaninovelConfig config, IReadOnlySet<string> catalogPrefixes, string assetsPath, FileSystem fileSystem)
	{
		List<string> created = new();
		string resourcesRoot = fileSystem.Path.Join(assetsPath, "Resources");

		foreach (string prefix in config.PathPrefixes)
		{
			if (catalogPrefixes.Contains(prefix))
			{
				continue;
			}

			string dirPath = fileSystem.Path.Join(resourcesRoot, prefix);
			if (fileSystem.Directory.Exists(dirPath))
			{
				continue;
			}

			try
			{
				Directory.CreateDirectory(dirPath);
				string gitkeepPath = fileSystem.Path.Join(dirPath, ".gitkeep");
				File.WriteAllText(gitkeepPath, string.Empty);
				created.Add(dirPath);
			}
			catch (Exception ex)
			{
				Logger.Warning(LogCategory.Export, $"占位目录创建失败: {dirPath}, 错误: {ex.Message}");
			}
		}

		return created;
	}

	private static IReadOnlySet<string> GetCatalogPrefixes(MovePlan plan)
	{
		HashSet<string> prefixes = new(StringComparer.OrdinalIgnoreCase);
		foreach (MoveItem item in plan.MoveItems)
		{
			int slashIndex = item.Address.IndexOf('/');
			if (slashIndex >= 0)
			{
				prefixes.Add(item.Address[..slashIndex]);
			}
			else
			{
				prefixes.Add(item.Address);
			}
		}
		return prefixes;
	}
}
