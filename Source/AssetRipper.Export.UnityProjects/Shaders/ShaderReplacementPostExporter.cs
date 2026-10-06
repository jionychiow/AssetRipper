using AssetRipper.Export.Configuration;
using AssetRipper.Import.Logging;
using AssetRipper.Processing;
using System.IO;
using System.Reflection;

namespace AssetRipper.Export.UnityProjects.Shaders;

public sealed class ShaderReplacementPostExporter : IPostExporter
{
	private const string NaninovelShaderDestination = "Resources/naninovel/shaders";
	private const string TmpShaderDestination = "TextMesh Pro/Resources/Shaders";

	private static readonly string[] NaninovelShaders =
	[
		"transitionaltexture.shader",
		"transitionalsprite.shader",
		"transitionalui.shader",
		"transparent.shader",
		"blurfilter.shader",
		"depthmask.shader",
		"depthoffield.shader",
		"digitalglitch.shader",
		"revealabletext.shader",
		"revealabletmprotext.shader",
		"revealabletmprosprite.shader",
		"revealabletmprotextrtl.shader",
	];

	private static readonly string[] TmpShaders =
	[
		"TMP_Bitmap.shader",
		"TMP_Bitmap-Custom-Atlas.shader",
		"TMP_Bitmap-Mobile.shader",
		"TMP_SDF.shader",
		"TMP_SDF Overlay.shader",
		"TMP_SDF-Mobile.shader",
		"TMP_SDF-Mobile Overlay.shader",
		"TMP_SDF-Mobile Masking.shader",
		"TMP_SDF-Surface.shader",
		"TMP_SDF-Surface-Mobile.shader",
		"TMP_Sprite.shader",
	];

	private static readonly string[] TmpCginc =
	[
		"TMPro.cginc",
		"TMPro_Properties.cginc",
		"TMPro_Surface.cginc",
	];

	public void DoPostExport(GameData gameData, FullConfiguration settings, FileSystem fileSystem)
	{
		Console.WriteLine($"[ShaderReplacementPostExporter] Starting... AssetsPath={settings.AssetsPath}");
		Assembly assembly = typeof(ShaderReplacementPostExporter).Assembly;

		int naninovelCount = WriteEmbeddedShaders(assembly, "AssetRipper.Export.UnityProjects.Shaders.Resources.Naninovel.", NaninovelShaders, settings.AssetsPath, NaninovelShaderDestination, fileSystem);
		int tmpShaderCount = WriteEmbeddedShaders(assembly, "AssetRipper.Export.UnityProjects.Shaders.Resources.TMP.", TmpShaders, settings.AssetsPath, TmpShaderDestination, fileSystem);
		int tmpCgincCount = WriteEmbeddedShaders(assembly, "AssetRipper.Export.UnityProjects.Shaders.Resources.TMP.", TmpCginc, settings.AssetsPath, TmpShaderDestination, fileSystem);

		Console.WriteLine($"[ShaderReplacementPostExporter] wrote {naninovelCount} Naninovel, {tmpShaderCount} TMP shaders, {tmpCgincCount} cginc");
		Logger.Info(LogCategory.Export, $"ShaderReplacementPostExporter: wrote {naninovelCount} Naninovel shaders, {tmpShaderCount} TMP shaders, {tmpCgincCount} TMP cginc files.");
	}

	private static int WriteEmbeddedShaders(Assembly assembly, string resourcePrefix, string[] fileNames, string assetsPath, string destinationRelative, FileSystem fileSystem)
	{
		int count = 0;
		string destinationDir = fileSystem.Path.Join(assetsPath, destinationRelative);

		if (!fileSystem.Directory.Exists(destinationDir))
		{
			Directory.CreateDirectory(destinationDir);
		}

		foreach (string fileName in fileNames)
		{
			string resourceName = resourcePrefix + fileName;
			using Stream? stream = assembly.GetManifestResourceStream(resourceName);
			if (stream is null)
			{
				Logger.Warning(LogCategory.Export, $"Embedded shader resource not found: {resourceName}");
				continue;
			}

			using StreamReader reader = new StreamReader(stream);
			string content = reader.ReadToEnd();

			string outputPath = fileSystem.Path.Join(destinationDir, fileName);
			File.WriteAllText(outputPath, content);
			count++;
		}

		return count;
	}
}