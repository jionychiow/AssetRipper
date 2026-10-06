using AssetRipper.Import.Logging;

namespace AssetRipper.Export.UnityProjects.Scripts;

public static class CompilationReadinessReport
{
	public static void LogToLogger(PostProcessStatistics postProcessStats, int asmdefConfigCount, List<string> riskWarnings)
	{
		Logger.Info(LogCategory.Export, $"Compilation readiness report: {postProcessStats.FixedFileCount} files fixed, {asmdefConfigCount} .asmdef generated, {riskWarnings.Count} risk warnings");

		foreach (string warning in riskWarnings)
		{
			Logger.Warning(LogCategory.Export, warning);
		}
	}
}