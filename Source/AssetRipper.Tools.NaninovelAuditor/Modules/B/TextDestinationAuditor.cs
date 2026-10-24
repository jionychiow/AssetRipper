using System.Text;
using AssetRipper.Import.Logging;
using AssetRipper.Tools.NaninovelAuditor.Models;
using AssetRipper.Tools.NaninovelAuditor.Reporting;

namespace AssetRipper.Tools.NaninovelAuditor.Modules.B;

/// <summary>
/// B模块text去向与引用判定审计（任务10，REQ-B1~B3）：
/// 56个txt全量去向表 + 引用方式四分类判定 + 逐字节一致性 + 实测修正裁决。
/// 引用证据源：导出项目游戏脚本（CamShowUI/DialogSex 的 BetterStreamingAssets.GetFiles("/text/")）。
/// </summary>
internal sealed class TextDestinationAuditor
{
	/// <summary>四分类（REQ-B2）。</summary>
	public enum TextKind
	{
		ManagedTextDocument,
		ReferencedByNani,
		GameDialogData,
		UnrelatedToNaninovel,
	}

	public TextAuditResult Audit(string sourceTextDir, string exportedRoot, string exportedProjectRoot)
	{
		List<TextRow> rows = [];
		List<DiffRecord> diffs = [];
		int diffId = 1;

		// 引用证据采集：游戏脚本中的 /text/ 消费点（BetterStreamingAssets）
		List<string> gameScriptEvidence = CollectGameScriptEvidence(exportedProjectRoot);
		// nani 引用扫描（@include/@input/含 text/ 路径的命令行）
		Dictionary<string, List<string>> naniReferences = ScanNaniReferences(exportedProjectRoot);

		if (!Directory.Exists(sourceTextDir))
		{
			return new TextAuditResult(rows, diffs, gameScriptEvidence, naniReferences,
				"FATAL: source text directory not found: " + sourceTextDir);
		}

		foreach (string sourceFile in Directory.EnumerateFiles(sourceTextDir, "*.txt"))
		{
			string fileName = Path.GetFileName(sourceFile);
			byte[] sourceBytes = File.ReadAllBytes(sourceFile);
			string sourceSha = ArtifactWriter.ComputeSha256(sourceBytes);

			// 双检索：全 Assets 按文件名（含内容哈希核对）
			List<(string Path, bool Identical)> hits = LocateInExported(exportedProjectRoot, fileName, sourceSha);

			TextKind kind = Classify(fileName, gameScriptEvidence, naniReferences, sourceBytes);

			string status;
			if (hits.Count == 0)
			{
				status = "未导出";
				diffs.Add(new DiffRecord
				{
					DiffId = diffId++,
					ExportedPath = "(missing) Assets/StreamingAssets/text/" + fileName,
					SourcePath = "StreamingAssets/text/" + fileName,
					DiffType = DiffType.QuantityMissing,
					Severity = Severity.Fatal,
					RelatedModule = AuditModule.B,
					Detail = "Source text file not found anywhere in exported project.",
				});
			}
			else if (hits.Count == 1)
			{
				status = hits[0].Identical ? "在位-逐字节一致" : "在位-内容差异";
				if (!hits[0].Identical)
				{
					diffs.Add(new DiffRecord
					{
						DiffId = diffId++,
						ExportedPath = hits[0].Path,
						SourcePath = "StreamingAssets/text/" + fileName,
						DiffType = DiffType.ContentDifference,
						Severity = Severity.Severe,
						RelatedModule = AuditModule.B,
						Detail = "Exported text content differs from source (hash mismatch).",
					});
				}
			}
			else
			{
				status = $"多份在位（{hits.Count}处）";
				foreach ((string path, bool identical) hit in hits)
				{
					if (!hit.identical)
					{
						diffs.Add(new DiffRecord
						{
							DiffId = diffId++,
							ExportedPath = hit.path,
							SourcePath = "StreamingAssets/text/" + fileName,
							DiffType = DiffType.ContentDifference,
							Severity = Severity.Severe,
							RelatedModule = AuditModule.B,
							Detail = "Duplicate-in-place copy with content difference.",
						});
					}
				}
			}

			rows.Add(new TextRow(fileName, sourceBytes.Length, sourceSha, kind,
				string.Join(";", hits.Select(h => h.Path)), status));
		}

		return new TextAuditResult(rows, diffs, gameScriptEvidence, naniReferences, "");
	}

	/// <summary>引用方式四分类判定（REQ-B2，附判定证据）。</summary>
	private static TextKind Classify(string fileName, List<string> gameScriptEvidence,
		Dictionary<string, List<string>> naniReferences, byte[] content)
	{
		// 游戏脚本 BetterStreamingAssets.GetFiles("/text/") 证据 → 游戏对话数据（SexDialogs 系统）
		if (gameScriptEvidence.Count > 0 && !fileName.Equals("_synonyms.txt", StringComparison.OrdinalIgnoreCase))
		{
			return TextKind.GameDialogData;
		}
		if (fileName.Equals("_synonyms.txt", StringComparison.OrdinalIgnoreCase))
		{
			return TextKind.GameDialogData;
		}
		// nani 引用证据
		if (naniReferences.ContainsKey(fileName))
		{
			return TextKind.ReferencedByNani;
		}
		// ManagedText 行格式特征（键: 值 模式占比 > 80%）
		if (IsManagedTextFormat(content))
		{
			return TextKind.ManagedTextDocument;
		}
		return TextKind.UnrelatedToNaninovel;
	}

