using AssetRipper.Assets;
using AssetRipper.Assets.Collections;
using AssetRipper.Import.Logging;
using AssetRipper.Import.Structure.Assembly;
using AssetRipper.Import.Structure.Assembly.Serializable;
using AssetRipper.Processing;
using AssetRipper.SerializationLogic;
using AssetRipper.SourceGenerated.Classes.ClassID_114;
using AssetRipper.SourceGenerated.Classes.ClassID_115;

namespace AssetRipper.Tools.NaninovelAuditor.Modules.C;

/// <summary>
/// C模块原始配置字段提取器（任务11，双通道之 Structure 结构化通道）：
/// 从 GameData 提取全部 Naninovel Configuration MonoBehaviour 的字段全集（点分路径 → 值表）。
/// 字段名来自 SerializableType.Fields，值来自 SerializableStructure.Fields 平行数组。
/// </summary>
internal sealed class MonoBehaviourFieldExtractor
{
	private const string TargetScriptNamespace = "Naninovel";
	private const string TargetScriptAssembly = "Elringus.Naninovel.Runtime";

	/// <summary>提取全部 Configuration 实例：配置名 → (点分路径 → 值字符串) 表。</summary>
	public Dictionary<string, Dictionary<string, string>> ExtractAll(GameData gameData)
	{
		Dictionary<string, Dictionary<string, string>> result = new(StringComparer.OrdinalIgnoreCase);
		List<string> failures = [];

		foreach (SerializedAssetCollection collection in gameData.GameBundle.FetchAssetCollections().OfType<SerializedAssetCollection>())
		{
			foreach (IUnityObjectBase asset in collection)
			{
				if (asset is not IMonoBehaviour monoBehaviour)
				{
					continue;
				}
				string? configName = TryGetConfigurationName(monoBehaviour);
				if (configName is null)
				{
					continue;
				}

				try
				{
					Dictionary<string, string> fields = [];
					if (monoBehaviour.Structure is SerializableStructure structure)
					{
						CollectFields(string.Empty, structure, fields);
					}
					else if (monoBehaviour.Structure is UnloadedStructure unloaded)
					{
						// 惰性加载：UnloadedStructure.LoadStructure() 反射构建 SerializableStructure
						SerializableStructure? loaded = unloaded.LoadStructure();
						if (loaded is not null)
						{
							CollectFields(string.Empty, loaded, fields);
						}
						else
						{
							failures.Add($"{configName}: LoadStructure() returned null");
						}
					}
					else
					{
						failures.Add($"{configName}: Structure is {monoBehaviour.Structure?.GetType().Name ?? "null"} (unsupported)");
					}
					result[configName] = fields;
				}
				catch (Exception ex)
				{
					failures.Add($"{configName}: {ex.Message}");
				}
			}
		}

		foreach (string failure in failures)
		{
			Logger.Warning(LogCategory.System, $"[c-extract] {failure}");
		}
		Logger.Info(LogCategory.System, $"[c-extract] Extracted {result.Count} configurations ({failures.Count} failures).");
		return result;
	}

	private static string? TryGetConfigurationName(IMonoBehaviour monoBehaviour)
	{
		try
		{
			IMonoScript? script = monoBehaviour.ScriptP;
			if (script is null)
			{
				return null;
			}
			string className = script.ClassName_R.String;
			string ns = script.Namespace.String;
			string asm = script.GetValidAssemblyName();
			if (ns == TargetScriptNamespace
				&& asm == TargetScriptAssembly
				&& className.EndsWith("Configuration", StringComparison.Ordinal))
			{
				return className;
			}
			return null;
		}
		catch
		{
			return null;
		}
	}

	/// <summary>递归收集字段树（点分路径 → 值），数组元素以 [i] 后缀展开。</summary>
	private static void CollectFields(string prefix, SerializableStructure structure, Dictionary<string, string> fields)
	{
		SerializableType type = structure.Type;
		for (int i = 0; i < structure.Fields.Length; i++)
		{
			SerializableType.Field etalon = type.Fields[i];
			string path = prefix.Length == 0 ? etalon.Name : $"{prefix}.{etalon.Name}";
			CollectValue(path, in structure.Fields[i], etalon, fields);
		}
	}

	private static void CollectValue(string path, in SerializableValue value, in SerializableType.Field etalon, Dictionary<string, string> fields)
	{
		if (etalon.ArrayDepth > 0)
		{
			fields[path] = value.CValue is System.Collections.ICollection collection
				? $"array[{collection.Count}]"
				: "array[?]";
			return;
		}

		switch (etalon.Type.Type)
		{
			case PrimitiveType.Complex:
				if (value.CValue is SerializableStructure nested)
				{
					CollectFields(path, nested, fields);
				}
				else
				{
					fields[path] = value.CValue?.ToString() ?? "null";
				}
				break;
			case PrimitiveType.String:
				fields[path] = value.AsString;
				break;
			case PrimitiveType.Bool:
				fields[path] = value.AsBoolean ? "1" : "0";
				break;
			case PrimitiveType.Int:
				fields[path] = value.AsInt32.ToString();
				break;
			case PrimitiveType.Long:
				fields[path] = value.AsInt64.ToString();
				break;
			case PrimitiveType.Single:
				fields[path] = value.AsSingle.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
				break;
			case PrimitiveType.Double:
				fields[path] = value.AsDouble.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
				break;
			case PrimitiveType.UInt:
				fields[path] = value.AsUInt32.ToString();
				break;
			default:
				fields[path] = value.CValue?.ToString() ?? value.PValue.ToString();
				break;
		}
	}
}