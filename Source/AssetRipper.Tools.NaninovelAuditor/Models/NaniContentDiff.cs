namespace AssetRipper.Tools.NaninovelAuditor.Models;

public sealed record NaniScriptId
{
	/// <summary>基础脚本名（剥离 _数字 后缀、去扩展名）。</summary>
	public required string BaseName { get; init; }
	/// <summary>导出侧文件全路径（本体或副本之一），用于唯一定位一条对比记录。</summary>
	public required string ExportedFilePath { get; init; }
}

public sealed record NaniContentDiff
{
	public required NaniScriptId ScriptId { get; init; }
	public required ContentMatchLevel MatchLevel { get; init; }
	public required IReadOnlyList<MinimalDiffFragment> Fragments { get; init; }
	/// <summary>规范化降级原因（MatchLevel == NormalizedIdentical 时必填）。</summary>
	public string NormalizationReason { get; init; } = "";
}

/// <summary>最小差异片段（spec 4.3.2 版权约束：单行命令级，超长强制截断）。</summary>
public sealed record MinimalDiffFragment
{
	public required int SourceLine { get; init; }
	public required int ExportedLine { get; init; }
	public required string SourceFragment { get; init; }
	public required string ExportedFragment { get; init; }
}