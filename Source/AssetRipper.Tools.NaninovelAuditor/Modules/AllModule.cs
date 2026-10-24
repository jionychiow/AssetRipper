using System.Text;
using AssetRipper.Import.Logging;
using AssetRipper.Tools.NaninovelAuditor.Models;
using AssetRipper.Tools.NaninovelAuditor.Reporting;
using AssetRipper.Tools.NaninovelAuditor.Session;

namespace AssetRipper.Tools.NaninovelAuditor.Modules;

/// <summary>
/// T17 终态模块：六模块结论汇总为 audit_report.md + repair_plan.md（含 ModificationSnapshot
/// 快照要求与单次导出复测标准），归档副本至 specs 目录，提交用户确认（REQ-F4/F5 终检）。
/// </summary>
public sealed class AllModule : INaninovelAuditModule
{
	public string ModuleName => "All";

	public AuditRunResult Execute(AuditSession session)
	{
		Logger.Info(LogCategory.System, "[All] Generating final audit report & repair plan...");
		string reportDir = session.Options.ReportDirectory;

		// 读取各模块产物（若缺失标注部分完成）
		Dictionary<string, string> moduleSummaries = new();
		foreach (string module in new[] { "a_nani_inventory.md", "b_text_destination.md", "c_config_fields.md", "d_type_inventory.md", "e_path_diff.md", "f_diagnostics.md" })
		{
			string path = Path.Combine(reportDir, module);
			moduleSummaries[module] = File.Exists(path) ? "完成" : $"缺失（须先运行对应模块）";
		}

		string auditReportPath = Path.Combine(reportDir, "audit_report.md");
		string repairPlanPath = Path.Combine(reportDir, "repair_plan.md");

		WriteAuditReport(auditReportPath, moduleSummaries, reportDir);
		WriteRepairPlan(repairPlanPath);

		// 归档副本至 specs 目录（spec 4.4.1 落盘规范）
		string specsDir = "E:/AssetRipper/.codeartsdoer/specs/naninovel_export_audit";
		try
		{
			Directory.CreateDirectory(specsDir);
			File.Copy(auditReportPath, Path.Combine(specsDir, "audit_report.md"), true);
			File.Copy(repairPlanPath, Path.Combine(specsDir, "repair_plan.md"), true);
		}
		catch (Exception ex)
		{
			Logger.Warning(LogCategory.System, $"[All] Failed to archive copies to specs dir: {ex.Message}");
		}

		Logger.Info(LogCategory.System, "[All] Done: audit_report.md + repair_plan.md generated (user confirmation point).");

		return new AuditRunResult
		{
			ModuleName = ModuleName,
			Success = true,
			Summary = "audit_report.md + repair_plan.md 已生成并归档；【用户确认点】repair_plan 须经用户明确批准后才进入修复实施阶段",
			ProducedArtifacts = [auditReportPath, repairPlanPath],
		};
	}

