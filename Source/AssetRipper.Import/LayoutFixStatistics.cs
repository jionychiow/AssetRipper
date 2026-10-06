using System.Collections.Concurrent;
using AssetRipper.Import.Logging;

namespace AssetRipper.Import;

public static class LayoutFixStatistics
{
	private static readonly ConcurrentDictionary<LayoutFixDecision, int> decisions = new();
	private static readonly ConcurrentDictionary<string, int> byNamespace = new();
	private static readonly ConcurrentDictionary<string, int> byFailureReason = new();

	public static void RecordDecision(LayoutFixDecision decision, long pathID, string scriptFullName, string? failureReason = null)
	{
		decisions.AddOrUpdate(decision, 1, (_, count) => count + 1);

		string ns = ExtractNamespace(scriptFullName);
		byNamespace.AddOrUpdate(ns, 1, (_, count) => count + 1);

		if (!string.IsNullOrEmpty(failureReason))
		{
			byFailureReason.AddOrUpdate(failureReason, 1, (_, count) => count + 1);
		}
	}

	public static void LogSummary()
	{
		int sourceGenSuccess = decisions.GetValueOrDefault(LayoutFixDecision.SourceGeneratedSuccess, 0);
		int reflectionSuccess = decisions.GetValueOrDefault(LayoutFixDecision.ReflectionSuccess, 0)
			+ decisions.GetValueOrDefault(LayoutFixDecision.SourceGeneratedFallbackToReflection, 0)
			+ decisions.GetValueOrDefault(LayoutFixDecision.ReflectionOnly, 0);
		int degraded = decisions.GetValueOrDefault(LayoutFixDecision.DegradedExport, 0);
		int totalFailure = decisions.GetValueOrDefault(LayoutFixDecision.TotalFailure, 0);
		int total = sourceGenSuccess + reflectionSuccess + degraded + totalFailure;

		Logger.Info(LogCategory.Export, $"Layout Fix Summary: Total={total}, SourceGenerated={sourceGenSuccess}, Reflection={reflectionSuccess}, Degraded={degraded}, Failed={totalFailure}");

		foreach (KeyValuePair<string, int> ns in byNamespace.OrderByDescending(x => x.Value))
		{
			Logger.Info(LogCategory.Export, $"  Namespace {ns.Key}: {ns.Value}");
		}

		if (byFailureReason.Count > 0)
		{
			Logger.Info(LogCategory.Export, "Failure reasons:");
			foreach (KeyValuePair<string, int> reason in byFailureReason.OrderByDescending(x => x.Value))
			{
				Logger.Info(LogCategory.Export, $"  {reason.Key}: {reason.Value}");
			}
		}
	}

	public static void Reset()
	{
		decisions.Clear();
		byNamespace.Clear();
		byFailureReason.Clear();
	}

	private static string ExtractNamespace(string scriptFullName)
	{
		int lastDot = scriptFullName.LastIndexOf('.');
		return lastDot > 0 ? scriptFullName[..lastDot] : "global";
	}
}