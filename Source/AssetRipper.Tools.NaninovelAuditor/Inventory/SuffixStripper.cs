using System.Text.RegularExpressions;

namespace AssetRipper.Tools.NaninovelAuditor.Inventory;

/// <summary>
/// `_数字` 后缀剥离：正则与存量 NaninovelResourceDedupPostExporter.SuffixRegex 完全同源
/// （来源：Source/AssetRipper.Export.UnityProjects/Naninovel/Dedup/NaninovelResourceDedupPostExporter.cs:14，
/// 字面量 _(\\d+)(\\.[^.]+)$）。口径偏差即审计缺陷（tasks.md 全局约束 10）。
/// </summary>
internal static partial class SuffixStripper
{
	[GeneratedRegex(@"_(\d+)(\.[^.]+)$", RegexOptions.Compiled)]
	private static partial Regex SuffixRegex();

	/// <summary>剥离文件名末尾的 _数字 后缀，返回 (基础名, 副本序号或null)。</summary>
	public static (string BaseName, int? DuplicateIndex) Strip(string fileName)
	{
		Match match = SuffixRegex().Match(fileName);
		if (match.Success)
		{
			string indexPart = match.Groups[1].Value;
			string extension = match.Groups[2].Value;
			string basePart = fileName[..^match.Length];
			return (basePart + extension, int.Parse(indexPart));
		}
		return (fileName, null);
	}
}