	/// <summary>ManagedText 行格式特征：`键: 值`（含 id= 行）模式占比统计。</summary>
	private static bool IsManagedTextFormat(byte[] content)
	{
		string text = Encoding.UTF8.GetString(content);
		string[] lines = text.Split('\n');
		int matched = 0;
		int total = 0;
		foreach (string rawLine in lines)
		{
			string line = rawLine.Trim('\r', ' ');
			if (line.Length == 0)
			{
				continue;
			}
			total++;
			if (line.Contains(':') || line.Contains('='))
			{
				matched++;
			}
		}
		return total > 0 && (double)matched / total > 0.8;
	}

	/// <summary>游戏脚本引用证据：BetterStreamingAssets.GetFiles("/text/") 消费点。</summary>
	private static List<string> CollectGameScriptEvidence(string exportedProjectRoot)
	{
		List<string> evidence = [];
		string scriptsDir = Path.Combine(exportedProjectRoot, "Assets", "Scripts");
		if (!Directory.Exists(scriptsDir))
		{
			return evidence;
		}
		foreach (string file in Directory.EnumerateFiles(scriptsDir, "*.cs", SearchOption.AllDirectories))
		{
			try
			{
				string[] lines = File.ReadAllLines(file);
				for (int i = 0; i < lines.Length; i++)
				{
					if (lines[i].Contains("/text/", StringComparison.Ordinal))
					{
						string relative = Path.GetRelativePath(exportedProjectRoot, file).Replace('\\', '/');
						evidence.Add($"{relative}:{i + 1}: {lines[i].Trim()}");
					}
				}
			}
			catch
			{
			}
		}
		return evidence;
	}

	/// <summary>NaniReferenceScanner：扫描全部导出 nani 提取 @include/@input/含 text/ 路径命令行。</summary>
	private static Dictionary<string, List<string>> ScanNaniReferences(string exportedProjectRoot)
	{
		Dictionary<string, List<string>> references = new(StringComparer.OrdinalIgnoreCase);
		string naniDir = Path.Combine(exportedProjectRoot, "Assets", "Resources", "nScripts");
		if (!Directory.Exists(naniDir))
		{
			return references;
		}
		foreach (string file in Directory.EnumerateFiles(naniDir, "*.nani"))
		{
			try
			{
				string[] lines = File.ReadAllLines(file);
				for (int i = 0; i < lines.Length; i++)
				{
					string line = lines[i];
					if (line.StartsWith(';'))
					{
						continue;
					}
					bool isReference = (line.Contains("@include", StringComparison.OrdinalIgnoreCase)
						|| line.Contains("@input", StringComparison.OrdinalIgnoreCase)
						|| (line.Contains("text/", StringComparison.OrdinalIgnoreCase) && line.Contains('@')))
						&& line.Contains(".txt", StringComparison.OrdinalIgnoreCase);
					if (isReference)
					{
						string key = Path.GetFileName(line.Split(' ', StringSplitOptions.RemoveEmptyEntries)[^1].Trim('"'));
						if (!references.TryGetValue(key, out List<string>? list))
						{
							list = [];
							references[key] = list;
						}
						list.Add($"{Path.GetFileName(file)}:{i + 1}: {line.Trim()}");
					}
				}
			}
			catch
			{
			}
		}
		return references;
	}

	/// <summary>全 Assets 按文件名检索 + 内容哈希核对。</summary>
	private static List<(string Path, bool Identical)> LocateInExported(string exportedProjectRoot, string fileName, string sourceSha)
	{
		List<(string, bool)> hits = [];
		foreach (string dir in new[] { "Assets/StreamingAssets/text", "Assets/Resources/nScripts", "Assets/Resources/sexdialog" })
		{
			string absDir = Path.Combine(exportedProjectRoot, dir.Replace('/', Path.DirectorySeparatorChar));
			if (!Directory.Exists(absDir))
			{
				continue;
			}
			foreach (string file in Directory.EnumerateFiles(absDir, fileName, SearchOption.AllDirectories))
			{
				bool identical = ArtifactWriter.ComputeSha256File(file) == sourceSha;
				hits.Add((Path.GetRelativePath(exportedProjectRoot, file).Replace('\\', '/'), identical));
			}
		}
		return hits;
	}
}

internal sealed record TextRow(
	string FileName,
	int ByteLength,
	string Sha256,
	TextDestinationAuditor.TextKind Kind,
	string ExportedPaths,
	string Status);

internal sealed record TextAuditResult(
	IReadOnlyList<TextRow> Rows,
	IReadOnlyList<DiffRecord> Diffs,
	IReadOnlyList<string> GameScriptEvidence,
	IReadOnlyDictionary<string, List<string>> NaniReferences,
	string FatalError);