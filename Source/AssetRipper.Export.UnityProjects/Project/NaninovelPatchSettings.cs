namespace AssetRipper.Export.UnityProjects.Project;

public sealed class NaninovelPatchSettings
{
	public bool EnableSpriteRendererPatch { get; init; } = true;
	public bool EnableUniRxAsyncPatch { get; init; } = false;
	public bool EnableAspectCorrection { get; init; } = false;
	public bool EnableRenderStateIsolation { get; init; } = false;
	public bool EnableBlurIntensity { get; init; } = false;
	public bool LogMethodBodySummary { get; init; } = false;
	public bool EnableIdempotencyCheck { get; init; } = true;
	public bool EnableUIManagerInstantiatePatch { get; init; } = true;
	public bool EnableScriptPlayerWaitPatch { get; init; } = false;
	public bool EnableRenderOrderFix { get; init; } = true;
	public int TargetCanvasSortingOrder { get; init; } = 100;
	public bool EnableRuntimeRenderOrderFixer { get; init; } = true;
	public bool EnableRenderOrderDiagnostic { get; init; } = true;
	public bool EnableShaderIncludeFix { get; init; } = true;
	public bool EnableAudioListenerFix { get; init; } = true;
}