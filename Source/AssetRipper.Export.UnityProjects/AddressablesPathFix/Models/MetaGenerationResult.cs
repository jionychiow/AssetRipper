namespace AssetRipper.Export.UnityProjects.AddressablesPathFix.Models;

public sealed record MetaGenerationResult
{
	public required string MetaPath { get; init; }
	public required string Guid { get; init; }
	public required bool WasCreated { get; init; }
	public required bool WasSkipped { get; init; }
	public string? Error { get; init; }
}