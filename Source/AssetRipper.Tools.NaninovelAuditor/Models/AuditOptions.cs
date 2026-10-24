namespace AssetRipper.Tools.NaninovelAuditor.Models;

public sealed record AuditOptions
{
	public required string SourceDirectory { get; init; }
	public required string ExportedRoot { get; init; }
	public required string ReportDirectory { get; init; }
	public required IReadOnlyList<string> ModuleSelection { get; init; }
	public required string DecompDirectory { get; init; }
	public int SampleCount { get; init; } = 10;
	public string? UnityExecutablePath { get; init; }
	public string? UnityLogPath { get; init; }

	public bool IsModuleSelected(string module) => ModuleSelection.Contains("All", StringComparer.OrdinalIgnoreCase)
		|| ModuleSelection.Contains(module, StringComparer.OrdinalIgnoreCase);
}