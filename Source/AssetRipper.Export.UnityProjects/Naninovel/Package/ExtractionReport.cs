namespace AssetRipper.Export.UnityProjects.Naninovel.Package;

/// <summary>
/// Status of a single extraction operation.
/// </summary>
public enum ExtractionStatus
{
	Succeeded,
	Skipped,
	Failed,
}

/// <summary>
/// Record of a single file extraction operation.
/// </summary>
/// <param name="PathName">The source pathname in the unitypackage or zip.</param>
/// <param name="TargetPath">The target path in the export project.</param>
/// <param name="Status">The result status of the extraction.</param>
/// <param name="Md5">The MD5 hash of the extracted file, or null if not computed.</param>
/// <param name="ErrorMessage">Error message if the extraction failed, or null.</param>
public sealed record ExtractionRecord(string PathName, string TargetPath, ExtractionStatus Status, string? Md5 = null, string? ErrorMessage = null);

/// <summary>
/// Aggregated report of all extraction operations.
/// </summary>
public sealed class ExtractionReport
{
	private readonly List<ExtractionRecord> _records = [];

	public int SucceededCount { get; private set; }
	public int SkippedCount { get; private set; }
	public int FailedCount { get; private set; }
	public IReadOnlyList<ExtractionRecord> Records => _records;

	public void Add(ExtractionRecord record)
	{
		_records.Add(record);
		switch (record.Status)
		{
			case ExtractionStatus.Succeeded:
				SucceededCount++;
				break;
			case ExtractionStatus.Skipped:
				SkippedCount++;
				break;
			case ExtractionStatus.Failed:
				FailedCount++;
				break;
		}
	}

	/// <summary>
	/// Merges multiple reports into a single combined report.
	/// </summary>
	public static ExtractionReport Merge(params ExtractionReport[] reports)
	{
		ExtractionReport merged = new();
		foreach (ExtractionReport report in reports)
		{
			foreach (ExtractionRecord record in report.Records)
			{
				merged.Add(record);
			}
		}
		return merged;
	}
}