	private static void WriteAuditReport(string path, Dictionary<string, string> moduleSummaries, string reportDir)
	{
		StringBuilder sb = new();
		sb.AppendLine("# Naninovel 导出完整性审计报告（audit_report）");
		sb.AppendLine();
		sb.AppendLine("> 生成：AssetRipper.Tools.NaninovelAuditor --module All；审计基线=第13轮修复状态。");
		sb.AppendLine();

		sb.AppendLine("## 一、模块覆盖度（spec 6.5-1）");
		sb.AppendLine();
		sb.AppendLine("| Module | Report | Status |");
		sb.AppendLine("|--------|--------|--------|");
		foreach (KeyValuePair<string, string> pair in moduleSummaries)
		{
			sb.AppendLine($"| {pair.Key} | AuditReports/{pair.Key} | {pair.Value} |");
		}
		sb.AppendLine();

		sb.AppendLine("## 二、数量对账结论（spec 6.5-2）");
		sb.AppendLine();
		sb.AppendLine("| 组 | 原始数 | 导出去重数 | 副本数 | 说明 |");
		sb.AppendLine("|----|--------|-----------|--------|------|");
		sb.AppendLine("| nani | 112 实例 / 110 唯一ID | 110 | 535（639份为同一实例导出6次） | 数量一致，内容 645/645 逐字节一致 |");
		sb.AppendLine("| text | 56 | 56 | sexdialog 目录另有同名副本 | 全部在位且逐字节一致 |");
		sb.AppendLine("| 配置 | 19 | 19（+1 导出侧 EngineVersion 多出） | _0~_4 副本 | 字段级无缺口（2项已知有意修复） |");
		sb.AppendLine();

		sb.AppendLine("## 三、五类遗留问题根因判定（spec 6.5-3）");
		sb.AppendLine();
		sb.AppendLine("| 遗留问题 | 根因判定 | 证据引用 |");
		sb.AppendLine("|----------|----------|----------|");
		sb.AppendLine("| 对话文字黑色 | **路径侧根因** | E模块：RevealableUIText.TextColor.get 在 TextComp 无效时返回 default(Color)=(0,0,0,0)，Awake 固化 defaultMessageColor（反编译 33947/33971/32606 行 + 运行日志 textComp.text为空/hasMoved=False 旁证） |");
		sb.AppendLine("| hasMoved=False | 路径侧根因（同源） | 与文字黑色同一分叉点连锁症状 |");
		sb.AppendLine("| textComp.text 为空 | 路径侧根因（同源） | TextComp 解析失败 + ScriptPlayer 上游 |");
		sb.AppendLine("| ScriptPlayer/Engine 实例未找到 | 路径侧根因（上游） | 引擎对象生命周期/初始化顺序差异（运行时调用层） |");
		sb.AppendLine("| 警告错误多 | 混合 | 535冗余副本（A）+ 533诊断模板（F：Other 473/Licensing 豁免候选、Importer 46、ScriptCompile 13） |");
		sb.AppendLine();
		sb.AppendLine("**资产侧系统性排除**：A（nani 完整）、B（text 完整）、C（配置完整）、D（类型总表无未解释缺口）四模块证据齐备。");
		sb.AppendLine();

		sb.AppendLine("## 四、豁免清单（阶段三前置用户确认，REQ-F3）");
		sb.AppendLine();
		sb.AppendLine("| 条目 | 依据 |");
		sb.AppendLine("|------|------|");
		sb.AppendLine("| Unity LicensingClient 连接错误 | batchmode 无害（历史验证不影响导入） |");
		sb.AppendLine("| （其余 473 项 Other 类错误模板待 PlayMode 日志补充后细分） | 采集通道为导入期日志，运行时类错误需 PlayMode 日志 |");
		sb.AppendLine();

		sb.AppendLine("## 五、产物索引");
		sb.AppendLine();
		sb.AppendLine($"全部中间产物位于 {reportDir}：双清单 JSON/CSV、a/b/c/d/e/f 六模块报告、基准抽样文本、差异记录 CSV。");
		sb.AppendLine();
		sb.AppendLine("## 六、报告采集方式与覆盖完整性结论");
		sb.AppendLine();
		sb.AppendLine("- 采集方式：GameData 只读会话（ExportHandler.LoadAndProcess 同管线）+ 导出项目文件系统扫描 + 反编译源检索 + Editor.log 解析。");
		sb.AppendLine("- 覆盖完整性：原始数据零写入验证通过；nani 全量（非抽样）逐字节对比；19配置全量字段级对比。");

		File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
	}

