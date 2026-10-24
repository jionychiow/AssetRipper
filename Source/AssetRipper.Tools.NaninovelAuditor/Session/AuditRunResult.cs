namespace AssetRipper.Tools.NaninovelAuditor.Session;

/// <summary>单模块审计执行结果。</summary>
public sealed record AuditRunResult
{
	public required string ModuleName { get; init; }
	public bool Success { get; init; }
	public string Summary { get; init; } = "";
	public IReadOnlyList<string> ProducedArtifacts { get; init; } = [];
}

public sealed class ModuleAuditException : Exception
{
	public string ModuleName { get; }

	public ModuleAuditException(string moduleName, string message, Exception? innerException = null)
		: base($"[{moduleName}] {message}", innerException)
	{
		ModuleName = moduleName;
	}
}