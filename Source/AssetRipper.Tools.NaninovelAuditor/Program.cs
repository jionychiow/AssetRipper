using System.Globalization;
using AssetRipper.Import.Logging;
using AssetRipper.Tools.NaninovelAuditor.Models;
using AssetRipper.Tools.NaninovelAuditor.Modules;
using AssetRipper.Tools.NaninovelAuditor.Session;

namespace AssetRipper.Tools.NaninovelAuditor;

internal static class Program
{
	private const string DefaultSource = "E:/AssetRipper/TomieWGM_Data";
	private const string DefaultExported = "E:/AssetRipper/ExportFinal122";
	private const string DefaultReportDir = "E:/AssetRipper/AuditReports";
	private const string DefaultDecomp = "E:/AssetRipper/NaninovelDecomp";
	private const int DefaultSampleCount = 10;

	private static readonly string[] KnownModules =
	[
		"All", "A", "B", "C", "D", "E", "F",
		"models-smoke", "smoke", "source-inventory", "exported-inventory", "baseline", "c-extract",
	];

	public static int Main(string[] args)
	{
		if (args.Length == 0 || args.Contains("--help") || args.Contains("-h") || args.Contains("/?"))
		{
			PrintUsage();
			return args.Length == 0 ? 1 : 0;
		}

		Dictionary<string, string> options = ParseArguments(args);
		if (options.Count == 0)
		{
			PrintUsage();
			return 1;
		}

		string source = GetOption(options, "source", DefaultSource);
		string exported = GetOption(options, "exported", DefaultExported);
		string reportDir = GetOption(options, "report-dir", DefaultReportDir);
		string module = GetOption(options, "module", "All");
		string decomp = GetOption(options, "decomp", DefaultDecomp);
		string? unity = options.TryGetValue("unity", out string? u) ? u : null;
		string? unityLog = options.TryGetValue("unity-log", out string? ul) ? ul : null;
		int sampleCount = GetIntOption(options, "sample-count", DefaultSampleCount);

		if (!ValidateModules(module))
		{
			Console.Error.WriteLine($"[NaninovelAuditor] Invalid --module value: {module}");
			PrintUsage();
			return 1;
		}

		Console.WriteLine("[NaninovelAuditor] Options:");
		Console.WriteLine($"  source      = {source}");
		Console.WriteLine($"  exported    = {exported}");
		Console.WriteLine($"  report-dir  = {reportDir}");
		Console.WriteLine($"  module      = {module}");
		Console.WriteLine($"  decomp      = {decomp}");
		Console.WriteLine($"  unity       = {unity ?? "(not set)"}");
		Console.WriteLine($"  unity-log   = {unityLog ?? "(not set)"}");
		Console.WriteLine($"  sample-count= {sampleCount}");

		Directory.CreateDirectory(reportDir);

		AuditOptions auditOptions = new()
		{
			SourceDirectory = Path.GetFullPath(source),
			ExportedRoot = Path.GetFullPath(exported),
			ReportDirectory = Path.GetFullPath(reportDir),
			ModuleSelection = module.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
			DecompDirectory = Path.GetFullPath(decomp),
			SampleCount = sampleCount,
			UnityExecutablePath = unity,
			UnityLogPath = unityLog,
		};

		List<string> selection = [.. auditOptions.ModuleSelection.Select(m => m.ToLowerInvariant())];

		if (selection.Contains("models-smoke"))
		{
			return ModelsSmokeRunner.Run(auditOptions);
		}

		if (selection.Contains("exported-inventory"))
		{
			return RunStandalone(new ExportedInventoryModule(), auditOptions);
		}

		bool all = selection.Contains("all");

		if (all || selection.Contains("smoke"))
		{
			Console.WriteLine("[NaninovelAuditor] === smoke ===");
			int rc = RunSessionModule(auditOptions, new SmokeModule());
			if (rc != 0 || !all)
			{
				return rc;
			}
		}
		if (all || selection.Contains("source-inventory"))
		{
			Console.WriteLine("[NaninovelAuditor] === source-inventory ===");
			int rc = RunSessionModule(auditOptions, new SourceInventoryModule());
			if (rc != 0 || !all)
			{
				return rc;
			}
		}
		if (all || selection.Contains("baseline"))
		{
			Console.WriteLine("[NaninovelAuditor] === baseline ===");
			int rc = RunSessionModule(auditOptions, new BaselineModule());
			if (rc != 0 || !all)
			{
				return rc;
			}
		}
		if (all || selection.Contains("a"))
		{
			Console.WriteLine("[NaninovelAuditor] === A (nani inventory + content + duplicate origin) ===");
			int rc = RunSessionModule(auditOptions, new AModule());
			if (rc != 0 || !all)
			{
				return rc;
			}
		}
		if (all || selection.Contains("b"))
		{
			Console.WriteLine("[NaninovelAuditor] === B (text destination) ===");
			int rc = RunSessionModule(auditOptions, new BModule());
			if (rc != 0 || !all)
			{
				return rc;
			}
		}
		if (all || selection.Contains("c") || selection.Contains("c-extract"))
		{
			Console.WriteLine("[NaninovelAuditor] === C (configuration fields + decomp evidence) ===");
			int rc = RunSessionModule(auditOptions, new CModule());
			if (rc != 0 || !all)
			{
				return rc;
			}
		}
		if (all || selection.Contains("d"))
		{
			Console.WriteLine("[NaninovelAuditor] === D (resource type inventory) ===");
			int rc = RunSessionModule(auditOptions, new DModule());
			if (rc != 0 || !all)
			{
				return rc;
			}
		}
		if (all || selection.Contains("e"))
		{
			Console.WriteLine("[NaninovelAuditor] === E (runtime path & color chain) ===");
			int rc = RunSessionModule(auditOptions, new EModule());
			if (rc != 0 || !all)
			{
				return rc;
			}
		}
		if (all || selection.Contains("f"))
		{
			Console.WriteLine("[NaninovelAuditor] === F (diagnostics) ===");
			int rc = RunSessionModule(auditOptions, new FModule());
			if (rc != 0 || !all)
			{
				return rc;
			}
		}
		if (all)
		{
			Console.WriteLine("[NaninovelAuditor] === All (final report & repair plan) ===");
			return RunSessionModule(auditOptions, new AllModule());
		}

		Console.WriteLine("[NaninovelAuditor] No module executed. Available: models-smoke, smoke, source-inventory, exported-inventory, baseline, A, B, C, D, E, F, All.");
		return 0;
	}

