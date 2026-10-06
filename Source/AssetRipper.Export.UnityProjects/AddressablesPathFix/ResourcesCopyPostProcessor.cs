using AssetRipper.Export.Configuration;
using AssetRipper.Export.UnityProjects.AddressablesPathFix.Models;
using AssetRipper.Import.Logging;

namespace AssetRipper.Export.UnityProjects.AddressablesPathFix;

public sealed class ResourcesCopyPostProcessor
{
	public ResourcesCopyPostProcessResult Process(FullConfiguration settings, FileSystem fileSystem)
	{
		try
		{
			string? sourceResources = SourceGameLocator.LocateResources(settings, fileSystem);
			if (sourceResources is null)
			{
				return new ResourcesCopyPostProcessResult
				{
					CopyResult = null,
					MetaResult = null,
					ValidationResult = null,
					Skipped = true,
					SkipReason = "原始游戏 Resources 目录未找到",
				};
			}

			string targetResources = fileSystem.Path.Join(settings.AssetsPath, "Resources");
			Directory.CreateDirectory(targetResources);

			Logger.Info(LogCategory.Export, $"开始 Resources 文件复制: {sourceResources} -> {targetResources}");

			ResourcesFileCopier copier = new();
			ResourcesCopyResult copyResult = copier.Copy(sourceResources, targetResources, fileSystem);

			List<string> copiedPaths = copyResult.CopiedFiles.Select(f => f.TargetPath).ToList();

			MetaFileGenerator metaGenerator = new();
			MetaBatchResult metaResult = metaGenerator.GenerateForFiles(copiedPaths, fileSystem);

			List<string> allResourceFiles = Directory.EnumerateFiles(targetResources, "*", SearchOption.AllDirectories)
				.Where(f => !f.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
				.Where(f => !File.Exists(f + ".meta"))
				.ToList();
			if (allResourceFiles.Count > 0)
			{
				Logger.Info(LogCategory.Export, $"为 {allResourceFiles.Count} 个非复制文件生成 .meta");
				MetaBatchResult extraMetaResult = metaGenerator.GenerateForFiles(allResourceFiles, fileSystem);
			}

			MetaFileValidator metaValidator = new();
			MetaValidationResult validationResult = metaValidator.Validate(targetResources, fileSystem);

			Logger.Info(LogCategory.Export, $"Resources 后处理完成: 复制={copyResult.CopiedFiles.Count}, .meta创建={metaResult.CreatedCount}, 验证={(validationResult.IsValid ? "通过" : "失败")}");

			return new ResourcesCopyPostProcessResult
			{
				CopyResult = copyResult,
				MetaResult = metaResult,
				ValidationResult = validationResult,
				Skipped = false,
				SkipReason = null,
			};
		}
		catch (Exception ex)
		{
			Logger.Error(LogCategory.Export, $"Resources 后处理异常: {ex.Message}");
			return new ResourcesCopyPostProcessResult
			{
				CopyResult = null,
				MetaResult = null,
				ValidationResult = null,
				Skipped = true,
				SkipReason = $"异常: {ex.Message}",
			};
		}
	}
}