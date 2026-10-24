using AssetRipper.Tools.NaninovelAuditor.Models;
using AssetRipper.Tools.NaninovelAuditor.Reporting;

namespace AssetRipper.Tools.NaninovelAuditor;

/// <summary>
/// 任务2 冒烟子命令：构造覆盖全部枚举值的样例模型 → 落盘 JSON+CSV → 读回反序列化 → 字段一致性断言。
/// </summary>
internal static class ModelsSmokeRunner
{
	public static int Run(AuditOptions options)
	{
		string reportDir = options.ReportDirectory;
		Directory.CreateDirectory(reportDir);

		List<DiffRecord> sampleRecords = BuildSampleDiffRecords();
		PathIdMap samplePathIdMap = BuildSamplePathIdMap();
		SourceInventory sampleSourceInventory = BuildSampleSourceInventory();
		ExportedInventory sampleExportedInventory = BuildSampleExportedInventory();

		Console.WriteLine($"[models-smoke] Sample diff records: {sampleRecords.Count} (covers all enum values)");

		string jsonPath = Path.Combine(reportDir, "models_smoke.json");
		string csvPath = Path.Combine(reportDir, "models_smoke.csv");

		ArtifactWriter.WriteJson(jsonPath, sampleRecords);
		ArtifactWriter.WriteCsv(csvPath,
			["DiffId", "ExportedPath", "SourcePath", "DiffType", "Severity", "RelatedModule", "Detail"],
			sampleRecords.Select(r => new[]
			{
				r.DiffId.ToString(),
				r.ExportedPath,
				r.SourcePath,
				r.DiffType.ToString(),
				r.Severity.ToString(),
				r.RelatedModule.ToString(),
				ArtifactWriter.TruncateFragment(r.Detail),
			}));

		List<DiffRecord> readBack = ArtifactWriter.ReadJson<List<DiffRecord>>(jsonPath);
		bool recordsEqual = readBack.Count == sampleRecords.Count
			&& readBack.OrderBy(r => r.DiffId).SequenceEqual(sampleRecords.OrderBy(r => r.DiffId));

		string mapPath = Path.Combine(reportDir, "models_smoke_pathidmap.json");
		ArtifactWriter.WriteJson(mapPath, samplePathIdMap);
		PathIdMap readBackMap = ArtifactWriter.ReadJson<PathIdMap>(mapPath);
		bool mapEqual = readBackMap.Files.Count == samplePathIdMap.Files.Count
			&& readBackMap.Files.Zip(samplePathIdMap.Files).All(p =>
				p.First.Name == p.Second.Name
				&& p.First.Assets.SequenceEqual(p.Second.Assets));

		string sourceInvPath = Path.Combine(reportDir, "models_smoke_source_inventory.json");
		string exportedInvPath = Path.Combine(reportDir, "models_smoke_exported_inventory.json");
		ArtifactWriter.WriteJson(sourceInvPath, sampleSourceInventory);
		ArtifactWriter.WriteJson(exportedInvPath, sampleExportedInventory);
		SourceInventory readBackSourceInv = ArtifactWriter.ReadJson<SourceInventory>(sourceInvPath);
		ExportedInventory readBackExportedInv = ArtifactWriter.ReadJson<ExportedInventory>(exportedInvPath);
		bool inventoryEqual = readBackSourceInv.Entries.SequenceEqual(sampleSourceInventory.Entries)
			&& readBackExportedInv.Entries.SequenceEqual(sampleExportedInventory.Entries);

		bool allEnumsCovered = VerifyEnumRoundTrip<DiffType>() && VerifyEnumRoundTrip<Severity>()
			&& VerifyEnumRoundTrip<ExportDiffStatus>() && VerifyEnumRoundTrip<NaninovelAssetType>()
			&& VerifyEnumRoundTrip<DiagnosticLevel>() && VerifyEnumRoundTrip<DiagnosticCategory>()
			&& VerifyEnumRoundTrip<Repairability>() && VerifyEnumRoundTrip<ZeroTargetPhase>()
			&& VerifyEnumRoundTrip<ContentMatchLevel>() && VerifyEnumRoundTrip<DuplicateOriginCause>()
			&& VerifyEnumRoundTrip<AuditModule>();

		Console.WriteLine($"[models-smoke] JSON round-trip (DiffRecord list): {(recordsEqual ? "PASS" : "FAIL")}");
		Console.WriteLine($"[models-smoke] JSON round-trip (PathIdMap): {(mapEqual ? "PASS" : "FAIL")}");
		Console.WriteLine($"[models-smoke] JSON round-trip (Inventories): {(inventoryEqual ? "PASS" : "FAIL")}");
		Console.WriteLine($"[models-smoke] Enum string round-trip (all enums): {(allEnumsCovered ? "PASS" : "FAIL")}");
		Console.WriteLine($"[models-smoke] Artifacts: {jsonPath} | {csvPath}");

		return recordsEqual && mapEqual && inventoryEqual && allEnumsCovered ? 0 : 1;
	}