	private static int RunStandalone(ExportedInventoryModule module, AuditOptions auditOptions)
	{
		Logger.Add(new ConsoleLogger(false));
		try
		{
			AuditRunResult result = module.Execute(auditOptions);
			Console.WriteLine($"[NaninovelAuditor] Module {result.ModuleName}: {(result.Success ? "OK" : "FAILED")} - {result.Summary}");
			return result.Success ? 0 : 1;
		}
		catch (Exception ex)
		{
			Console.Error.WriteLine($"[NaninovelAuditor] FATAL: {ex.Message}");
			return 2;
		}
	}

	private static int RunSessionModule(AuditOptions auditOptions, INaninovelAuditModule auditModule)
	{
		Logger.Add(new ConsoleLogger(false));
		try
		{
			using AuditSession session = AuditSession.Open(auditOptions);
			AuditRunResult result = auditModule.Execute(session);
			Console.WriteLine($"[NaninovelAuditor] Module {result.ModuleName}: {(result.Success ? "OK" : "FAILED")} - {result.Summary}");
			return result.Success ? 0 : 1;
		}
		catch (Exception ex)
		{
			Console.Error.WriteLine($"[NaninovelAuditor] FATAL: {ex.Message}");
			return 2;
		}
	}

	private static Dictionary<string, string> ParseArguments(string[] args)
	{
		Dictionary<string, string> result = new();
		for (int i = 0; i < args.Length; i++)
		{
			string arg = args[i];
			if (!arg.StartsWith("--"))
			{
				Console.Error.WriteLine($"[NaninovelAuditor] Unrecognized argument: {arg}");
				return [];
			}

			string key;
			string value;
			int eq = arg.IndexOf('=');
			if (eq > 0)
			{
				key = arg[2..eq];
				value = arg[(eq + 1)..];
			}
			else
			{
				key = arg[2..];
				if (i + 1 >= args.Length || args[i + 1].StartsWith("--"))
				{
					Console.Error.WriteLine($"[NaninovelAuditor] Missing value for --{key}");
					return [];
				}
				value = args[++i];
			}

			result[key] = value;
		}
		return result;
	}

	private static string GetOption(Dictionary<string, string> options, string key, string fallback)
	{
		return options.TryGetValue(key, out string? value) && !string.IsNullOrWhiteSpace(value) ? value : fallback;
	}

	private static int GetIntOption(Dictionary<string, string> options, string key, int fallback)
	{
		if (options.TryGetValue(key, out string? value)
			&& int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
			&& parsed > 0)
		{
			return parsed;
		}
		return fallback;
	}

	private static bool ValidateModules(string module)
	{
		if (string.IsNullOrWhiteSpace(module))
		{
			return false;
		}
		foreach (string part in module.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
		{
			if (!KnownModules.Contains(part, StringComparer.OrdinalIgnoreCase))
			{
				return false;
			}
		}
		return true;
	}

	private static void PrintUsage()
	{
		Console.WriteLine($"Usage: {Path.GetFileName(Environment.ProcessPath)} [options]");
		Console.WriteLine();
		Console.WriteLine("Naninovel export integrity auditor (read-only audit of source data vs exported project).");
		Console.WriteLine();
		Console.WriteLine("Options:");
		Console.WriteLine($"  --source <path>        Source game data directory. Default: {DefaultSource}");
		Console.WriteLine($"  --exported <path>      Exported project root directory. Default: {DefaultExported}");
		Console.WriteLine($"  --report-dir <path>    Audit report output directory. Default: {DefaultReportDir}");
		Console.WriteLine("  --module <list>        Comma-separated module list: A,B,C,D,E,F,All");
		Console.WriteLine("                         plus smoke modules: models-smoke, smoke, source-inventory,");
		Console.WriteLine("                         exported-inventory, baseline, c-extract. Default: All");
		Console.WriteLine($"  --decomp <path>        NaninovelDecomp decompiled source directory. Default: {DefaultDecomp}");
		Console.WriteLine("  --unity <path>         Unity 2019.4.35f1 executable path (F module batchmode collection).");
		Console.WriteLine("  --unity-log <path>     Existing Editor.log path (F module manual channel).");
		Console.WriteLine($"  --sample-count <n>     Sample count for content comparison. Default: {DefaultSampleCount}");
		Console.WriteLine("  --help                 Show this help.");
	}
}