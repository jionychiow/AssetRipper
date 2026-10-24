using AssetRipper.Tools.NaninovelAuditor.Session;

namespace AssetRipper.Tools.NaninovelAuditor.Modules;

/// <summary>
/// 审计模块统一接口：Execute 内部只读 GameData 与导出项目，产物统一落盘到 ReportDirectory。
/// </summary>
public interface INaninovelAuditModule
{
	/// <summary>模块标识（A~F 或冒烟值）。</summary>
	string ModuleName { get; }

	AuditRunResult Execute(AuditSession session);
}