﻿﻿using AsmResolver.DotNet;
using AssetRipper.Assets;
using AssetRipper.Export.Configuration;
using AssetRipper.Export.UnityProjects.Project;
using AssetRipper.Import.Logging;
using AssetRipper.Import.Structure.Assembly;
using AssetRipper.Import.Structure.Assembly.Managers;
using AssetRipper.SourceGenerated;
using AssetRipper.SourceGenerated.Classes.ClassID_115;

namespace AssetRipper.Export.UnityProjects.Scripts;

public class ScriptExporter : IAssetExporter
{
	public ScriptExporter(IAssemblyManager assemblyManager, FullConfiguration configuration)
	{
		AssemblyManager = assemblyManager;
		Decompiler = new ScriptDecompiler(AssemblyManager)
		{
			LanguageVersion = configuration.ExportSettings.ScriptLanguageVersion.ToCSharpLanguageVersion(configuration.Version),
			ScriptContentLevel = configuration.ImportSettings.ScriptContentLevel,
			FullyQualifiedTypeNames = configuration.ExportSettings.ScriptTypesFullyQualified,
		};
		ExportMode = configuration.ExportSettings.ScriptExportMode;
		PluginExportMode = configuration.ExportSettings.PluginExportMode;
		ReferenceAssemblyDictionary = ReferenceAssemblies.GetReferenceAssemblies(AssemblyManager, configuration.Version);
	}

	public IAssemblyManager AssemblyManager { get; }
	public ScriptExportMode ExportMode { get; }
	public PluginExportMode PluginExportMode { get; }
	internal ScriptDecompiler Decompiler { get; }
	internal Dictionary<string, UnityGuid> ReferenceAssemblyDictionary { get; }
	private bool HasDecompiled { get; set; } = false;
	private static long MonoScriptDecompiledFileID { get; } = ExportIdHandler.GetMainExportID((int)ClassIDType.MonoScript);

	public bool TryCreateCollection(IUnityObjectBase asset, [NotNullWhen(true)] out IExportCollection? exportCollection)
	{
		if (asset is IMonoScript script)
		{
			if (HasDecompiled)
			{
				exportCollection = new SingleRedirectExportCollection(asset, CreateExportPointer(script));
			}
			else
			{
				HasDecompiled = true;
				if (AssemblyManager.IsSet)
				{
					exportCollection = new ScriptExportCollection(this, script);
				}
				else
				{
					exportCollection = new EmptyScriptExportCollection(this, script);
				}
			}
			return true;
		}
		else
		{
			exportCollection = null;
			return false;
		}
	}

	public AssemblyExportType GetExportType(IMonoScript script)
	{
		return GetExportType(script.GetAssemblyNameFixed());
	}

	public MetaPtr CreateExportPointer(IMonoScript script)
	{
		return GetExportType(script) switch
		{
			AssemblyExportType.Decompile => new(MonoScriptDecompiledFileID, ScriptHashing.CalculateScriptGuid(script), AssetType.Meta),
			AssemblyExportType.Skip => CreateSkipExportPointer(script),
			_ => new(ScriptHashing.CalculateScriptFileID(script), ScriptHashing.CalculateAssemblyGuid(script), AssetType.Meta),
		};
	}

	private MetaPtr CreateSkipExportPointer(IMonoScript script)
	{
		string assemblyName = script.GetAssemblyNameFixed();
		int fileID = ScriptHashing.CalculateScriptFileID(script);
		if (ReferenceAssemblyDictionary.TryGetValue(assemblyName, out UnityGuid guid))
		{
			return new MetaPtr(fileID, guid, AssetType.Meta);
		}
		else
		{
			Logger.Warning(LogCategory.Export, $"Assembly {assemblyName} not in reference dictionary. Using calculated GUID.");
			return new MetaPtr(fileID, ScriptHashing.CalculateAssemblyGuid(script), AssetType.Meta);
		}
	}

	internal bool IsThirdPartyAssembly(string? assemblyName)
	{
		if (ThirdPartyAssemblyDetector.IsThirdPartyByName(assemblyName))
		{
			return true;
		}

		if (!AssemblyManager.IsSet || string.IsNullOrEmpty(assemblyName))
		{
			return false;
		}

		AssemblyDefinition? assembly = AssemblyManager.GetAssemblies().FirstOrDefault(a => a.Name == assemblyName);
		return ThirdPartyAssemblyDetector.IsThirdPartyAssembly(assembly);
	}

	public AssemblyExportType GetExportType(string assemblyName)
	{
		if (assemblyName is not null && ThirdPartyAssemblyDetector.ShouldSkipAssembly(assemblyName))
		{
			Logger.Info(LogCategory.Export, $"Skipping system assembly that Unity already includes: {assemblyName}");
			return AssemblyExportType.Skip;
		}
		else if (assemblyName is not null && ReferenceAssemblyDictionary.ContainsKey(assemblyName))
		{
			return AssemblyExportType.Skip;
		}
		else if (UnityBuiltinAssemblyIdentifier.IsUnityBuiltinAssembly(assemblyName))
		{
			Logger.Info(LogCategory.Export, $"Saving Unity built-in assembly as DLL: {assemblyName}");
			return AssemblyExportType.Save;
		}
		else if (!AssemblyManager.IsSet)
		{
			return AssemblyExportType.Decompile;
		}
		else if (IsThirdPartyAssembly(assemblyName))
		{
			if (PluginExportMode is PluginExportMode.Skip)
			{
				Logger.Info(LogCategory.Export, $"Skipping third-party assembly export: {assemblyName} (plugin export mode: Skip, manual import required)");
				return AssemblyExportType.Skip;
			}
			Logger.Info(LogCategory.Export, $"Saving third-party assembly as DLL: {assemblyName} (plugin export mode: FromGame)");
			return AssemblyExportType.Save;
		}
		else if (ExportMode is ScriptExportMode.Decompiled)
		{
			return AssemblyExportType.Decompile;
		}
		else if (ExportMode is ScriptExportMode.Hybrid)
		{
			return assemblyName is not null && ReferenceAssemblies.IsPredefinedAssembly(assemblyName)
				? AssemblyExportType.Decompile
				: AssemblyExportType.Save;
		}
		else
		{
			return AssemblyExportType.Save;
		}
	}

	AssetType IAssetExporter.ToExportType(IUnityObjectBase asset) => AssetType.Meta;

	bool IAssetExporter.ToUnknownExportType(Type type, out AssetType assetType)
	{
		assetType = AssetType.Meta;
		return true;
	}
}
