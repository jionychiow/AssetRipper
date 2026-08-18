using AssetRipper.Assets;
using AssetRipper.Export.Modules.Naninovel;
using AssetRipper.Import.Logging;
using AssetRipper.Import.Structure.Assembly;
using AssetRipper.Import.Structure.Assembly.Serializable;
using AssetRipper.IO.Endian;
using AssetRipper.SourceGenerated.Classes.ClassID_114;
using AssetRipper.SourceGenerated.Classes.ClassID_115;

namespace AssetRipper.Export.UnityProjects.Naninovel;

public class NaninovelScriptExporter : BinaryAssetExporter
{
	private const string TargetScriptClassName = "Script";
	private const string TargetScriptNamespace = "Naninovel";
	private const string TargetScriptAssembly = "Elringus.Naninovel.Runtime";

	public override bool TryCreateCollection(IUnityObjectBase asset, [NotNullWhen(true)] out IExportCollection? exportCollection)
	{
		if (asset is not IMonoBehaviour monoBehaviour)
		{
			exportCollection = null;
			return false;
		}

		if (!IsNaninovelScript(monoBehaviour))
		{
			exportCollection = null;
			return false;
		}

		exportCollection = new NaninovelScriptExportCollection(this, monoBehaviour);
		return true;
	}


	private static bool IsNaninovelScript(IMonoBehaviour monoBehaviour)
	{
		try
		{
			IMonoScript? script = monoBehaviour.ScriptP;
			if (script is null)
			{
				Logger.Warning(LogCategory.Export, $"[NaninovelExport] MonoBehaviour '{monoBehaviour.GetBestName()}' (PathID={monoBehaviour.PathID}) has null ScriptP.");
				return false;
			}

			string className = script.ClassName_R.String;
			string ns = script.Namespace.String;
			string asm = script.GetValidAssemblyName();

			bool isMatch = className == TargetScriptClassName
				&& ns == TargetScriptNamespace
				&& asm == TargetScriptAssembly;

			if (isMatch)
			{
				Logger.Info(LogCategory.Export, $"[NaninovelExport] MATCH: '{monoBehaviour.GetBestName()}' (PathID={monoBehaviour.PathID}) -> Class={className}, NS={ns}, Asm={asm}.");
			}
			else if (className == TargetScriptClassName || ns.Contains("Naninovel") || asm.Contains("Naninovel"))
			{
				Logger.Info(LogCategory.Export, $"[NaninovelExport] NEAR-MISS: '{monoBehaviour.GetBestName()}' (PathID={monoBehaviour.PathID}) -> Class={className}, NS={ns}, Asm={asm}.");
			}

			return isMatch;
		}
		catch (Exception ex)
		{
			Logger.Warning(LogCategory.Export, $"[NaninovelExport] Failed to resolve m_Script for MonoBehaviour '{monoBehaviour.GetBestName()}' (PathID={monoBehaviour.PathID}): {ex.Message}");
			return false;
		}
	}
}
