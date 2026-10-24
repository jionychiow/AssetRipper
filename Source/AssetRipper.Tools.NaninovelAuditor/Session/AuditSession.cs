using AssetRipper.Export.Configuration;
using AssetRipper.Export.UnityProjects;
using AssetRipper.Import.Logging;
using AssetRipper.IO.Files;
using AssetRipper.Processing;
using AssetRipper.Tools.NaninovelAuditor.Models;

namespace AssetRipper.Tools.NaninovelAuditor.Session;

/// <summary>
/// 只读审计会话：复用导出同管线（ExportHandler.LoadAndProcess，不调用 Export）建立
/// 与导出时完全一致的 GameData 视图，单次加载全模块复用；会话开启前后校验原始数据零写入。
/// </summary>
public sealed class AuditSession : IDisposable
{
	public GameData GameData { get; }
	public AuditOptions Options { get; }

	private AuditSession(GameData gameData, AuditOptions options)
	{
		GameData = gameData;
		Options = options;
	}

	public void Dispose()
	{
		// GameData holds no unmanaged handles requiring explicit release in current usage.
	}

	public static AuditSession Open(AuditOptions options)
	{
		Logger.Info(LogCategory.System, $"[NaninovelAuditor] Opening audit session for {options.SourceDirectory}...");

		SourceWriteSnapshot before = SourceWriteSnapshot.Take(options.SourceDirectory);

		AssetRipper.Import.LayoutFixStatistics.Reset();
		FullConfiguration settings = new();
		settings.LoadFromDefaultPath();
		ExportHandler handler = new(settings);
		GameData gameData = handler.LoadAndProcess([options.SourceDirectory], LocalFileSystem.Instance);

		AssetRipper.Import.LayoutFixStatistics.LogSummary();

		SourceWriteSnapshot after = SourceWriteSnapshot.Take(options.SourceDirectory);
		if (!before.Equals(after))
		{
			throw new InvalidOperationException(
				$"[NaninovelAuditor] Source data write detected! before={before}, after={after}. Aborting to protect source data.");
		}

		Logger.Info(LogCategory.System, "[NaninovelAuditor] Audit session opened: source data zero-write verification passed.");
		return new AuditSession(gameData, options);
	}

	/// <summary>原始数据源零写入快照（文件数 + 总字节数 + 目录数）。</summary>
	private readonly record struct SourceWriteSnapshot(int FileCount, long TotalBytes, int DirectoryCount)
	{
		public static SourceWriteSnapshot Take(string root)
		{
			int files = 0;
			int directories = 0;
			long totalBytes = 0;
			if (!Directory.Exists(root))
			{
				return new SourceWriteSnapshot(0, 0, 0);
			}
			foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
			{
				files++;
				totalBytes += new FileInfo(file).Length;
			}
			foreach (string dir in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories))
			{
				directories++;
			}
			return new SourceWriteSnapshot(files, totalBytes, directories);
		}
	}
}