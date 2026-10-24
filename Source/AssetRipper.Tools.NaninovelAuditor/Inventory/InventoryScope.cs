namespace AssetRipper.Tools.NaninovelAuditor.Inventory;

/// <summary>审计作用域内的固定路径与命名约定。</summary>
internal static class InventoryScope
{
	public const string TargetScriptClassName = "Script";
	public const string TargetScriptNamespace = "Naninovel";
	public const string TargetScriptAssembly = "Elringus.Naninovel.Runtime";

	/// <summary>原始数据中文件系统 nani 脚本目录（相对于 SourceDirectory）。</summary>
	public const string RelativeFilesystemScriptsDir = "resources/nScripts";

	/// <summary>导出项目 TextAsset 输出根（相对于 ExportedRoot）。</summary>
	public const string ExportedProjectName = "ExportedProject";

	/// <summary>导出 nani 输出目录（相对于导出项目根）。</summary>
	public const string ExportedScriptsRelativeDir = "Assets/Resources/nScripts";

	/// <summary>path_id_map.json 位置（相对于 ExportedRoot）。</summary>
	public const string PathIdMapRelativePath = "AuxiliaryFiles/path_id_map.json";

	public static string GetExportedProjectRoot(string exportedRoot) => Path.Combine(exportedRoot, ExportedProjectName);

	public static string GetExportedScriptsDir(string exportedRoot) => Path.Combine(exportedRoot, ExportedProjectName, ExportedScriptsRelativeDir.Replace('/', Path.DirectorySeparatorChar));
}