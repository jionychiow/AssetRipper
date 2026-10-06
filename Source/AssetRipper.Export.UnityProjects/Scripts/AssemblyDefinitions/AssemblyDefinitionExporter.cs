using AsmResolver.DotNet;
using AssetRipper.Import.Logging;
using System.Text.Json;

namespace AssetRipper.Export.UnityProjects.Scripts.AssemblyDefinitions;

public static class AssemblyDefinitionExporter
{
	public static void Export(AssemblyDefinitionDetails details, FileSystem fileSystem, Dictionary<string, UnityGuid> referenceAssemblies)
	{
		string assetPath = fileSystem.Path.Join(details.OutputFolder, $"{details.AssemblyName}.asmdef");

		AssemblyDefinitionAsset asset = new AssemblyDefinitionAsset(details.AssemblyName);
		ModuleDefinition? module = details.Assembly?.ManifestModule;
		int skippedCount = 0;
		if (module is not null)
		{
			foreach (AssemblyReference reference in module.AssemblyReferences)
			{
				if (reference.Name is null || referenceAssemblies.ContainsKey(reference.Name))
				{
					skippedCount++;
					continue;
				}

				asset.References.Add(reference.Name);
			}
		}

		string assetData = JsonSerializer.Serialize(asset, AssemblyDefinitionSerializerContext.Default.AssemblyDefinitionAsset);
		fileSystem.File.WriteAllText(assetPath, assetData);

		Logger.Info(LogCategory.Export, $"Generated .asmdef for '{details.AssemblyName}': {asset.References.Count} references, {skippedCount} framework references skipped");
	}
}
