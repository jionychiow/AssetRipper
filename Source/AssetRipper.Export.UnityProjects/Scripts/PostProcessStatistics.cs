namespace AssetRipper.Export.UnityProjects.Scripts;

public sealed class PostProcessStatistics
{
	public int ProcessedFileCount { get; private set; }
	public int FixedFileCount { get; private set; }
	public Dictionary<string, int> FixRecordsByRule { get; } = new();

	public void AddProcessedFile()
	{
		ProcessedFileCount++;
	}

	public void AddFix(string ruleName)
	{
		FixedFileCount++;
		FixRecordsByRule[ruleName] = FixRecordsByRule.GetValueOrDefault(ruleName) + 1;
	}

	public void Merge(PostProcessStatistics other)
	{
		ProcessedFileCount += other.ProcessedFileCount;
		FixedFileCount += other.FixedFileCount;
		foreach ((string ruleName, int count) in other.FixRecordsByRule)
		{
			FixRecordsByRule[ruleName] = FixRecordsByRule.GetValueOrDefault(ruleName) + count;
		}
	}
}