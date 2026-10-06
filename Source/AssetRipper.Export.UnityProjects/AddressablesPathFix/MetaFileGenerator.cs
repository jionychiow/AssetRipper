using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using AssetRipper.Export.UnityProjects.AddressablesPathFix.Models;
using AssetRipper.Import.Logging;

namespace AssetRipper.Export.UnityProjects.AddressablesPathFix;

public sealed class MetaFileGenerator
{
	private static readonly Regex GuidRegex = new(@"guid:\s*([0-9a-fA-F]{32})", RegexOptions.IgnoreCase | RegexOptions.Compiled);

	public MetaGenerationResult GenerateForFile(string resourceFilePath, FileSystem fileSystem)
	{
		string metaPath = resourceFilePath + ".meta";

		if (File.Exists(metaPath))
		{
			try
			{
				string existingContent = File.ReadAllText(metaPath);
				Match match = GuidRegex.Match(existingContent);
				string existingGuid = match.Success ? match.Groups[1].Value : "";
				return new MetaGenerationResult
				{
					MetaPath = metaPath,
					Guid = existingGuid,
					WasCreated = false,
					WasSkipped = true,
				};
			}
			catch
			{
			}
		}

		string guid = System.Guid.NewGuid().ToString("N");

		if (TextureMetaWriter.IsImageFile(resourceFilePath))
		{
			try
			{
				TextureMetaWriter.WriteDefaultTextureMeta(resourceFilePath, guid);
				return new MetaGenerationResult
				{
					MetaPath = metaPath,
					Guid = guid,
					WasCreated = true,
					WasSkipped = false,
				};
			}
			catch (Exception ex)
			{
				return new MetaGenerationResult
				{
					MetaPath = metaPath,
					Guid = guid,
					WasCreated = false,
					WasSkipped = false,
					Error = ex.Message,
				};
			}
		}

		string content = $"fileFormatVersion: 2\nguid: {guid}\n";

		try
		{
			File.WriteAllText(metaPath, content, new UTF8Encoding(false));
		}
		catch (Exception ex)
		{
			return new MetaGenerationResult
			{
				MetaPath = metaPath,
				Guid = guid,
				WasCreated = false,
				WasSkipped = false,
				Error = ex.Message,
			};
		}

		return new MetaGenerationResult
		{
			MetaPath = metaPath,
			Guid = guid,
			WasCreated = true,
			WasSkipped = false,
		};
	}

	public MetaBatchResult GenerateForFiles(IReadOnlyList<string> filePaths, FileSystem fileSystem)
	{
		List<MetaGenerationResult> results = [];
		int created = 0;
		int skipped = 0;
		int failed = 0;

		foreach (string filePath in filePaths)
		{
			MetaGenerationResult result = GenerateForFile(filePath, fileSystem);
			results.Add(result);

			if (result.WasCreated)
				created++;
			else if (result.WasSkipped)
				skipped++;
			else
				failed++;
		}

		Logger.Info(LogCategory.Export, $".meta 生成完成: 总数={filePaths.Count}, 创建={created}, 跳过={skipped}, 失败={failed}");

		return new MetaBatchResult
		{
			Results = results,
			CreatedCount = created,
			SkippedCount = skipped,
			FailedCount = failed,
		};
	}
}