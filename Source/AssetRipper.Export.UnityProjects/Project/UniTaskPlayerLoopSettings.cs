namespace AssetRipper.Export.UnityProjects.Project;

public sealed class UniTaskPlayerLoopSettings
{
	public bool EnableEditorScriptGeneration { get; init; } = true;
	public bool EnableUniRxAsyncILPatching { get; init; } = false;
	public string EditorScriptFileName { get; init; } = "UniTaskPlayerLoopInitializer.cs";
}