namespace AssetRipper.Import;

public enum LayoutFixDecision
{
	SourceGeneratedSuccess,
	ReflectionSuccess,
	SourceGeneratedFallbackToReflection,
	ReflectionOnly,
	DegradedExport,
	TotalFailure,
}