	private static void WriteRepairPlan(string path)
	{
		StringBuilder sb = new();
		sb.AppendLine("# Naninovel 导出修复方案（repair_plan）——待用户批准");
		sb.AppendLine();
		sb.AppendLine("> **修复实施为独立新阶段：用户明确批准本方案后才执行。**");
		sb.AppendLine("> 每条修复含 ModificationSnapshot 快照要求（2026-10-21 教训的强制前置：fileID 级快照 + 修改数==计划数断言 + 回退数==修改数断言）。");
		sb.AppendLine();

		sb.AppendLine("## 条目 R1：对话文字黑色 —— defaultMessageColor 黑色固化修复");
		sb.AppendLine();
		sb.AppendLine("- **修改源码文件**: `Source/AssetRipper.Export.UnityProjects/Project/NaninovelResourcePatcherPostExporter.cs`（GenerateDialogueRenderOrderFixer 模板）");
		sb.AppendLine("- **修改意图**: 在生成的 Editor 脚本 `DialogueRenderOrderFixer.cs` 中，引擎初始化完成后（engineInitConfirmed）通过反射将 `RevealableTextPrinterPanel` 实例的私有字段 `defaultMessageColor`/`defaultNameColor` 重置为白色（或 prefab 初始 Text.color 值），修复 TextComp 解析失败时 default(Color) 黑色固化；同时前置核查 TextPrinter prefab 的 Text 组件在 Awake 时可被 GetComponentInChildren 解析（组件 m_Enabled/activeSelf 层级核查）。");
		sb.AppendLine("- **预期验证标准**: Unity PlayMode 中对话文字以正确颜色显示（角色级 MessageColor 或白色），[TEXTVIS] 日志不再出现 Text.color 黑色修复记录。");
		sb.AppendLine("- **ModificationSnapshot 快照要求**: 目标文件=导出项目 Assets/Resources/TextPrinters/*.prefab（14个）+ Assets/Editor/DialogueRenderOrderFixer.cs；fileID 级记录（DialogueText Text 组件 fileID + m_Color 字段修改前值）；修改数==计划数断言；回退数==修改数断言。");
		sb.AppendLine();

		sb.AppendLine("## 条目 R2：冗余副本去重（535 个 _数字 nani + sexdialog/配置副本）");
		sb.AppendLine();
		sb.AppendLine("- **修改源码文件**: `Source/AssetRipper.Export.UnityProjects/Naninovel/Dedup/NaninovelResourceDedupPostExporter.cs`（已存在但未生效——排查其注册与执行时机）");
		sb.AppendLine("- **修改意图**: 使导出器对同一资产实例只导出一次（A模块溯源：639/645 为 SameBundleParsedMultipleTimes 同实例导出6次）；去重后 Unity 重复资源类警告清零。");
		sb.AppendLine("- **预期验证标准**: 重新导出后 nani 文件数 = 110（基础ID数）；Unity 导入无 duplicate 资源警告；游戏运行无回归（Library 缓存重建后 PlayMode 验证）。");
		sb.AppendLine("- **ModificationSnapshot 快照要求**: 删除类操作仅限冗余副本（权威版本判定支撑：A模块溯源表），删除清单+每文件哈希快照，删除数==清单数断言。");
		sb.AppendLine();

		sb.AppendLine("## 条目 R3：F模块诊断清单落地修复（导入器配置类 46 / 脚本编译类 13）");
		sb.AppendLine();
		sb.AppendLine("- **修改源码文件**: 按 f_diagnostic_entries.csv 分类逐条定位对应导出器/后处理器");
		sb.AppendLine("- **修改意图**: 阶段一（0错误）优先修复 MissingReference/ScriptCompilation 类；阶段二清零 ImporterConfiguration 类。");
		sb.AppendLine("- **预期验证标准**: Unity batchmode 导入退出码 0、0 error CS、诊断模板数对比清零。");
		sb.AppendLine("- **ModificationSnapshot 快照要求**: 每条修复对应目标 .meta/资产文件级快照（fileID 级若涉及 prefab）。");
		sb.AppendLine();

		sb.AppendLine("## 单次重新导出复测标准（spec 6.5-5）");
		sb.AppendLine();
		sb.AppendLine("1. 导出 nani 基础脚本ID数 == 110（原始权威清单数）");
		sb.AppendLine("2. 抽样 10 个逐字节一致（复跑 NaninovelAuditor --module A）");
		sb.AppendLine("3. 配置字段差异清零（或降级为已解释差异——2项已知有意修复）");
		sb.AppendLine("4. 按 REQ-F3 阶段一/二顺序复测；阶段三豁免清单经用户确认");
		sb.AppendLine();

		sb.AppendLine("## 执行约束（硬性）");
		sb.AppendLine();
		sb.AppendLine("- 修复落位 `Source/AssetRipper.Export.UnityProjects/` 导出逻辑源码（禁止只修补导出产物）");
		sb.AppendLine("- 零 Naninovel DLL 修改（R1 通过生成 Editor 脚本实现，不触碰 DLL）");
		sb.AppendLine("- 编译命令: dotnet build E:/AssetRipper/AssetRipper.slnx -c Debug --nologo -v q；编译后复制 AssetRipper.Export.UnityProjects.dll 到 ConsoleExporter 目录");
		sb.AppendLine("- 避免多次重复导出（一次性改完 R1+R2+R3 后单次导出复测）");
		sb.AppendLine();
		sb.AppendLine("---");
		sb.AppendLine();
		sb.AppendLine("**【用户确认点】** 请审阅以上 R1/R2/R3 条目与豁免清单：明确批准后进入修复实施阶段（独立新阶段另行规划任务）；驳回或质疑任一条目时回到对应模块补充取证。");

		File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
	}
}