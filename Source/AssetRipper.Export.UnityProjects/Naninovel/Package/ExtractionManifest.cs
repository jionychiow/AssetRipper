namespace AssetRipper.Export.UnityProjects.Naninovel.Package;

/// <summary>
/// Configuration manifest specifying which entries to extract from the official Naninovel unitypackage
/// and which DLLs are superseded by source compilation or retained as precompiled.
/// </summary>
public sealed class ExtractionManifest
{
	/// <summary>
	/// Path prefix for Runtime source files in the unitypackage. Default: "Assets/Naninovel/Runtime/"
	/// </summary>
	public string RuntimeSourcePrefix { get; set; } = "Assets/Naninovel/Runtime/";

	/// <summary>
	/// Path prefix for Editor source files in the unitypackage. Default: "Assets/Naninovel/Editor/"
	/// </summary>
	public string EditorSourcePrefix { get; set; } = "Assets/Naninovel/Editor/";

	/// <summary>
	/// General prefix for all Naninovel assets (Plugins, ThirdParty, shaders, prefabs, etc.)
	/// </summary>
	public string NaninovelAssetPrefix { get; set; } = "Assets/Naninovel/";

	/// <summary>
	/// File extensions that should be extracted from the unitypackage.
	/// </summary>
	public IReadOnlySet<string> ExtractedExtensions { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
	{
		".cs", ".asmdef", ".shader", ".cginc", ".hlsl", ".prefab", ".mat", ".png", ".tga", ".json", ".asset", ".txt", ".dll", ".meta"
	};

	/// <summary>
	/// Pathname patterns to exclude from extraction (supports wildcards).
	/// </summary>
	public IReadOnlyList<string> ExcludePatterns { get; set; } = ["*Tests*", "*Examples*", "*.pdf", "*Documentation*"];

	/// <summary>
	/// DLLs that are superseded by source compilation or official package and should be deleted from Assets/Plugins/.
	/// </summary>
	public IReadOnlyList<string> SupersededDlls { get; set; } =
	[
		"UniRx.Async.dll",
		"Elringus.Naninovel.Runtime.dll",
		"Elringus.Naninovel.Editor.dll",
		"Naninovel.NCalc.dll",

		"NLayer.dll"
	];

	/// <summary>
	/// DLLs that have no source code equivalent and must be retained as precompiled.
	/// </summary>
	public IReadOnlyList<string> RetainedDlls { get; set; } =
	[
		"Naninovel.Common.dll",
		"Naninovel.NCalc.dll",
		"Naninovel.Lexing.dll",
		"Naninovel.Parsing.dll",
		"Naninovel.WebSocketSharp.dll",
		"NLayer.dll"
	];

	/// <summary>
	/// Creates a default manifest configured for Naninovel v1.18.1.
	/// </summary>
	public static ExtractionManifest CreateDefault() => new();
}