	private static List<DiffRecord> BuildSampleDiffRecords()
	{
		List<DiffRecord> records = [];
		DiffType[] diffTypes = Enum.GetValues<DiffType>();
		Severity[] severities = Enum.GetValues<Severity>();
		AuditModule[] modules = Enum.GetValues<AuditModule>();
		int id = 1;
		// 全部 DiffType 至少出现一次（severity/module 轮换覆盖全部值）
		for (int i = 0; i < diffTypes.Length; i++)
		{
			records.Add(new DiffRecord
			{
				DiffId = id++,
				ExportedPath = $"Assets/Resources/nScripts/sample_{i}.nani",
				SourcePath = $"resources.assets#9{i}00",
				DiffType = diffTypes[i],
				Severity = severities[i % severities.Length],
				RelatedModule = modules[i % modules.Length],
				RepairPlanItemId = i % 2 == 0 ? i : null,
				LinkedLegacyIssue = i % 3 == 0 ? "DialogueTextBlack" : "",
				Detail = $"sample detail {i} with, comma and \"quotes\" for CSV escaping",
			});
		}
		// 补齐全部 severity 与 module 值
		for (int i = 0; i < severities.Length; i++)
		{
			records.Add(new DiffRecord
			{
				DiffId = id++,
				ExportedPath = $"Assets/Resources/nScripts/sev_{i}.nani",
				SourcePath = $"sharedassets0.assets#9{i}01",
				DiffType = diffTypes[i % diffTypes.Length],
				Severity = severities[i],
				RelatedModule = modules[i % modules.Length],
				Detail = $"severity coverage {i}",
			});
		}
		for (int i = 0; i < modules.Length; i++)
		{
			records.Add(new DiffRecord
			{
				DiffId = id++,
				ExportedPath = $"Assets/Resources/nScripts/mod_{i}.nani",
				SourcePath = $"StreamingAssets/aa/StandaloneWindows/bundle#{i}",
				DiffType = diffTypes[i % diffTypes.Length],
				Severity = severities[i % severities.Length],
				RelatedModule = modules[i],
				Detail = $"module coverage {i}",
			});
		}
		return records;
	}

	private static PathIdMap BuildSamplePathIdMap()
	{
		return new PathIdMap
		{
			Files =
			[
				new PathIdMapFile
				{
					Name = "resources.assets",
					Assets =
					[
						new PathIdMapEntry { PathID = 12345, Name = "AB_Cafe", Type = "MonoBehaviour" },
						new PathIdMapEntry { PathID = 67890, Name = "mod_blackmail1", Type = "TextAsset" },
					],
				},
			],
		};
	}

	private static SourceInventory BuildSampleSourceInventory()
	{
		return new SourceInventory
		{
			Entries =
			[
				new SourceInventoryEntry
				{
					AssetType = NaninovelAssetType.NaniScript,
					AssetIdentifier = "AB_Cafe",
					DataSourcePath = "resources.assets#12345",
					ByteLength = 2048,
					Sha256 = "a1b2c3d4e5f6",
					IsAuthoritative = true,
					AuthorityBasis = "Resources path hit",
					PathId = 12345,
					BaselineTextSha256 = "f6e5d4c3b2a1",
				},
				new SourceInventoryEntry
				{
					AssetType = NaninovelAssetType.ManagedText,
					AssetIdentifier = "_synonyms",
					DataSourcePath = "StreamingAssets/text/_synonyms.txt",
					ByteLength = 512,
					Sha256 = "112233445566",
					IsAuthoritative = true,
					AuthorityBasis = "File system source",
					PathId = 0,
				},
			],
		};
	}

	private static ExportedInventory BuildSampleExportedInventory()
	{
		return new ExportedInventory
		{
			Entries =
			[
				new ExportedInventoryEntry
				{
					BaseAssetIdentifier = "AB_Cafe",
					ExportedPath = "Assets/Resources/nScripts/AB_Cafe.nani",
					DuplicateIndex = null,
					ByteLength = 2048,
					Sha256 = "a1b2c3d4e5f6",
					DiffStatus = ExportDiffStatus.ByteIdentical,
					MetaGuid = "00000000000000000000000000000001",
				},
				new ExportedInventoryEntry
				{
					BaseAssetIdentifier = "AB_Cafe",
					ExportedPath = "Assets/Resources/nScripts/AB_Cafe_0.nani",
					DuplicateIndex = 0,
					ByteLength = 2048,
					Sha256 = "a1b2c3d4e5f6",
					DiffStatus = ExportDiffStatus.ByteIdentical,
					MetaGuid = "00000000000000000000000000000002",
				},
				new ExportedInventoryEntry
				{
					BaseAssetIdentifier = "Empty_Case",
					ExportedPath = "Assets/Resources/nScripts/Empty_Case.nani",
					DuplicateIndex = null,
					ByteLength = 0,
					Sha256 = "e3b0c44298fc1c14",
					DiffStatus = ExportDiffStatus.EmptyFilePlaceholder,
				},
			],
		};
	}

	private static bool VerifyEnumRoundTrip<T>() where T : struct, Enum
	{
		foreach (T value in Enum.GetValues<T>())
		{
			string json = System.Text.Json.JsonSerializer.Serialize(value, typeof(T), AuditorSerializerContext.Default);
			object? readBack = System.Text.Json.JsonSerializer.Deserialize(json, typeof(T), AuditorSerializerContext.Default);
			if (readBack is null || !value.Equals(readBack))
			{
				Console.WriteLine($"[models-smoke] Enum round-trip FAILED for {typeof(T).Name}.{value}");
				return false;
			}
		}
		return true;
	}

}