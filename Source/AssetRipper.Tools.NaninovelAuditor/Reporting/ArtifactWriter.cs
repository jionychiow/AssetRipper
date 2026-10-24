using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AssetRipper.Tools.NaninovelAuditor.Reporting;

/// <summary>
/// 审计产物落盘器：JSON（source-generation 序列化，AOT 友好）+ CSV 双写、目录自动创建、超长片段强制截断。
/// </summary>
public static class ArtifactWriter
{
	public const int MaxFragmentLength = 200;

	public static void WriteJson<T>(string path, T value)
	{
		EnsureParentDirectory(path);
		string json = JsonSerializer.Serialize(value, typeof(T), AuditorSerializerContext.Default);
		File.WriteAllText(path, json, Encoding.UTF8);
	}

	public static T ReadJson<T>(string path)
	{
		EnsureFileExists(path);
		string json = File.ReadAllText(path, Encoding.UTF8);
		return (T)(JsonSerializer.Deserialize(json, typeof(T), AuditorSerializerContext.Default)
			?? throw new InvalidOperationException($"Failed to deserialize {typeof(T).Name} from {path}"));
	}

	public static void WriteCsv(string path, string[] header, IEnumerable<string[]> rows)
	{
		EnsureParentDirectory(path);
		StringBuilder sb = new();
		AppendCsvLine(sb, header);
		foreach (string[] row in rows)
		{
			if (row.Length != header.Length)
			{
				throw new InvalidOperationException($"CSV row column count {row.Length} != header count {header.Length}");
			}
			AppendCsvLine(sb, row);
		}
		File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
	}

	/// <summary>差异片段截断（spec 4.3.2 版权约束：单行命令级）。</summary>
	public static string TruncateFragment(string fragment)
	{
		if (string.IsNullOrEmpty(fragment))
		{
			return string.Empty;
		}
		return fragment.Length <= MaxFragmentLength ? fragment : fragment[..MaxFragmentLength] + "…[truncated]";
	}

	public static string ComputeSha256(Stream stream)
	{
		stream.Position = 0;
		using System.Security.Cryptography.SHA256 sha = System.Security.Cryptography.SHA256.Create();
		return Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
	}

	public static string ComputeSha256(byte[] data)
	{
		using System.Security.Cryptography.SHA256 sha = System.Security.Cryptography.SHA256.Create();
		return Convert.ToHexString(sha.ComputeHash(data)).ToLowerInvariant();
	}

	public static string ComputeSha256File(string path)
	{
		using FileStream stream = File.OpenRead(path);
		return ComputeSha256(stream);
	}

	private static void AppendCsvLine(StringBuilder sb, string[] fields)
	{
		for (int i = 0; i < fields.Length; i++)
		{
			if (i > 0)
			{
				sb.Append(',');
			}
			sb.Append(EscapeCsvField(fields[i] ?? string.Empty));
		}
		sb.AppendLine();
	}

	private static string EscapeCsvField(string field)
	{
		if (field.Contains(',') || field.Contains('"') || field.Contains('\n') || field.Contains('\r'))
		{
			return "\"" + field.Replace("\"", "\"\"") + "\"";
		}
		return field;
	}

	private static void EnsureParentDirectory(string path)
	{
		string? dir = Path.GetDirectoryName(Path.GetFullPath(path));
		if (!string.IsNullOrEmpty(dir))
		{
			Directory.CreateDirectory(dir);
		}
	}

	private static void EnsureFileExists(string path)
	{
		if (!File.Exists(path))
		{
			throw new FileNotFoundException($"Audit artifact not found: {path}");
		}
	}
}

[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(Models.AuditOptions))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(Models.SourceInventory))]
[JsonSerializable(typeof(Models.SourceInventoryEntry))]
[JsonSerializable(typeof(Models.ExportedInventory))]
[JsonSerializable(typeof(Models.ExportedInventoryEntry))]
[JsonSerializable(typeof(Models.DiffRecord))]
[JsonSerializable(typeof(List<Models.DiffRecord>))]
[JsonSerializable(typeof(Models.DiagnosticEntry))]
[JsonSerializable(typeof(List<Models.DiagnosticEntry>))]
[JsonSerializable(typeof(Models.RepairPlanItem))]
[JsonSerializable(typeof(List<Models.RepairPlanItem>))]
[JsonSerializable(typeof(Models.ModificationSnapshot))]
[JsonSerializable(typeof(Models.FileIdRecord))]
[JsonSerializable(typeof(Models.AuditConclusion))]
[JsonSerializable(typeof(Models.ModuleCoverageEntry))]
[JsonSerializable(typeof(Models.QuantityReconciliationEntry))]
[JsonSerializable(typeof(Models.RootCauseVerdict))]
[JsonSerializable(typeof(Models.NaniScriptId))]
[JsonSerializable(typeof(Models.NaniContentDiff))]
[JsonSerializable(typeof(Models.MinimalDiffFragment))]
[JsonSerializable(typeof(Models.DuplicateOriginTrace))]
[JsonSerializable(typeof(List<Models.DuplicateOriginTrace>))]
[JsonSerializable(typeof(Models.PathIdMap))]
[JsonSerializable(typeof(Models.PathIdMapFile))]
[JsonSerializable(typeof(Models.PathIdMapEntry))]
internal sealed partial class AuditorSerializerContext : JsonSerializerContext
{
}