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

		CopyCgincToNaninovelDir(settings.AssetsPath, fileSystem);

		RedirectDummyShaders(settings.AssetsPath);
	}

	private static void RedirectDummyShaders(string assetsPath)
	{
		string dummyMarker = "//DummyShaderTextExporter";
		var allShaderFiles = new List<(string Path, string ShaderName, bool IsDummy, string Guid)>();

		foreach (string shaderFile in Directory.EnumerateFiles(assetsPath, "*.shader", SearchOption.AllDirectories))
		{
			string content = File.ReadAllText(shaderFile);
			bool isDummy = content.Contains(dummyMarker);
			string shaderName = ExtractShaderName(content);
			string metaPath = shaderFile + ".meta";
			string guid = File.Exists(metaPath) ? ExtractGuid(File.ReadAllText(metaPath)) : "";
			allShaderFiles.Add((shaderFile, shaderName, isDummy, guid));
		}

		int dummyCount = allShaderFiles.Count(f => f.IsDummy);
		int realCount = allShaderFiles.Count(f => !f.IsDummy);
		Logger.Info(LogCategory.Export, $"RedirectDummyShaders: {allShaderFiles.Count} total shaders, {dummyCount} dummy, {realCount} real");


		int redirectedCount = 0;
		int deletedCount = 0;

		foreach (var dummy in allShaderFiles.Where(f => f.IsDummy && !string.IsNullOrEmpty(f.ShaderName) && !string.IsNullOrEmpty(f.Guid)))
		{
			var realShader = allShaderFiles.FirstOrDefault(f => !f.IsDummy && f.ShaderName == dummy.ShaderName);
			if (realShader.Path == null || string.IsNullOrEmpty(realShader.Guid))
			{
				continue;
			}

			Logger.Info(LogCategory.Export, $"Redirecting dummy shader '{dummy.ShaderName}': {dummy.Guid} -> {realShader.Guid}");

			foreach (string matFile in Directory.EnumerateFiles(assetsPath, "*.mat", SearchOption.AllDirectories))
			{
				string matContent = File.ReadAllText(matFile);
				if (!matContent.Contains(dummy.Guid))
				{
					continue;
				}

				string newContent = matContent.Replace(dummy.Guid, realShader.Guid);
				File.WriteAllText(matFile, newContent);
				redirectedCount++;
				Logger.Info(LogCategory.Export, $"  Updated material: {matFile}");
			}

			try
			{
				File.Delete(dummy.Path);
				if (File.Exists(dummy.Path + ".meta"))
				{
					File.Delete(dummy.Path + ".meta");
				}
				deletedCount++;
				Logger.Info(LogCategory.Export, $"  Deleted dummy shader: {dummy.Path}");
			}
			catch (Exception ex)
			{
				Logger.Warning(LogCategory.Export, $"  Failed to delete dummy shader {dummy.Path}: {ex.Message}");
			}
		}

		if (redirectedCount > 0 || deletedCount > 0)
		{
			Logger.Info(LogCategory.Export, $"Dummy shader redirect complete: {redirectedCount} materials updated, {deletedCount} dummy shaders deleted");
		}
	}

	private static void CopyCgincToNaninovelDir(string assetsPath, FileSystem fileSystem)
	{
		try
		{
			string naniShaderDir = fileSystem.Path.Join(assetsPath, NaninovelShaderDestination);
			string tmpShaderDir = fileSystem.Path.Join(assetsPath, TmpShaderDestination);

			if (!Directory.Exists(naniShaderDir) || !Directory.Exists(tmpShaderDir))
			{
				Logger.Warning(LogCategory.Export, $"Cannot copy cginc to Naninovel dir: directories not found: nani={naniShaderDir}, tmp={tmpShaderDir}");
				return;
			}

			foreach (string cgincFile in TmpCginc)
			{
				string srcPath = Path.Combine(tmpShaderDir, cgincFile);
				string dstPath = Path.Combine(naniShaderDir, cgincFile);
				if (File.Exists(srcPath) && !File.Exists(dstPath))
				{
					File.Copy(srcPath, dstPath);
					Logger.Info(LogCategory.Export, $"Copied {cgincFile} to Naninovel shader directory");

					string metaPath = dstPath + ".meta";
					if (!File.Exists(metaPath))
					{
						string newGuid = Guid.NewGuid().ToString("N");
						string metaContent = $"fileFormatVersion: 2\nguid: {newGuid}\nShaderImporter:\n  externalObjects: {{}}\n  defaultTextures: []\n  nonModifiableTextures: []\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n";
						File.WriteAllText(metaPath, metaContent);
					}
				}
			}
		}
		catch (Exception ex)
		{
			Logger.Warning(LogCategory.Export, $"Failed to copy cginc files to Naninovel dir: {ex}");
		}
	}

	private static string ExtractShaderName(string shaderContent)
	{
		int idx = shaderContent.IndexOf("Shader \"");
		if (idx < 0)
		{
			return "";
		}
		idx += "Shader \"".Length;
		int end = shaderContent.IndexOf('"', idx);
		if (end < 0)
		{
			return "";
		}
		return shaderContent.Substring(idx, end - idx);
	}

	private static string ExtractGuid(string metaContent)
	{
		int idx = metaContent.IndexOf("guid: ");
		if (idx < 0)
		{
			return "";
		}
		idx += "guid: ".Length;
		int end = metaContent.IndexOf('\n', idx);
		if (end < 0)
		{
			end = metaContent.Length;
		}
		return metaContent.Substring(idx, end - idx).Trim();
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

			string metaPath = outputPath + ".meta";
			if (!File.Exists(metaPath))
			{
				string newGuid = Guid.NewGuid().ToString("N");
				string metaContent = $"fileFormatVersion: 2\nguid: {newGuid}\nShaderImporter:\n  externalObjects: {{}}\n  defaultTextures: []\n  nonModifiableTextures: []\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n";
				File.WriteAllText(metaPath, metaContent);
				Logger.Info(LogCategory.Export, $"  Generated .meta for {fileName}: guid={newGuid}");
			}

			count++;
		}

		return count;
	}
}