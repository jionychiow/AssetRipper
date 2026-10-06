using AssetRipper.Import;

namespace AssetRipper.Tests;

public class LayoutFixStatisticsTests
{
	[SetUp]
	public void SetUp()
	{
		LayoutFixStatistics.Reset();
	}

	[TearDown]
	public void TearDown()
	{
		LayoutFixStatistics.Reset();
	}

	[Test]
	public void RecordDecision_AccumulatesCount()
	{
		LayoutFixStatistics.RecordDecision(LayoutFixDecision.ReflectionSuccess, 1, "NS.ClassA");
		LayoutFixStatistics.RecordDecision(LayoutFixDecision.ReflectionSuccess, 2, "NS.ClassA");
		LayoutFixStatistics.RecordDecision(LayoutFixDecision.DegradedExport, 3, "NS.ClassB");
		LayoutFixStatistics.LogSummary();
	}

	[Test]
	public void RecordDecision_GroupsByNamespace()
	{
		LayoutFixStatistics.RecordDecision(LayoutFixDecision.ReflectionSuccess, 1, "UnityEngine.UI.Image");
		LayoutFixStatistics.RecordDecision(LayoutFixDecision.DegradedExport, 2, "Naninovel.Command");
		LayoutFixStatistics.RecordDecision(LayoutFixDecision.ReflectionSuccess, 3, "UnityEngine.UI.Text");
		LayoutFixStatistics.LogSummary();
	}

	[Test]
	public void RecordDecision_ClassifiesByFailureReason()
	{
		LayoutFixStatistics.RecordDecision(LayoutFixDecision.DegradedExport, 1, "NS.ClassA", "TryRead failed");
		LayoutFixStatistics.RecordDecision(LayoutFixDecision.DegradedExport, 2, "NS.ClassB", "TryRead failed");
		LayoutFixStatistics.RecordDecision(LayoutFixDecision.TotalFailure, 3, "NS.ClassC", "No structure data");
		LayoutFixStatistics.LogSummary();
	}

	[Test]
	public void Reset_ClearsAllCounters()
	{
		LayoutFixStatistics.RecordDecision(LayoutFixDecision.ReflectionSuccess, 1, "NS.ClassA");
		LayoutFixStatistics.RecordDecision(LayoutFixDecision.DegradedExport, 2, "NS.ClassB", "reason");
		LayoutFixStatistics.Reset();
		LayoutFixStatistics.LogSummary();
	}

	[Test]
	public void RecordDecision_ConcurrentAccess_ThreadSafe()
	{
		int threadCount = 10;
		int decisionsPerThread = 100;
		System.Threading.Tasks.Parallel.For(0, threadCount, threadId =>
		{
			for (int i = 0; i < decisionsPerThread; i++)
			{
				LayoutFixStatistics.RecordDecision(
					LayoutFixDecision.ReflectionSuccess,
					threadId * 1000L + i,
					$"NS{threadCount}.Class{i}");
			}
		});
		LayoutFixStatistics.LogSummary();
	}

	[Test]
	public void RecordDecision_EmptyScriptFullName_HandledGracefully()
	{
		LayoutFixStatistics.RecordDecision(LayoutFixDecision.TotalFailure, 1, "");
		LayoutFixStatistics.RecordDecision(LayoutFixDecision.TotalFailure, 2, "NoNamespace");
		LayoutFixStatistics.LogSummary();
	}
}